// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>
    /// The S09 owner acceptances recorded in SAM_OpenStudio#33 (2026-10-22). The owner accepted OS-MAT-SINGLE-SIDED
    /// (optics option 1) for the two reviewed fixtures only, with its effect on results unmeasured. An acceptance applies
    /// only under the approved rules, to a fixture whose id and frozen-model bytes (SHA-256) are both the reviewed ones,
    /// and only to the 3a MAPPING-DIFFERENCE rows S09 lists for the single-sided writer behaviour. It moves those rows
    /// from rank 3 to rank 5 (NOT PASS, accepted limitation, exit 14); it never makes them pass and never hides a
    /// higher-ranked FAIL or BLOCKED result. Any other model, fixture id or rule set gets no acceptance.
    /// </summary>
    public static class OwnerDecisions
    {
        public const string SingleSidedOpaqueReference = "SAM_OpenStudio#33 owner decision 2026-10-22, OS-MAT-SINGLE-SIDED option 1: accepted for the reviewed fixture bytes only; effect on results unmeasured";

        /// <summary>The reviewed fixtures: the S08 signed-off files, by id and SHA-256 (Fixtures/SHA256SUMS.txt).</summary>
        public static readonly IReadOnlyDictionary<string, string> ReviewedFixtures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["s08-fix-box"] = "45524172b32f85df89511dd344141023de999faa9552a199e6b49062612acd08",
            ["s08-fix-two"] = "20da9b586345e9fc67dc6308e6da1578b9bc41b3c694dbdab37e9c677959baa0",
        };

        public static IReadOnlyCollection<OwnerAcceptance> For(string fixtureId, string modelSha256, GateRuleSet ruleSet)
        {
            if (ruleSet != GateRuleSet.Approved || fixtureId == null || modelSha256 == null
                || !ReviewedFixtures.TryGetValue(fixtureId, out string sha256) || !string.Equals(sha256, modelSha256, StringComparison.Ordinal))
            {
                return Array.Empty<OwnerAcceptance>();
            }

            return new[] { new OwnerAcceptance("3a", fixtureId, AcceptableLimitation.MappingDifference, SingleSidedOpaqueReference) };
        }
    }
}
