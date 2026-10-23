// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>Process exit codes, the same as the S08 Tas harness so both engines' runs read alike.</summary>
    public static class ReadBackExit
    {
        public const int Pass = 0;
        public const int Fail = 10;
        public const int BlockedTechnical = 11;
        public const int BlockedOwner = 12;
        public const int NotRun = 13;
        public const int AcceptedLimitation = 14;
        public const int Usage = 64;
        public const int Infrastructure = 70;

        public static int Of(GateStatus status)
        {
            switch (status)
            {
                case GateStatus.Pass:
                    return Pass;
                case GateStatus.Fail:
                    return Fail;
                case GateStatus.BlockedTechnical:
                    return BlockedTechnical;
                case GateStatus.BlockedOwnerDecision:
                    return BlockedOwner;
                case GateStatus.NotPassAcceptedLimitation:
                    return AcceptedLimitation;
                default:
                    return NotRun;
            }
        }
    }
}
