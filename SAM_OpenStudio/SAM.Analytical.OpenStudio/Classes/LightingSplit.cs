// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// The lighting energy split of SAM S07 §5.1 (amendment L1, T-LIGHT-SPLIT). SAM
    /// <c>LightingRadiantProportion</c> p is the total radiant share (long-wave plus short-wave) of the lighting gain
    /// entering the room; SAM and Tas have no luminaire-return allocation. For EnergyPlus <c>Lights</c>:
    /// Return Air Fraction 0, Fraction Visible p·s, Fraction Radiant p·(1 − s), convective 1 − p.
    /// <para>
    /// s is a declared contract assumption, not SAM data: the short-wave share of the emitter's radiant output for the
    /// reference category, suspended (pendant) LED office lighting with the whole fitting in the conditioned room. It is
    /// a midpoint-derived reference from cited historical ranges (IEA SHC Task 61 / EBC Annex 77 Technical Report D1,
    /// Table 6; ASHRAE Handbook—Fundamentals 2017, Ch. 18, Table 3), kept at full precision.
    /// </para>
    /// The view coefficient (<c>LightingViewCoefficient</c>) is a comfort coefficient, never an energy fraction, and is
    /// not part of this split.
    /// </summary>
    public static class LightingSplit
    {
        /// <summary>The reference total radiant share p₀ (S07 §5.1 baseline), applied only when SAM states no p.</summary>
        public const double ReferenceRadiantProportion = 0.576;

        /// <summary>The short-wave share s of the radiant output, 0.2415 / 0.576, never rounded.</summary>
        public const double ShortWaveShare = 0.2415 / 0.576;

        /// <summary>EnergyPlus Fraction Visible for a total radiant share p: p·s.</summary>
        public static double FractionVisible(double radiantProportion) => radiantProportion * ShortWaveShare;

        /// <summary>EnergyPlus Fraction Radiant (long-wave) for a total radiant share p: p·(1 − s).</summary>
        public static double FractionRadiant(double radiantProportion) => radiantProportion * (1 - ShortWaveShare);
    }
}
