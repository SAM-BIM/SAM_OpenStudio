// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M1: diagnostic aggregation, IsValid and context registration contract tests.</summary>
    [TestFixture]
    public class DiagnosticsTests
    {
        [Test]
        public void Warnings_DoNotInvalidate_ErrorsDo()
        {
            OpenStudioConversionContext context = new OpenStudioConversionContext(null, new global::OpenStudio.Model());

            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Removed 2 collinear vertices");
            Assert.That(context.HasErrors, Is.False);
            Assert.That(new OpenStudioConversionResult(context).IsValid, Is.True, "Warnings must not invalidate the result");

            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Construction layer material not found");
            Assert.That(context.HasErrors, Is.True);
            Assert.That(new OpenStudioConversionResult(context).IsValid, Is.False, "Errors must invalidate the result");
        }

        [Test]
        public void Result_IsASnapshot_OfDiagnosticsAndObjectMap()
        {
            OpenStudioConversionContext context = new OpenStudioConversionContext(null, new global::OpenStudio.Model());
            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "First");

            OpenStudioConversionResult result = new OpenStudioConversionResult(context);
            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Second");

            Assert.That(result.Diagnostics.Count, Is.EqualTo(1), "Result must snapshot diagnostics at creation time");
            Assert.That(result.IsValid, Is.True, "Later context errors must not affect an existing result");
        }

        [Test]
        public void Diagnostic_CarriesSamObjectIdentity()
        {
            OpenStudioConversionContext context = new OpenStudioConversionContext(null, new global::OpenStudio.Model());
            Space space = new Space("Office 04");

            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Profile missing", space, "SAM_Space_Office_04_xxxxxxxx");

            Core.OpenStudio.OpenStudioDiagnostic diagnostic = context.Diagnostics[0];
            Assert.That(diagnostic.SamGuid, Is.EqualTo(space.Guid));
            Assert.That(diagnostic.SamObjectType, Is.EqualTo("Space"));
            Assert.That(diagnostic.Code, Is.EqualTo("SAM-OS-SCH-001"));
            Assert.That(diagnostic.OpenStudioObjectName, Is.EqualTo("SAM_Space_Office_04_xxxxxxxx"));
        }

        [Test]
        public void RegisterModelObject_TracksAndRejectsDuplicates()
        {
            global::OpenStudio.Model model = new global::OpenStudio.Model();
            OpenStudioConversionContext context = new OpenStudioConversionContext(null, model);

            Space samSpace = new Space("Office 04");
            global::OpenStudio.Space openStudioSpace = new global::OpenStudio.Space(model);
            openStudioSpace.setName(Core.OpenStudio.Query.OpenStudioName(samSpace));

            Assert.That(context.RegisterModelObject(samSpace, openStudioSpace), Is.True);
            Assert.That(context.RegisterModelObject(samSpace, openStudioSpace), Is.False, "Duplicate registration must be rejected");
            Assert.That(context.References.Count, Is.EqualTo(1));
            Assert.That(context.References.OpenStudioObjectName(samSpace.Guid), Does.StartWith("SAM_Space_Office_04_"));

            Assert.That(context.TryGetModelObject(samSpace.Guid, out global::OpenStudio.Space retrieved), Is.True);
            Assert.That(retrieved.nameString(), Is.EqualTo(openStudioSpace.nameString()));
        }
    }
}
