using UnityEngine;
using Game.Economy.Data;

namespace Game.Economy.Progression
{
    /// <summary>
    /// Pure static utility class encapsulating all mathematical formulas
    /// that govern the economic progression curves of Escape Horizon.
    ///
    /// This class has no state, no MonoBehaviour lifecycle, and no Unity
    /// dependencies beyond Mathf. It exists exclusively to isolate the
    /// progression math from the runtime systems that consume it, allowing
    /// independent testing, tuning, and reuse across SH_ResourceSystem,
    /// SH_EconomicEventManager, and the UI progression screens.
    ///
    /// All formulas are derived directly from GDD §5.5.2 and §5.5.3.
    ///
    /// Responsibility boundaries:
    ///   - OWNS: IC cost curve (exponential) for Development Point acquisition.
    ///   - OWNS: Scrap cost curve (linear) for build reconfiguration.
    ///   - OWNS: Eligibility checks and progress fraction queries.
    ///   - DOES NOT OWN: Resource state (belongs to SH_ResourceSystem).
    ///   - DOES NOT OWN: Event modifiers (applied by SH_EconomicEventManager
    ///                   before calling these formulas).
    /// </summary>
    public static class SH_ProgressionCalculator
    {
        #region Identity Core Cost Curve (Exponential)

        /// <summary>
        /// Calculates the Identity Core (IC) cost required to generate
        /// the next Development Point (DP) given the current total DP already spent.
        ///
        /// Formula (GDD §5.5.3):
        ///     Cost_IC(DP) = IC_0 * e^(k * DP)
        ///
        /// Where:
        ///     IC_0 = icBaseCost       (base cost for the first DP)
        ///     k    = icScalingCoeff   (exponential growth rate)
        ///     DP   = totalDPSpent     (total development points already invested)
        ///
        /// The result is always at least 1, ensuring the first DP is always
        /// attainable and the cost never collapses to zero due to floating point.
        /// </summary>
        /// <param name="totalDPSpent">
        /// Total Development Points already spent across all trees.
        /// Must be zero or greater.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset providing IC_0 and k constants.
        /// Must not be null.
        /// </param>
        /// <returns>
        /// The IC cost (as a float) required to unlock the next DP.
        /// Returns float.MaxValue if settings is null to prevent accidental
        /// eligibility approvals on invalid state.
        /// </returns>
        public static float GetICCostForNextDP(int totalDPSpent, SH_EconomySettings settings)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] GetICCostForNextDP: SH_EconomySettings reference is null. Returning float.MaxValue to prevent invalid eligibility.");
#endif
                return float.MaxValue;
            }
            if (totalDPSpent < 0) 
            { 
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ProgressionCalculator] GetICCostForNextDP: totalDPSpent ({totalDPSpent}) is negative. Clamping to 0."); 
#endif
                totalDPSpent = 0;
            }

            // IC_0 * e^(k * DP)
            float cost = settings.icBaseCost *
                         Mathf.Exp(settings.icScalingCoefficient * totalDPSpent);

            // Guarantee a minimum cost of 1 to preserve curve integrity at DP = 0.
            return Mathf.Max(1f, cost);
        }

        /// <summary>
        /// Returns true if the pilot currently holds enough Identity Cores
        /// to afford the next Development Point at the given progression level.
        /// This is a pure eligibility check with no side effects.
        /// </summary>
        /// <param name="currentIC">
        /// The pilot's current Identity Core count.
        /// </param>
        /// <param name="totalDPSpent">
        /// Total Development Points already spent.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset. Must not be null.
        /// </param>
        /// <returns>
        /// True if currentIC >= cost of next DP. False otherwise.
        /// </returns>
        public static bool IsEligibleForNextDP(
            int currentIC,
            int totalDPSpent,
            SH_EconomySettings settings)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] IsEligibleForNextDP: SH_EconomySettings reference is null. Returning false.");
#endif
                return false;
            }

                float cost = GetICCostForNextDP(totalDPSpent, settings);
            return currentIC >= cost;
        }

        /// <summary>
        /// Returns a normalized fraction (0.0 to 1.0) representing how close
        /// the pilot is to affording the next Development Point.
        /// Used by the UI progression bar to visualize IC accumulation progress
        /// without exposing raw values.
        ///
        /// A value of 0.0 means the pilot has no IC toward the next DP.
        /// A value of 1.0 means the pilot has exactly enough (or more) IC for the next DP.
        /// </summary>
        /// <param name="currentIC">
        /// The pilot's current Identity Core count.
        /// </param>
        /// <param name="totalDPSpent">
        /// Total Development Points already spent.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset. Must not be null.
        /// </param>
        /// <returns>
        /// Normalized progress toward the next DP, clamped between 0.0 and 1.0.
        /// </returns>
        public static float GetProgressToNextDP(
            int currentIC,
            int totalDPSpent,
            SH_EconomySettings settings)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] GetProgressToNextDP: SH_EconomySettings reference is null. Returning 0."); 
#endif
                return 0f;
            }

            float cost = GetICCostForNextDP(totalDPSpent, settings);

            if (cost <= 0f)
                return 1f;

            return Mathf.Clamp01((float)currentIC / cost);
        }

        /// <summary>
        /// Calculates how many Development Points the pilot would gain
        /// if a purge operation were executed right now, without modifying any state.
        /// Used by the UI to preview the outcome of a purge before confirming.
        ///
        /// Simulates the iterative purge loop from SH_ResourceSystem.PurgeCores()
        /// using a local copy of the IC and DP counters.
        /// </summary>
        /// <param name="currentIC">
        /// The pilot's current Identity Core count.
        /// </param>
        /// <param name="totalDPSpent">
        /// Total Development Points already spent.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset. Must not be null.
        /// </param>
        /// <returns>
        /// The number of DPs that would be gained from a purge at the current state.
        /// Returns 0 if insufficient IC or settings is null.
        /// </returns>
        public static int SimulatePurge(
            int currentIC,
            int totalDPSpent,
            SH_EconomySettings settings)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] SimulatePurge: SH_EconomySettings reference is null. Returning 0."); 
#endif
                return 0;
            }

            int simulatedIC = currentIC;
            int simulatedDPSpent = totalDPSpent;
            int dpGained = 0;

            while (true)
            {
                float cost = GetICCostForNextDP(simulatedDPSpent, settings);

                if (simulatedIC < cost)
                    break;

                simulatedIC -= Mathf.CeilToInt(cost);
                simulatedDPSpent++;
                dpGained++;
            }

            return dpGained;
        }

        #endregion

        #region Scrap Reconfiguration Cost Curve (Linear)

        /// <summary>
        /// Calculates the Scrap (SC) cost required to perform a build reconfiguration
        /// (resetting all spent Development Points) at the current progression level.
        ///
        /// Formula (GDD §5.5.2):
        ///     Cost_SC(DP) = SC_0 + m * DP
        ///
        /// Where:
        ///     SC_0 = scBaseReconfigCost         (flat base penalty)
        ///     m    = scReconfigCostMultiplier   (linear growth per DP spent)
        ///     DP   = totalDPSpent               (total development points invested)
        ///
        /// Note: Event modifiers (ReconfigurationOverload CS coefficient) are NOT
        /// applied here. The caller (SH_ResourceSystem.RequestReconfiguration) is
        /// responsible for multiplying this result by the active CS modifier.
        /// This keeps the formula pure and testable in isolation.
        /// </summary>
        /// <param name="totalDPSpent">
        /// Total Development Points already spent. Must be zero or greater.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset providing SC_0 and m constants.
        /// Must not be null.
        /// </param>
        /// <returns>
        /// The base Scrap cost (before event modifiers) for a reconfiguration.
        /// Returns float.MaxValue if settings is null.
        /// </returns>
        public static float GetReconfigCost(int totalDPSpent, SH_EconomySettings settings)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] GetReconfigCost: SH_EconomySettings reference is null. Returning float.MaxValue to prevent invalid authorization."); 
#endif
                return float.MaxValue;
            }
            if (totalDPSpent < 0) 
            { 
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ProgressionCalculator] GetReconfigCost: totalDPSpent ({totalDPSpent}) is negative. Clamping to 0."); 
#endif
                totalDPSpent = 0;
            }

            // SC_0 + m * DP
            float cost = settings.scBaseReconfigCost +
                         (settings.scReconfigCostMultiplier * totalDPSpent);

            return Mathf.Max(0f, cost);
        }

        /// <summary>
        /// Calculates the effective Scrap reconfiguration cost after applying
        /// the active economic event modifier (CS coefficient).
        /// Use this method when displaying the final cost to the player in the UI,
        /// as it reflects the true cost including any active overload penalties.
        /// </summary>
        /// <param name="totalDPSpent">
        /// Total Development Points already spent.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset. Must not be null.
        /// </param>
        /// <param name="csModifier">
        /// Active reconfiguration cost modifier from SH_EconomicEventManager.
        /// Default is 1.0 (no event active). Must be >= 1.0.
        /// </param>
        /// <returns>
        /// The final Scrap cost after applying the CS modifier.
        /// </returns>
        public static float GetReconfigCostWithModifier(
            int totalDPSpent,
            SH_EconomySettings settings,
            float csModifier = 1f)
        {
            if (csModifier < 1f) 
            { 
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ProgressionCalculator] GetReconfigCostWithModifier: csModifier ({csModifier}) is below 1.0. Clamping to 1.0. Reconfiguration cost modifiers cannot reduce base cost."); 
#endif
                csModifier = 1f;
            }

            float baseCost = GetReconfigCost(totalDPSpent, settings);
            return baseCost * csModifier;
        }

        #endregion

        #region Curve Inspection Utilities

        /// <summary>
        /// Generates an array of IC costs for a specified number of future DPs,
        /// starting from the current progression level.
        /// Used by the UI to render the full progression cost curve as a preview
        /// graph in the base operations screen.
        /// </summary>
        /// <param name="totalDPSpent">
        /// Current total DP spent (starting point of the curve preview).
        /// </param>
        /// <param name="stepsToPreview">
        /// Number of future DP costs to calculate. Clamped between 1 and 50.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset. Must not be null.
        /// </param>
        /// <returns>
        /// Array of float values representing IC costs for each upcoming DP.
        /// Returns an empty array if settings is null.
        /// </returns>
        public static float[] GetICCostCurvePreview(
            int totalDPSpent,
            int stepsToPreview,
            SH_EconomySettings settings)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] GetICCostCurvePreview: SH_EconomySettings reference is null. Returning empty array.");
#endif
                return new float[0]; 
            }

            stepsToPreview = Mathf.Clamp(stepsToPreview, 1, 50);
            float[] curve = new float[stepsToPreview];

            for (int i = 0; i < stepsToPreview; i++)
            {
                curve[i] = GetICCostForNextDP(totalDPSpent + i, settings);
            }

            return curve;
        }

        /// <summary>
        /// Generates an array of Scrap reconfiguration costs for a specified number
        /// of future DP levels, starting from the current progression level.
        /// Used by the UI to communicate the increasing penalty of late-game reconfigurations.
        /// </summary>
        /// <param name="totalDPSpent">
        /// Current total DP spent (starting point of the preview).
        /// </param>
        /// <param name="stepsToPreview">
        /// Number of future DP levels to preview. Clamped between 1 and 50.
        /// </param>
        /// <param name="settings">
        /// Economy settings asset. Must not be null.
        /// </param>
        /// <param name="csModifier">
        /// Active reconfiguration cost modifier. Default is 1.0.
        /// </param>
        /// <returns>
        /// Array of float values representing Scrap reconfig costs per DP level.
        /// Returns an empty array if settings is null.
        /// </returns>
        public static float[] GetReconfigCostCurvePreview(
            int totalDPSpent,
            int stepsToPreview,
            SH_EconomySettings settings,
            float csModifier = 1f)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError("[SH_ProgressionCalculator] GetReconfigCostCurvePreview: SH_EconomySettings reference is null. Returning empty array.");
#endif
                return new float[0]; 
            }

            stepsToPreview = Mathf.Clamp(stepsToPreview, 1, 50);
            float[] curve = new float[stepsToPreview];

            for (int i = 0; i < stepsToPreview; i++)
            {
                curve[i] = GetReconfigCostWithModifier(
                    totalDPSpent + i, settings, csModifier);
            }

            return curve;
        }

        #endregion
    }
}