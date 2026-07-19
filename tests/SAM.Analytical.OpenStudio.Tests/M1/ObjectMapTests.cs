// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M1: duplicate-prevention and traceability contract tests for OpenStudioObjectMap.</summary>
    [TestFixture]
    public class ObjectMapTests
    {
        [Test]
        public void TryAdd_PreventsDuplicatesAndNeverOverwrites()
        {
            Core.OpenStudio.OpenStudioObjectMap map = new Core.OpenStudio.OpenStudioObjectMap();
            Guid guid = Guid.NewGuid();

            bool first = map.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(guid, "Space", "SAM_Space_A_00000001"));
            bool second = map.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(guid, "Space", "SAM_Space_B_00000002"));

            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "Duplicate SAM Guid must be rejected");
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map.OpenStudioObjectName(guid), Is.EqualTo("SAM_Space_A_00000001"), "Existing mapping must never be overwritten");
        }

        [Test]
        public void TryAdd_RejectsNullAndEmptyNames()
        {
            Core.OpenStudio.OpenStudioObjectMap map = new Core.OpenStudio.OpenStudioObjectMap();

            Assert.That(map.TryAdd(null), Is.False);
            Assert.That(map.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(Guid.NewGuid(), "Space", " ")), Is.False);
            Assert.That(map.Count, Is.EqualTo(0));
        }

        [Test]
        public void ToNameDictionary_IsASnapshot()
        {
            Core.OpenStudio.OpenStudioObjectMap map = new Core.OpenStudio.OpenStudioObjectMap();
            Guid guid = Guid.NewGuid();
            map.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(guid, "Panel", "SAM_Panel_X_00000003"));

            IReadOnlyDictionary<Guid, string> snapshot = map.ToNameDictionary();
            map.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(Guid.NewGuid(), "Panel", "SAM_Panel_Y_00000004"));

            Assert.That(snapshot.Count, Is.EqualTo(1), "Snapshot must not grow with the map");
            Assert.That(snapshot[guid], Is.EqualTo("SAM_Panel_X_00000003"));
            Assert.That(map.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryGetReference_ReturnsRegisteredReference()
        {
            Core.OpenStudio.OpenStudioObjectMap map = new Core.OpenStudio.OpenStudioObjectMap();
            Guid guid = Guid.NewGuid();
            map.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(guid, "Aperture", "SAM_Aperture_W1_00000005"));

            Assert.That(map.Contains(guid), Is.True);
            Assert.That(map.TryGetReference(guid, out Core.OpenStudio.OpenStudioObjectReference reference), Is.True);
            Assert.That(reference.SamObjectType, Is.EqualTo("Aperture"));
            Assert.That(map.TryGetReference(Guid.NewGuid(), out _), Is.False);
        }
    }
}
