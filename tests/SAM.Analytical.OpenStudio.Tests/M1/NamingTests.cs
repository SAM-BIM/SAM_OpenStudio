// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M1: deterministic naming and sanitization contract tests.</summary>
    [TestFixture]
    public class NamingTests
    {
        [Test]
        public void SanitizeName_ReplacesForbiddenAndWhitespace()
        {
            Assert.That(Core.OpenStudio.Query.SanitizeName("Office 04"), Is.EqualTo("Office_04"));
            Assert.That(Core.OpenStudio.Query.SanitizeName("A,B;C!D"), Is.EqualTo("A_B_C_D"));
            Assert.That(Core.OpenStudio.Query.SanitizeName("  padded  "), Is.EqualTo("padded"));
            Assert.That(Core.OpenStudio.Query.SanitizeName("a\r\n\tb"), Is.EqualTo("a_b"));
            Assert.That(Core.OpenStudio.Query.SanitizeName("a __ b"), Is.EqualTo("a_b"));
            Assert.That(Core.OpenStudio.Query.SanitizeName(""), Is.EqualTo(""));
            Assert.That(Core.OpenStudio.Query.SanitizeName(null), Is.Null);
        }

        [Test]
        public void OpenStudioName_IsDeterministicAndFormatted()
        {
            Guid guid = new Guid("72a6f932-0000-0000-0000-000000000000");

            string first = Core.OpenStudio.Query.OpenStudioName("Space", "Office 04", guid);
            string second = Core.OpenStudio.Query.OpenStudioName("Space", "Office 04", guid);

            Assert.That(first, Is.EqualTo("SAM_Space_Office_04_72a6f932"));
            Assert.That(second, Is.EqualTo(first), "Naming must be deterministic");
        }

        [Test]
        public void OpenStudioName_OmitsEmptyName()
        {
            Guid guid = new Guid("b42bd811-0000-0000-0000-000000000000");
            Assert.That(Core.OpenStudio.Query.OpenStudioName("Surface", null, guid), Is.EqualTo("SAM_Surface_b42bd811"));
            Assert.That(Core.OpenStudio.Query.OpenStudioName("Surface", "   ", guid), Is.EqualTo("SAM_Surface_b42bd811"));
        }

        [Test]
        public void OpenStudioName_FromSamObject_UsesTypeNameAndGuid()
        {
            Space space = new Space("Office 04");

            string name = Core.OpenStudio.Query.OpenStudioName(space);

            Assert.That(name, Does.StartWith("SAM_Space_Office_04_"));
            Assert.That(name, Does.EndWith(space.Guid.ToString("N").Substring(0, 8)));
            Assert.That(Core.OpenStudio.Query.OpenStudioName(space), Is.EqualTo(name), "Naming must be deterministic");
        }

        [Test]
        public void SanitizeName_RemovesQuotes()
        {
            // Quotes must never reach OpenStudio object names: they flow into EnergyPlus report
            // keys and would break the SQL extraction literals (review P2-02).
            string sanitized = Core.OpenStudio.Query.SanitizeName("O'Brien \"Annex\"");
            Assert.That(sanitized, Does.Not.Contain("'"));
            Assert.That(sanitized, Does.Not.Contain("\""));
            Assert.That(sanitized, Is.EqualTo("O_Brien_Annex"));
        }

        [Test]
        public void SpaceName_WithApostrophe_ConvertsWithDeterministicName()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            Space space = new Space(new System.Guid("12121212-0000-0000-0000-000000000001"), "O'Brien", new Geometry.Spatial.Point3D(0, 0, 0));
            string name = Core.OpenStudio.Query.OpenStudioName(space);
            Assert.That(name, Does.Not.Contain("'"), "No apostrophe may survive into an OpenStudio name");
            Assert.That(Core.OpenStudio.Query.OpenStudioName(space), Is.EqualTo(name), "Naming must stay deterministic");
        }
    }
}
