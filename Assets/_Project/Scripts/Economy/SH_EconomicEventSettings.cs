using UnityEngine;

namespace Game.Economy.Data
{
    /// <summary>
    /// Enumerates the three categories of dynamic economic events that can
    /// modify resource acquisition rates and progression costs at runtime.
    /// Used as a type-safe key for event activation, tracking, and UI feedback.
    /// </summary>
    public enum EconomicEventType
    {
        /// <summary>
        /// Reduces the Identity Core (IC) drop rate for a defined duration.
        /// Triggered by narrative progression milestones (entering a new critical region).
        /// Represents the difficulty of refining knowledge in a hostile environment.
        /// </summary>
        IdentityCoreScarcity,

        /// <summary>
        /// Increases the Scrap cost of build reconfiguration for a defined duration.
        /// Triggered when the pilot reconfigures the build too frequently,
        /// penalizing indecision and rewarding commitment to a specialization.
        /// </summary>
        ReconfigurationOverload,

        /// <summary>
        /// Temporarily modifies the Energy regeneration rate (positive or negative).
        /// Triggered randomly on elite enemy encounters (15% base probability).
        /// Simulates instability in the Mecha's core reactor during high-intensity combat.
        /// </summary>
        EnergyFlux
    }

    /// <summary>
    /// Configuration asset for the dynamic economic event system.
    /// Defines all tunable parameters for event activation conditions,
    /// effect magnitudes, and cycle durations.
    /// Separating these values from SH_EconomySettings allows independent
    /// tuning of event behavior without affecting base economic constants.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EconomicEventSettings",
        menuName = "ScapeHorizon/Settings/EconomicEventSettings",
        order = 301)]
    public class SH_EconomicEventSettings : ScriptableObject
    {
        #region Identity Core Scarcity Event

        [Header("Event I — Identity Core Scarcity")]

        [Tooltip("Drop rate multiplier applied to IC acquisition while this event is active. " +
                 "Corresponds to CE in: DropRate_event = DropRate_base * CE. " +
                 "Values below 1.0 reduce IC drops (e.g. 0.75 = 25% reduction).")]
        [Range(0.1f, 1f)]
        public float scarcityCoefficient = 0.75f;

        [Tooltip("Duration of the scarcity event in seconds of active gameplay. " +
                 "Corresponds to DC (Cycle Duration). " +
                 "Resets when the pilot exits the critical region that triggered it.")]
        [Min(10f)]
        public float scarcityDuration = 300f;

        #endregion

        #region Reconfiguration Overload Event

        [Header("Event II — Reconfiguration Overload")]

        [Tooltip("Scrap cost multiplier applied to build reconfiguration while active. " +
                 "Corresponds to CS in: Cost_CH_event = Cost_CH * CS. " +
                 "Values above 1.0 increase the reconfiguration cost (e.g. 2.0 = double cost).")]
        [Min(1f)]
        public float overloadCoefficient = 2f;

        [Tooltip("Number of reconfigurations within the time window that triggers this event. " +
                 "If the pilot reconfigures this many times within reconfigWindowSeconds, " +
                 "the overload event activates automatically.")]
        [Min(2)]
        public int reconfigTriggerThreshold = 3;

        [Tooltip("Time window in seconds of active gameplay within which reconfigurations " +
                 "are counted toward the overload threshold. " +
                 "Reconfigurations outside this window do not accumulate.")]
        [Min(60f)]
        public float reconfigWindowSeconds = 7200f;

        [Tooltip("Duration of the overload event in seconds of active gameplay " +
                 "after it has been triggered by exceeding the reconfig threshold.")]
        [Min(10f)]
        public float overloadDuration = 600f;

        #endregion

        #region Energy Flux Event

        [Header("Event III — Energy Flux")]

        [Tooltip("Probability (0 to 1) of triggering an Energy Flux event " +
                 "when combat begins against an elite enemy. " +
                 "Corresponds to the 15% base probability defined in the GDD.")]
        [Range(0f, 1f)]
        public float energyFluxChance = 0.15f;

        [Tooltip("Energy regeneration rate multiplier when a positive flux is active. " +
                 "Values above 1.0 increase regen (e.g. 1.2 = 20% faster regeneration).")]
        [Min(1f)]
        public float energyFluxPositiveMultiplier = 1.2f;

        [Tooltip("Energy regeneration rate multiplier when a negative flux is active. " +
                 "Values below 1.0 reduce regen (e.g. 0.7 = 30% slower regeneration).")]
        [Range(0.1f, 1f)]
        public float energyFluxNegativeMultiplier = 0.7f;

        [Tooltip("Duration of the Energy Flux event in seconds. " +
                 "Applies for the duration of the elite encounter that triggered it.")]
        [Min(5f)]
        public float energyFluxDuration = 60f;

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            scarcityCoefficient = Mathf.Clamp(scarcityCoefficient, 0.1f, 1f);
            scarcityDuration = Mathf.Max(10f, scarcityDuration);

            overloadCoefficient = Mathf.Max(1f, overloadCoefficient);
            reconfigTriggerThreshold = Mathf.Max(2, reconfigTriggerThreshold);
            reconfigWindowSeconds = Mathf.Max(60f, reconfigWindowSeconds);
            overloadDuration = Mathf.Max(10f, overloadDuration);

            energyFluxChance = Mathf.Clamp01(energyFluxChance);
            energyFluxPositiveMultiplier = Mathf.Max(1f, energyFluxPositiveMultiplier);
            energyFluxNegativeMultiplier = Mathf.Clamp(energyFluxNegativeMultiplier, 0.1f, 1f);
            energyFluxDuration = Mathf.Max(5f, energyFluxDuration);
        }

        #endregion
    }
}