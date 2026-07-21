// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// R3: identity round trip. The forward exporter stamps full SAM Guids into
    /// OS:AdditionalProperties; these tests hold that channel to its contract — restore what is
    /// there, never trust the 8-character name suffix as an identity, and import a third-party
    /// OSM that has none of it without complaint.
    /// </summary>
    [TestFixture]
    public class IdentityRoundTripTests
    {
        [Test]
        public void ForwardConversion_StampsFullSamGuidOnExportedObjects()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();
            Space sourceSpace = source.AdjacencyCluster.GetSpaces()[0];

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                global::OpenStudio.Space openStudioSpace = conversionResult.Model.getSpaces()[0];

                Guid guid;
                Assert.That(Core.OpenStudio.Query.TryGetSAMGuid(openStudioSpace, out guid), Is.True, "Every exported object must carry its full SAM Guid");
                Assert.That(guid, Is.EqualTo(sourceSpace.Guid));

                string type;
                Assert.That(Core.OpenStudio.Query.TryGetSAMType(openStudioSpace, out type), Is.True);
                Assert.That(type, Is.EqualTo("Space"));

                string name;
                Assert.That(Core.OpenStudio.Query.TryGetSAMName(openStudioSpace, out name), Is.True);
                Assert.That(name, Is.EqualTo(sourceSpace.Name), "The unsanitised SAM name must survive, unlike the sanitised object name");
            }
        }

        [Test]
        public void ForwardConversion_StampsTheModelGuidOnTheBuilding()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                Guid guid;
                Assert.That(Core.OpenStudio.Query.TryGetSAMGuid(conversionResult.Model.getBuilding(), out guid), Is.True);
                Assert.That(guid, Is.EqualTo(source.Guid));
            }
        }

        [Test]
        public void Import_RestoresFullSpaceAndPanelGuids()
        {
            AnalyticalModel source = AnalyticalModelFixtures.TwoAdjacentBoxes();

            HashSet<Guid> sourceSpaceGuids = new HashSet<Guid>(source.AdjacencyCluster.GetSpaces().Select(x => x.Guid));
            HashSet<Guid> sourcePanelGuids = new HashSet<Guid>(source.AdjacencyCluster.GetPanels().Select(x => x.Guid));

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                OpenStudioImportResult result = conversionResult.Model.ToSAM();

                foreach (Space space in result.AnalyticalModel.AdjacencyCluster.GetSpaces())
                {
                    Assert.That(sourceSpaceGuids.Contains(space.Guid), Is.True, $"Space Guid {space.Guid} was not restored from the source model");
                }

                foreach (Panel panel in result.AnalyticalModel.AdjacencyCluster.GetPanels())
                {
                    Assert.That(sourcePanelGuids.Contains(panel.Guid), Is.True, $"Panel Guid {panel.Guid} was not restored from the source model");
                }
            }
        }

        [Test]
        [Repeat(10)]
        public void Import_RestoresPanelGuids_RegardlessOfSurfaceOrder()
        {
            // Regression guard. Identity used to be stamped only on the PRIMARY surface of an
            // internal panel, so whether the shared wall kept its Guid depended on which of its
            // two surfaces the importer happened to meet first — and OpenStudio's handle ordering
            // is not stable between runs. Repeating the round trip is what exposed it.
            AnalyticalModel source = AnalyticalModelFixtures.TwoAdjacentBoxes();
            HashSet<Guid> sourcePanelGuids = new HashSet<Guid>(source.AdjacencyCluster.GetPanels().Select(x => x.Guid));

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                OpenStudioImportResult result = conversionResult.Model.ToSAM();

                foreach (Panel panel in result.AnalyticalModel.AdjacencyCluster.GetPanels())
                {
                    Assert.That(sourcePanelGuids.Contains(panel.Guid), Is.True, $"Panel Guid {panel.Guid} was not restored; identity must not depend on surface order");
                }
            }
        }

        [Test]
        public void Import_RestoresTheModelLevelGuid()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                OpenStudioImportResult result = conversionResult.Model.ToSAM();

                Assert.That(result.AnalyticalModel.Guid, Is.EqualTo(source.Guid));
            }
        }

        [Test]
        public void Import_WithIdentityRestorationDisabled_IssuesFreshGuids()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();
            Guid sourceSpaceGuid = source.AdjacencyCluster.GetSpaces()[0].Guid;

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                OpenStudioImportResult result = conversionResult.Model.ToSAM(new Core.OpenStudio.OpenStudioImportOptions { RestoreSAMIdentity = false });

                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].Guid, Is.Not.EqualTo(sourceSpaceGuid));
            }
        }

        [Test]
        public void ThirdPartyModel_WithoutSamMetadata_ImportsWithoutIdentityDiagnostics()
        {
            using (global::OpenStudio.Model model = BuildThirdPartyModel())
            {
                OpenStudioImportResult result = model.ToSAM();

                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                Assert.That(result.Successful, Is.True, "An OSM with no SAM metadata is the normal third-party case");
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces().Count, Is.EqualTo(1));
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetPanels().Count, Is.EqualTo(6));

                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision), Is.False, "The absence of identity metadata is not a problem and must not be reported as one");
            }
        }

        [Test]
        public void MalformedIdentityMetadata_IsReportedAndReplacedWithAFreshGuid()
        {
            using (global::OpenStudio.Model model = BuildThirdPartyModel())
            {
                model.getSpaces()[0].additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.Guid, "not-a-guid");

                OpenStudioImportResult result = model.ToSAM();

                Assert.That(result.Successful, Is.True, "Bad metadata degrades the identity, not the import");
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision), Is.True);
            }
        }

        [Test]
        public void DuplicateIdentityMetadata_IsReportedAndOnlyOneObjectKeepsTheGuid()
        {
            AnalyticalModel source = AnalyticalModelFixtures.TwoAdjacentBoxes();

            using (OpenStudioConversionResult conversionResult = source.ToOpenStudio())
            {
                global::OpenStudio.SpaceVector spaces = conversionResult.Model.getSpaces();

                Guid duplicated;
                Assert.That(Core.OpenStudio.Query.TryGetSAMGuid(spaces[0], out duplicated), Is.True);

                // Force the collision the audit requires a warning for.
                spaces[1].additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.Guid, duplicated.ToString("D"));

                OpenStudioImportResult result = conversionResult.Model.ToSAM();

                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision), Is.True);

                List<Space> importedSpaces = result.AnalyticalModel.AdjacencyCluster.GetSpaces();
                Assert.That(importedSpaces.Count, Is.EqualTo(2));
                Assert.That(importedSpaces[0].Guid, Is.Not.EqualTo(importedSpaces[1].Guid), "Two SAM objects must never share one Guid");
            }
        }

        [Test]
        public void IdentityOfAWrongType_IsRejected()
        {
            using (global::OpenStudio.Model model = BuildThirdPartyModel())
            {
                Guid guid = Guid.NewGuid();
                global::OpenStudio.Space space = model.getSpaces()[0];
                space.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.Guid, guid.ToString("D"));
                space.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.Type, "Panel");

                OpenStudioImportResult result = model.ToSAM();

                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].Guid, Is.Not.EqualTo(guid), "A Panel's identity must not be restored onto a Space");
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision), Is.True);
            }
        }

        [Test]
        public void GuidSuffixMatching_IsNotTreatedAsAnIdentity()
        {
            // The deterministic name suffix is 8 hex characters — 32 bits of a 128-bit Guid.
            // Two distinct Guids can share it, and the importer must not restore identity from it.
            Guid guid = Guid.NewGuid();
            string name = Core.OpenStudio.Query.OpenStudioName("Space", "Office", guid);

            string suffix;
            Assert.That(Query.TryGetGuidSuffix(name, out suffix), Is.True);
            Assert.That(suffix.Length, Is.EqualTo(8));

            using (global::OpenStudio.Model model = BuildThirdPartyModel())
            {
                model.getSpaces()[0].setName(name);

                OpenStudioImportResult result = model.ToSAM();

                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].Guid, Is.Not.EqualTo(guid), "A name suffix is a matching key, never a restorable identity");
            }
        }

        /// <summary>
        /// Builds a minimal single-room model directly through the OpenStudio API, with no SAM
        /// metadata anywhere — the shape of an OSM authored in the OpenStudio Application or by a
        /// third-party tool.
        /// </summary>
        private static global::OpenStudio.Model BuildThirdPartyModel()
        {
            global::OpenStudio.Model model = new global::OpenStudio.Model();

            global::OpenStudio.Space space = new global::OpenStudio.Space(model);
            space.setName("Third Party Room");

            global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
            thermalZone.setName("Third Party Zone");
            space.setThermalZone(thermalZone);

            const double width = 4;
            const double depth = 3;
            const double height = 2.7;

            AddSurface(model, space, "Floor", "Ground", new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 0.0, depth, 0.0 }, new[] { width, depth, 0.0 }, new[] { width, 0.0, 0.0 } });
            AddSurface(model, space, "RoofCeiling", "Outdoors", new[] { new[] { 0.0, 0.0, height }, new[] { width, 0.0, height }, new[] { width, depth, height }, new[] { 0.0, depth, height } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { 0.0, 0.0, 0.0 }, new[] { width, 0.0, 0.0 }, new[] { width, 0.0, height }, new[] { 0.0, 0.0, height } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { width, 0.0, 0.0 }, new[] { width, depth, 0.0 }, new[] { width, depth, height }, new[] { width, 0.0, height } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { width, depth, 0.0 }, new[] { 0.0, depth, 0.0 }, new[] { 0.0, depth, height }, new[] { width, depth, height } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { 0.0, depth, 0.0 }, new[] { 0.0, 0.0, 0.0 }, new[] { 0.0, 0.0, height }, new[] { 0.0, depth, height } });

            return model;
        }

        private static void AddSurface(global::OpenStudio.Model model, global::OpenStudio.Space space, string surfaceType, string boundaryCondition, double[][] vertices)
        {
            global::OpenStudio.Point3dVector point3dVector = new global::OpenStudio.Point3dVector();
            foreach (double[] vertex in vertices)
            {
                point3dVector.Add(new global::OpenStudio.Point3d(vertex[0], vertex[1], vertex[2]));
            }

            global::OpenStudio.Surface surface = new global::OpenStudio.Surface(point3dVector, model);
            surface.setSpace(space);
            surface.setSurfaceType(surfaceType);
            surface.setOutsideBoundaryCondition(boundaryCondition);
        }
    }
}
