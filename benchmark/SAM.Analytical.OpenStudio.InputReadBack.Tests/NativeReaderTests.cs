// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>The text reader and the schedule expansion: unknown layouts and forms are unreadable, never guessed.</summary>
    [TestFixture]
    public class NativeReaderTests
    {
        private const string Idf =
            "Zone,\n  Z1,                                      !- Name\n  ,                                       !- Direction of Relative North {deg}\n  1, 2, 3;                                !- Z Origin {m}\n\n" +
            "BuildingSurface:Detailed,\n  S1,  !- Name\n  Wall, !- Surface Type\n  0, 0, 0,  !- X,Y,Z Vertex 1 {m}\n  1, 0, 0;  !- X,Y,Z Vertex 2 {m}\n";

        [Test]
        public void Parse_KeepsFieldsAndTheFileLabels()
        {
            IdfObject zone = IdfText.Parse(Idf).First();
            Assert.That(zone.Type, Is.EqualTo("Zone"));
            Assert.That(zone.Name, Is.EqualTo("Z1"));
            Assert.That(zone.Fields.Select(x => x.Value), Is.EqualTo(new[] { "Z1", string.Empty, "1", "2", "3" }));
            Assert.That(zone.Fields[4].Label, Is.EqualTo("Z Origin {m}"), "a label describes the last field on its line");
            Assert.That(zone.Value(1, "Direction of Relative North", out string problem), Is.EqualTo(string.Empty));
            Assert.That(problem, Is.Null);
        }

        [Test]
        public void AFieldWhoseLabelDisagrees_IsUnreadable()
        {
            IdfObject zone = IdfText.Parse(Idf).First();
            Assert.That(zone.Value(1, "Multiplier", out string problem), Is.Null);
            Assert.That(problem, Does.Contain("labelled 'Direction of Relative North"));
            Assert.That(NativeModel.Number(zone, 1, "Multiplier").Kind, Is.EqualTo(SAM.Analytical.InputEquivalence.ReaderKind.Unreadable));
            Assert.That(NativeModel.Number(zone, 1, "Direction of Relative North").Kind, Is.EqualTo(SAM.Analytical.InputEquivalence.ReaderKind.Absent), "blank is absent (EnergyPlus default), not zero");
        }

        private static NativeSchedule Compact(string body)
        {
            return ScheduleReader.Read(IdfText.Parse("Schedule:Compact,\n  S, !- Name\n  F, !- Schedule Type Limits Name\n" + body).Single(), 365, "idf");
        }

        [Test]
        public void Compact_UntilLabelsEndTheirHour_AndCoverTheYear()
        {
            NativeSchedule schedule = Compact("  Through: 06/30, For: AllDays, Until: 07:00, 0.1, Until: 24:00, 0.9,\n  Through: 12/31, For: AllDays, Until: 24:00, 0.5;\n");
            Assert.That(schedule.Problem, Is.Null);
            Assert.That(schedule.Values.Length, Is.EqualTo(8760));
            Assert.That(schedule.Values[6], Is.EqualTo(0.1), "hour index 6 = 06:00-07:00, ended by 'Until: 07:00'");
            Assert.That(schedule.Values[7], Is.EqualTo(0.9));
            Assert.That(schedule.Values[181 * 24], Is.EqualTo(0.5), "1 July (day index 181)");
            Assert.That(schedule.Values[181 * 24 - 1], Is.EqualTo(0.9), "30 June 23:00");
            Assert.That(schedule.AlignmentVerified, Is.True);
        }

        [TestCase("  Through: 12/31, For: Weekdays, Until: 24:00, 1;\n", "For: AllDays")]
        [TestCase("  Through: 12/31, For: AllDays, Until: 07:30, 1, Until: 24:00, 0;\n", "whole hour")]
        [TestCase("  Through: 12/30, For: AllDays, Until: 24:00, 1;\n", "covers 364 of 365")]
        [TestCase("  Through: 12/31, For: AllDays, Until: 12:00, 1;\n", "not 24:00")]
        [TestCase("  Through: 12/31, For: AllDays, Until: 24:00, x;\n", "not a finite number")]
        public void Compact_AnyOtherForm_IsUnreadable(string body, string reason)
        {
            NativeSchedule schedule = Compact(body);
            Assert.That(schedule.Values, Is.Null);
            Assert.That(schedule.AlignmentVerified, Is.False);
            Assert.That(schedule.Problem, Does.Contain(reason));
        }
    }
}
