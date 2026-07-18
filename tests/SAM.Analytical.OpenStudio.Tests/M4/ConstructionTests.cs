// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M4: material and construction conversion, layer order and orientation (plan §9–§10).</summary>
    [TestFixture]
    public class ConstructionTests
    {
        private OpenStudioConversionResult result;
        private global::OpenStudio.Model model;

        [OneTimeSetUp]
        public void Convert()
        {
            result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            model = result.Model;
        }

        private static List<string> LayerNames(global::OpenStudio.Surface surface)
        {
            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = surface.construction();
            Assert.That(optionalConstructionBase != null && !optionalConstructionBase.isNull(), $"Surface {surface.nameString()} must have a construction");

            global::OpenStudio.OptionalConstruction optionalConstruction = optionalConstructionBase.get().to_Construction();
            Assert.That(optionalConstruction != null && !optionalConstruction.isNull(), "ConstructionBase must be a layered Construction");

            List<string> names = new List<string>();
            foreach (global::OpenStudio.Material material in optionalConstruction.get().layers())
            {
                names.Add(material.nameString());
            }

            return names;
        }

        [Test]
        public void Conversion_IsValid_AllSurfacesHaveConstructions()
        {
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True);

            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                global::OpenStudio.OptionalConstructionBase constructionBase = surface.construction();
                Assert.That(constructionBase != null && !constructionBase.isNull(), $"{surface.nameString()} has no construction");
            }
        }

        [Test]
        public void ExternalWall_LayersOutsideFirst()
        {
            global::OpenStudio.Surface externalWall = null;
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                if (surface.outsideBoundaryCondition() == "Outdoors" && surface.surfaceType() == "Wall")
                {
                    externalWall = surface;
                    break;
                }
            }

            Assert.That(externalWall, Is.Not.Null);

            List<string> layerNames = LayerNames(externalWall);
            Assert.That(layerNames.Count, Is.EqualTo(3));
            Assert.That(layerNames[0], Does.Contain("Brick"), "EnergyPlus outside layer must be the SAM outermost (last stored) layer");
            Assert.That(layerNames[1], Does.Contain("Insulation"));
            Assert.That(layerNames[2], Does.Contain("Plasterboard"), "EnergyPlus inside layer must be the SAM innermost (first stored) layer");
        }

        [Test]
        public void InternalWall_OppositeSides_HaveReversedLayerSequences()
        {
            List<global::OpenStudio.Surface> internalSurfaces = new List<global::OpenStudio.Surface>();
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                if (surface.outsideBoundaryCondition() == "Surface")
                {
                    internalSurfaces.Add(surface);
                }
            }

            Assert.That(internalSurfaces.Count, Is.EqualTo(2));

            List<string> side0 = LayerNames(internalSurfaces[0]);
            List<string> side1 = LayerNames(internalSurfaces[1]);

            List<string> side1Reversed = new List<string>(side1);
            side1Reversed.Reverse();

            Assert.That(side0, Is.EqualTo(side1Reversed), "Internal panel sides must carry exact layer reversals of each other");
            Assert.That(side0, Is.Not.EqualTo(side1), "Sides must not share the same layer direction");
        }

        [Test]
        public void Materials_AreDeduplicated_ByGuidAndThickness()
        {
            Assert.That(model.getStandardOpaqueMaterials().Count, Is.EqualTo(3), "Brick, Insulation, Plasterboard — one instance each, shared by forward and reverse constructions");
            Assert.That(model.getStandardGlazings().Count, Is.EqualTo(1), "Glass used twice at the same thickness must be one instance");
            Assert.That(model.getGass().Count, Is.EqualTo(1), "One air gap material");
        }

        [Test]
        public void OpaqueMaterial_PhysicalValues_ArePreserved()
        {
            global::OpenStudio.StandardOpaqueMaterial brick = null;
            foreach (global::OpenStudio.StandardOpaqueMaterial material in model.getStandardOpaqueMaterials())
            {
                if (material.nameString().Contains("Brick"))
                {
                    brick = material;
                    break;
                }
            }

            Assert.That(brick, Is.Not.Null);
            Assert.That(brick.thickness(), Is.EqualTo(0.1).Within(1e-9));
            Assert.That(brick.thermalConductivity(), Is.EqualTo(0.84).Within(1e-9));
            Assert.That(brick.density(), Is.EqualTo(1700).Within(1e-9));
            Assert.That(brick.specificHeat(), Is.EqualTo(800).Within(1e-9));
            Assert.That(brick.thermalAbsorptance(), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(brick.solarAbsorptance(), Is.EqualTo(0.7).Within(1e-9), "1 − ExternalSolarReflectance(0.3)");
            Assert.That(brick.visibleAbsorptance(), Is.EqualTo(0.7).Within(1e-9), "1 − ExternalLightReflectance(0.3)");
        }

        [Test]
        public void Window_GetsDoubleGlazedPaneConstruction()
        {
            global::OpenStudio.SubSurfaceVector subSurfaces = model.getSubSurfaces();
            Assert.That(subSurfaces.Count, Is.EqualTo(1));

            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = subSurfaces[0].construction();
            Assert.That(optionalConstructionBase != null && !optionalConstructionBase.isNull());

            global::OpenStudio.OptionalConstruction optionalConstruction = optionalConstructionBase.get().to_Construction();
            Assert.That(optionalConstruction != null && !optionalConstruction.isNull());

            List<string> names = new List<string>();
            foreach (global::OpenStudio.Material material in optionalConstruction.get().layers())
            {
                names.Add(material.nameString());
            }

            Assert.That(names.Count, Is.EqualTo(3), "Glass / Air / Glass");
            Assert.That(names[0], Does.Contain("Glass"));
            Assert.That(names[1], Does.Contain("Air"));
            Assert.That(names[2], Does.Contain("Glass"));
        }

        [Test]
        public void MissingMaterial_RaisesError_NeverSubstitutes()
        {
            Construction badConstruction = new Construction(new Guid("99999999-0000-0000-0000-000000000001"), "Bad Construction", new List<ConstructionLayer>
            {
                new ConstructionLayer("DoesNotExist", 0.1),
            });

            OpenStudioConversionResult badResult = AnalyticalModelFixtures.SingleBox(badConstruction, includeWindow: false).ToOpenStudio();

            Assert.That(badResult.IsValid, Is.False, "Missing material must invalidate the conversion");
            Assert.That(badResult.Diagnostics.Any(d => d.Code == "SAM-OS-CON-001" && d.Message.Contains("DoesNotExist")), Is.True, "Diagnostic must name the missing material");
        }
    }
}
