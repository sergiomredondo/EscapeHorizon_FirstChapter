using UnityEngine;

namespace Game.Economy.Data
{
    /// <summary>
    /// Enumerates the three resource types that constitute the game's
    /// internal economy. Used as a universal key across all economic systems
    /// to avoid magic strings and ensure type safety.
    /// </summary>
    public enum ResourceType
    {
        /// <summary>
        /// Identity Core (IC). Permanent progression resource.
        /// Finite, high-value, obtained exclusively through ethical rescue actions.
        /// </summary>
        IdentityCore,

        /// <summary>
        /// Scrap (SC). Tactical flexibility resource.
        /// Abundant, obtained from combat. Used for build reconfiguration costs.
        /// </summary>
        Scrap,

        /// <summary>
        /// Energy core (EC). Combat sustainability resource.
        /// Regenerable, capped, consumed by Mecha abilities and actions.
        /// </summary>
        EnergyCore
    }

    /// <summary>
    /// Central configuration asset for the game's economic system.
    /// Stores all tunable parameters for resources, progression curves,
    /// defeat penalties, and health (Durabilidad) thresholds.
    /// Acts as the single source of truth for economic balance.
    /// Modify values here to tune the economy without touching code.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EconomySettings",
        menuName = "ScapeHorizon/Settings/EconomySettings",
        order = 300)]
    public class SH_EconomySettings : ScriptableObject
    {
        #region EnergyCore (EC) Parameters

        [Header("EnergyCore (EC) — Combat Sustainability")]

        [Tooltip("Maximum amount of Energia the Mecha can store. " +
                 "Acts as the hard cap for the EC reservoir.")]
        [Min(1f)]
        public float maxEnergy = 100f;

        [Tooltip("Passive regeneration rate of Energia per second during combat. " +
                 "Modulated at runtime by active economic events.")]
        [Min(0f)]
        public float energyRegenPerSecond = 5f;

        [Tooltip("Minimum Energia guaranteed after a defeat event. " +
                 "Expressed as a fraction of maxEnergy (e.g. 0.1 = 10%).")]
        [Range(0f, 1f)]
        public float energyDefeatFloor = 0.1f;

        #endregion

        #region Identity Core (IC) Parameters

        [Header("Identity Core (IC) — Permanent Progression")]

        [Tooltip("Base IC cost to generate the first Development Point (DP). " +
                 "Corresponds to IC_0 in the exponential cost formula.")]
        [Min(1f)]
        public float icBaseCost = 10f;

        [Tooltip("Exponential scaling coefficient for IC cost per PD already spent. " +
                 "Corresponds to k in: Cost(DP) = NI_0 * e^(k * PD). " +
                 "Higher values make progression steeper.")]
        [Min(0.01f)]
        public float icScalingCoefficient = 0.15f;

        #endregion

        #region Defeat & Penalty

        [Tooltip("Fraction of max durability at which the tactical retreat triggers. " +
         "The pilot warns and activates the escape before reaching zero. " +
         "Range: 0.05 – 0.30. Default: 0.15 (15%).")]
        [Range(0.05f, 0.30f)]
        public float retreatHealthThreshold = 0.15f;

        [Tooltip("Minimum energy fraction the Mecha retains after a defeat penalty. " +
                 "Energy never drops below 10% of maximum after a retreat.")]
        [Range(0.05f, 0.20f)]
        public float energyFloorFraction = 0.10f;

        [Tooltip("Fraction of unspent Scrap retained after a defeat event. " +
                 "Expressed as a fraction (e.g. 0.5 = 50% retained, 50% lost).")]
        [Range(0f, 1f)]
        public float scrapDefeatRetentionRate = 0.5f;

        [Tooltip("Fraction of unspent Energy retained after a defeat event. " +
                 "Expressed as a fraction (e.g. 0.5 = 50% retained, 50% lost).")]
        [Range(0f, 1f)]
        public float energyDefeatRetentionRate = 0.5f;

        [Tooltip("Fraction of unspent IC cores retained after a defeat event. " +
                 "Expressed as a fraction (e.g. 0.5 = 50% retained, 50% lost).")]
        [Range(0f, 1f)]
        public float icDefeatRetentionRate = 0.5f;

        #endregion

        #region Scrap (SC) Parameters

        [Header("Scrap (SC) — Tactical Flexibility")]

        [Tooltip("Base Scrap cost for the first build reconfiguration. " +
                 "Corresponds to SC_0 in the linear cost formula.")]
        [Min(0f)]
        public float scBaseReconfigCost = 50f;

        [Tooltip("Linear multiplier applied per total DP spent when calculating " +
                 "reconfiguration cost. Corresponds to m in: Cost(DP) = SC_0 + m * DP.")]
        [Min(0f)]
        public float scReconfigCostMultiplier = 20f;

        #endregion

        #region Durability Parameters

        [Header("Durability — Mecha Structural Integrity")]

        [Tooltip("Maximum Durability (HP) of the Mecha. " +
                 "Defines the upper bound for the health component.")]
        [Min(1f)]
        public float maxDurability = 200f;

        [Tooltip("Durability threshold below which the defeat sequence is triggered. " +
                 "Expressed as a fraction of maxDurabilidad (e.g. 0.05 = 5%).")]
        [Range(0f, 0.5f)]
        public float defeatThreshold = 0.05f;

        [Tooltip("Amount of Durability restored per unit of Scrap spent during " +
                 "interlude repair actions. Defines the CH-to-HP exchange rate.")]
        [Min(0.1f)]
        public float durabilityPerScrap = 2f;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            maxEnergy = Mathf.Max(1f, maxEnergy);
            energyRegenPerSecond = Mathf.Max(0f, energyRegenPerSecond);
            energyDefeatFloor = Mathf.Clamp01(energyDefeatFloor);

            icBaseCost = Mathf.Max(1f, icBaseCost);
            icScalingCoefficient = Mathf.Max(0.01f, icScalingCoefficient);
            icDefeatRetentionRate = Mathf.Clamp01(icDefeatRetentionRate);

            scBaseReconfigCost = Mathf.Max(0f, scBaseReconfigCost);
            scReconfigCostMultiplier = Mathf.Max(0f, scReconfigCostMultiplier);

            maxDurability = Mathf.Max(1f, maxDurability);
            defeatThreshold = Mathf.Clamp(defeatThreshold, 0f, 0.5f);
            durabilityPerScrap = Mathf.Max(0.1f, durabilityPerScrap);

            retreatHealthThreshold = Mathf.Clamp(retreatHealthThreshold, 0.05f, 0.30f);
            energyFloorFraction = Mathf.Clamp(energyFloorFraction, 0.05f, 0.20f);
        }

        #endregion
    }
}