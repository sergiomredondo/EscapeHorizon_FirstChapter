using Game.Economy.Data;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.ProBuilder.Shapes;
using UnityEngine.UIElements;
using static UnityEngine.EventSystems.EventTrigger;

namespace Game.Economy.Data
{
    /// <summary>
    /// Declarative data asset defining the economic rewards delivered by a single
    /// enemy archetype upon interaction.
    ///
    /// Two interaction outcomes are modeled, reflecting the central ethical mechanic
    /// of Escape Horizon (GDD §4.3):
    ///
    ///   DESTROY path  — The Captive Automaton is destroyed.
    ///                   Yields Scrap (SC) immediately.
    ///                   Yields no Identity Cores.
    ///                   Contributes to temporary build power but blocks
    ///                   permanent progression.
    ///
    ///   RESCUE path   — The Captive Automaton's Identity Core is extracted intact.
    ///                   Yields Identity Cores (IC) for permanent progression.
    ///                   Yields reduced or zero Scrap.
    ///                   Contributes to long-term build flexibility via PurgeCores().
    ///
    /// Energy drops (EC) are outcome-independent and represent residual power cells
    /// released on any interaction.
    ///
    /// This asset is the authoritative source for drop quantities.
    /// The IC drop rate modifier from SH_EconomicEventManager (Scarcity event) is
    /// applied by SH_ResourceSystem.AddResource() at the point of delivery,
    /// not here. This keeps the data layer pure and event-agnostic.
    ///
    /// Usage:
    ///   Create one asset per enemy archetype via the Asset menu.
    ///   Assign to the enemy's component that calls SH_ResourceSystem.AddResource().
    ///   Reference the appropriate DropPath enum value based on the interaction outcome.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ResourceDropData",
        menuName = "ScapeHorizon/Economy/Resource Drop Data",
        order = 300)]
    public class SH_ResourceDropData : ScriptableObject
    {
        #region Destroy Path — Scrap Rewards

        [Header("Destroy Path — Scrap Rewards")]

        [Tooltip("Base Scrap (SC) awarded when this enemy archetype is destroyed.\n" +
                 "Scrap feeds immediate build power but does not contribute to\n" +
                 "permanent progression (Identity Core purge).")]
        [Min(0f)]
        public float scrapOnDestroy = 25f;

        [Tooltip("Variance range applied to scrapOnDestroy at delivery time.\n" +
                 "Final drop = scrapOnDestroy ± scrapVariance.\n" +
                 "Set to 0 for deterministic drops.")]
        [Min(0f)]
        public float scrapVariance = 5f;

        #endregion

        #region Rescue Path — Identity Core Rewards

        [Header("Rescue Path — Identity Core Rewards")]

        [Tooltip("Identity Cores (IC) awarded when this enemy's core is extracted intact.\n" +
                 "IC accumulates toward Development Points via SH_ResourceSystem.PurgeCores().\n" +
                 "The active Scarcity event modifier is applied at delivery, not here.")]
        [Min(0)]
        public int identityCoresOnRescue = 1;

        [Tooltip("Residual Scrap (SC) awarded on the rescue path.\n" +
                 "Typically lower than scrapOnDestroy to reinforce the ethical tradeoff.\n" +
                 "Set to 0 to make the rescue path a pure IC reward with no Scrap consolation.")]
        [Min(0f)]
        public float scrapOnRescue = 5f;

        #endregion

        #region Energy Drop — Outcome Independent

        [Header("Energy Drop — Outcome Independent")]

        [Tooltip("Energy (EC) awarded regardless of whether the enemy was destroyed or rescued.\n" +
                 "Represents residual power cells released on any interaction outcome.\n" +
                 "Set to 0 to disable energy drops for this archetype.")]
        [Min(0f)]
        public float energyOnInteraction = 10f;

        [Tooltip("Variance range applied to energyOnInteraction at delivery time.\n" +
                 "Final drop = energyOnInteraction ± energyVariance.\n" +
                 "Set to 0 for deterministic energy drops.")]
        [Min(0f)]
        public float energyVariance = 2f;

        #endregion

        #region Derived Drop Calculation

        /// <summary>
        /// Calculates the final Scrap drop for the Destroy path, applying variance.
        /// Variance is sampled uniformly within ± scrapVariance of the base value.
        /// The result is clamped to zero to prevent negative drops on low-base archetypes.
        /// </summary>
        /// <returns>
        /// Final Scrap quantity to deliver via SH_ResourceSystem.AddResource().
        /// </returns>
        public float RollScrapOnDestroy()
        {
            float variance = scrapVariance > 0f
                ? Random.Range(-scrapVariance, scrapVariance)
                : 0f;

            return Mathf.Max(0f, scrapOnDestroy + variance);
        }

        /// <summary>
        /// Calculates the final Scrap drop for the Rescue path, applying no variance.
        /// Rescue Scrap is deterministic to preserve the clarity of the ethical tradeoff:
        /// the pilot must always know exactly what they are giving up by choosing rescue.
        /// </summary>
        /// <returns>
        /// Final Scrap quantity to deliver via SH_ResourceSystem.AddResource().
        /// </returns>
        public float RollScrapOnRescue()
        {
            return scrapOnRescue;
        }

        /// <summary>
        /// Calculates the final Energy drop, applying variance.
        /// Energy variance is outcome-independent and consistent across both paths.
        /// </summary>
        /// <returns>
        /// Final Energy quantity to deliver via SH_ResourceSystem.AddResource().
        /// </returns>
        public float RollEnergyDrop()
        {
            float variance = energyVariance > 0f
                ? Random.Range(-energyVariance, energyVariance)
                : 0f;

            return Mathf.Max(0f, energyOnInteraction + variance);
        }

        /// <summary>
        /// Convenience method that delivers all applicable resources for the Destroy path
        /// directly to the provided resource system in a single call.
        /// Intended for use by enemy defeat handlers once combat resolution is implemented.
        /// Energy is always delivered. Scrap is rolled with variance.
        /// Identity Cores are not delivered on the Destroy path.
        /// </summary>
        /// <param name="resourceSystem">
        /// The active resource system. Must not be null.
        /// </param>
        public void DeliverDestroyRewards(SH_ResourceSystem resourceSystem)
        {
            if (resourceSystem == null)
            {
                Debug.LogError("[SH_ResourceDropData] DeliverDestroyRewards: " +
                               "SH_ResourceSystem reference is null. No rewards delivered.");
                return;
            }

            float scrap = RollScrapOnDestroy();
            float energy = RollEnergyDrop();

            resourceSystem.AddResource(ResourceType.Scrap, scrap);
            resourceSystem.AddResource(ResourceType.EnergyCore, energy);

            Debug.Log($"[SH_ResourceDropData] Destroy rewards delivered from '{name}': " +
                      $"{scrap:F1} SC, {energy:F1} EC.");
        }

        /// <summary>
        /// Convenience method that delivers all applicable resources for the Rescue path
        /// directly to the provided resource system in a single call.
        /// Intended for use by enemy rescue handlers once interaction resolution is implemented.
        /// Energy and residual Scrap are always delivered.
        /// Identity Cores are delivered as integers via the IC-specific resource type.
        /// The active Scarcity modifier is applied inside AddResource(), not here.
        /// </summary>
        /// <param name="resourceSystem">
        /// The active resource system. Must not be null.
        /// </param>
        public void DeliverRescueRewards(SH_ResourceSystem resourceSystem)
        {
            if (resourceSystem == null)
            {
                Debug.LogError("[SH_ResourceDropData] DeliverRescueRewards: " +
                               "SH_ResourceSystem reference is null. No rewards delivered.");
                return;
            }

            float scrap = RollScrapOnRescue();
            float energy = RollEnergyDrop();

            resourceSystem.AddResource(ResourceType.IdentityCore, identityCoresOnRescue);
            resourceSystem.AddResource(ResourceType.Scrap, scrap);
            resourceSystem.AddResource(ResourceType.EnergyCore, energy);

            Debug.Log($"[SH_ResourceDropData] Rescue rewards delivered from '{name}': " +
                      $"{identityCoresOnRescue} IC, {scrap:F1} SC, {energy:F1} EC.");
        }

        #endregion

        #region Editor Validation

        private void OnValidate()
        {
            scrapOnDestroy = Mathf.Max(0f, scrapOnDestroy);
            scrapVariance = Mathf.Max(0f, scrapVariance);
            scrapOnRescue = Mathf.Max(0f, scrapOnRescue);
            energyOnInteraction = Mathf.Max(0f, energyOnInteraction);
            energyVariance = Mathf.Max(0f, energyVariance);
            identityCoresOnRescue = Mathf.Max(0, identityCoresOnRescue);

            // Design guard: rescue Scrap should never exceed destroy Scrap.
            // If it does, log a warning to alert the designer without blocking saving.
            if (scrapOnRescue > scrapOnDestroy && scrapOnDestroy > 0f)
            {
                Debug.LogWarning($"[SH_ResourceDropData] '{name}': scrapOnRescue " +
                                 $"({scrapOnRescue}) exceeds scrapOnDestroy ({scrapOnDestroy}). " +
                                 $"This undermines the ethical tradeoff. Consider reducing " +
                                 $"scrapOnRescue or increasing scrapOnDestroy.");
            }
        }

        #endregion
    }
}