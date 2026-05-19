using UnityEngine;
using System;
using Game.Economy.Data;
using Game.Economy.Progression;

namespace Game.Economy
{
    /// <summary>
    /// Central authority for the Mecha's economic state.
    /// Manages the three core resources: Identity Core (IC), Scrap (SC),
    /// and Energy Core (EC), along with defeat penalties and core purging.
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Current values of IC, SC, and EC.
    ///   - OWNS: Energy passive regeneration tick.
    ///   - OWNS: Defeat penalty logic (IC loss, EC floor enforcement).
    ///   - OWNS: Core purge logic (IC to DP conversion).
    ///   - OWNS: Reconfiguration cost validation and Scrap deduction.
    ///   - DOES NOT OWN: Durability (belongs to SH_HealthComponent).
    ///   - DOES NOT OWN: Drop amounts per enemy (belongs to enemy data assets).
    ///   - DOES NOT OWN: Economic event modifiers (applied by SH_EconomicEventManager).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_ResourceSystem : MonoBehaviour
    {
        #region Dependencies

        /// <summary>
        /// Reference to the central economy settings asset.
        /// Injected via Initialize(). Provides all economic constants and curves.
        /// </summary>
        private SH_EconomySettings _settings;

        /// <summary> Current Identity Core (IC) count. Integer: cores are discrete units. </summary>
        private int _currentIdentityCores;

        /// <summary> Current Scrap (SC) amount. Float: scrap accumulates in fractional amounts. </summary>
        private float _currentScrap;

        /// <summary> Current Energy Core (EC) level. Float: energy regenerates continuously. </summary>
        private float _currentEnergy;

        /// <summary> Total Development Points (DP) spent across all sessions. Drives cost curves. </summary>
        private int _totalDPSpent;

        /// <summary> Total Development Points earned across all sessions. Used for UI display and analytics. </summary>
        private int _totalDPEarned;
        private int _dpSpentOnActiveBuild;

        /// <summary>
        /// Active multiplier on energy regeneration rate.
        /// Default is 1.0. Modified by SH_EconomicEventManager for Energy Flux events.
        /// </summary>
        private float _energyRegenModifier = 1f;

        /// <summary>
        /// Active multiplier on IC drop rate.
        /// Default is 1.0. Modified by SH_EconomicEventManager for Scarcity events.
        /// </summary>
        private float _icDropRateModifier = 1f;

        /// <summary>
        /// Active multiplier on Scrap reconfiguration cost.
        /// Default is 1.0. Modified by SH_EconomicEventManager for Overload events.
        /// </summary>
        private float _reconfigCostModifier = 1f;

        /// <summary>
        /// Guards all public methods against execution before Initialize() is called.
        /// </summary>
        private bool _isInitialized;

        /// <summary> Current Identity Core count. Read-only from external systems. </summary>
        public int CurrentIdentityCores => _currentIdentityCores;

        /// <summary> Current Scrap amount. Read-only from external systems. </summary>
        public float CurrentScrap => _currentScrap;

        /// <summary>
        /// Current Energy level. Read-only from external systems.
        /// Clamped between 0 and maxEnergy at all times.
        /// </summary>
        public float CurrentEnergy => _currentEnergy;

        /// <summary>
        /// Current Energy expressed as a normalized fraction (0.0 to 1.0).
        /// Used by the UI system to drive energy bar fill without raw value exposure.
        /// </summary>
        public float NormalizedEnergy =>
            _settings != null && _settings.maxEnergy > 0f
                ? _currentEnergy / _settings.maxEnergy
                : 0f;

        /// <summary> Total Development Points spent. Used by SH_ProgressionCalculator. </summary>
        public int TotalDPSpent => _totalDPSpent;

        /// <summary> Gets the total number of DP (Development Points) earned. </summary>
        public int TotalDPEarned => _totalDPEarned;
        public int AvailableDevelopmentPoints => _totalDPEarned - _dpSpentOnActiveBuild;

        /// <summary> Active energy regeneration modifier. Exposed for UI diagnostic display. </summary>
        public float EnergyRegenModifier => _energyRegenModifier;

        /// <summary> Active IC drop rate modifier. Exposed for UI diagnostic display. </summary>
        public float ICDropRateModifier => _icDropRateModifier;

        /// <summary> Active reconfiguration cost modifier. Exposed for UI diagnostic display. </summary>
        public float ReconfigCostModifier => _reconfigCostModifier;

        #endregion

        #region Events (Observer Pattern)

        /// <summary>
        /// Fired after any change to a resource value (add or consume).
        /// Parameters: (ResourceType type, float newValue).
        /// Consumed by: UI HUD bars, SH_Debugger telemetry.
        /// Note: For IC, newValue is cast from int to float for interface uniformity.
        /// </summary>
        public event Action<ResourceType, float> OnResourceChanged;

        /// <summary>
        /// Fired after a successful purge operation converts IC into Development Points.
        /// Parameters: (int dpGained, int newTotalDPSpent).
        /// Consumed by: Build system (unlock DP nodes), UI progression screen.
        /// </summary>
        public event Action<int, int> OnDevelopmentPointsGained;

        /// <summary>
        /// Fired after the defeat penalty is applied to resources.
        /// Parameters: (int icLost, float newICCount, float newEnergyAfterFloor).
        /// Consumed by: UI (defeat summary screen), narrative system.
        /// </summary>
        public event Action<int, int, float> OnDefeatPenaltyApplied;

        /// <summary>
        /// Fired after a successful reconfiguration Scrap deduction.
        /// Parameters: (float scrapCost, float newScrapAmount).
        /// Consumed by: Build system (execute reset), UI reconfiguration panel.
        /// </summary>
        public event Action<float, float> OnReconfigurationPaid;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during orchestration.
        /// Sets all resources to their starting values and validates the settings reference.
        /// Energy starts at maximum. IC and Scrap start at zero (earned through gameplay).
        /// </summary>
        /// <param name="settings">
        /// The central economy settings asset. Must not be null.
        /// </param>
        public void Initialize(SH_EconomySettings settings)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_ResourceSystem] Initialization failed on {gameObject.name}: SH_EconomySettings reference is null. Ensure a valid EconomySettings asset is assigned.");
#endif
                return;
            }

            _settings = settings;
            _currentEnergy = _settings.maxEnergy;
            _currentIdentityCores = 0;
            _currentScrap = 0f;
            _totalDPSpent = 0;

            _energyRegenModifier = 1f;
            _icDropRateModifier = 1f;
            _reconfigCostModifier = 1f;

            _isInitialized = true;
        }

        #endregion

        #region Unity Lifecycle

        /// <summary>
        /// Passive energy regeneration tick.
        /// Applies regeneration per frame scaled by deltaTime and the active regen modifier.
        /// Does nothing if not initialized or energy is already at maximum.
        /// </summary>
        private void Update()
        {
            if (!_isInitialized)
                return;

            if (_currentEnergy >= _settings.maxEnergy)
                return;

            float regenThisFrame =
                _settings.energyRegenPerSecond * _energyRegenModifier * Time.deltaTime;

            float previousEnergy = _currentEnergy;
            _currentEnergy = Mathf.Min(_settings.maxEnergy, _currentEnergy + regenThisFrame);

            if (_currentEnergy != previousEnergy)
            {
                OnResourceChanged?.Invoke(ResourceType.EnergyCore, _currentEnergy);
            }
        }

        #endregion

        #region Public API — Resource Queries

        /// <summary>
        /// Returns true if the system currently holds at least the specified amount
        /// of the given resource type. Does not modify any state.
        /// </summary>
        /// <param name="type"> The resource type to query. </param>
        /// <param name="amount"> The minimum amount required. Must be greater than zero. </param>
        public bool HasResource(ResourceType type, float amount)
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] HasResource called before initialization.");
#endif
                return false;
            }
            if (amount <= 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] HasResource called with invalid amount ({amount}). Value must be greater than zero.");
#endif
                return false;
            }
            return type switch
            {
                ResourceType.IdentityCore => _currentIdentityCores >= (int)amount,
                ResourceType.Scrap => _currentScrap >= amount,
                ResourceType.EnergyCore => _currentEnergy >= amount,
                _ => false
            };
        }

        #endregion

        #region Public API — Resource Modification

        /// <summary>
        /// Adds the specified amount of a resource to the current total.
        /// For Identity Core, the IC drop rate modifier is applied before adding,
        /// reflecting active scarcity events that reduce effective IC acquisition.
        /// Energy is clamped to maxEnergy. IC and Scrap have no upper cap.
        /// </summary>
        /// <param name="type"> The resource type to add. </param>
        /// <param name="amount"> Amount to add. Must be greater than zero. </param>
        public void AddResource(ResourceType type, float amount)
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] AddResource called before initialization.");
#endif
                return;
            }
            if (amount <= 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] AddResource called with invalid amount ({amount}). Value must be greater than zero.");
#endif
                return;
            } 
            if (amount >= 1f)
            switch (type)
            {
                case ResourceType.IdentityCore:
                    // Apply IC drop rate modifier for active scarcity events.
                    // Fractional results are floored: partial cores are not awarded.
                    int icToAdd = Mathf.FloorToInt(amount * _icDropRateModifier);
                    if (icToAdd > 0)
                    {
                        _currentIdentityCores += icToAdd;
                        OnResourceChanged?.Invoke(ResourceType.IdentityCore,
                            _currentIdentityCores);
                    }
                    break;

                case ResourceType.Scrap:
                    _currentScrap += amount;
                    OnResourceChanged?.Invoke(ResourceType.Scrap, _currentScrap);
                    break;

                case ResourceType.EnergyCore:
                    float previousEnergy = _currentEnergy;
                    _currentEnergy = Mathf.Min(_settings.maxEnergy, _currentEnergy + amount);
                    if (_currentEnergy != previousEnergy)
                    {
                        OnResourceChanged?.Invoke(ResourceType.EnergyCore, _currentEnergy);
                    }
                    break;
            }
        }

        /// <summary>
        /// Attempts to consume the specified amount of a resource.
        /// Returns true and deducts the amount only if sufficient resources exist.
        /// Returns false without modifying state if the amount is insufficient.
        /// Callers must check the return value before proceeding with the action
        /// that depends on the resource cost.
        /// </summary>
        /// <param name="type"> The resource type to consume. </param>
        /// <param name="amount"> Amount to consume. Must be greater than zero. </param>
        /// <returns> True if the resource was successfully consumed. False otherwise. </returns>
        public bool ConsumeResource(ResourceType type, float amount)
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] ConsumeResource called before initialization.");
#endif
                return false;
            }
            if (amount <= 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] ConsumeResource called with invalid amount ({amount}). Value must be greater than zero.");
#endif
                return false;
            }
            if (!HasResource(type, amount))
                return false;

            switch (type)
            {
                case ResourceType.IdentityCore:
                    _currentIdentityCores -= (int)amount;
                    OnResourceChanged?.Invoke(ResourceType.IdentityCore,
                        _currentIdentityCores);
                    break;

                case ResourceType.Scrap:
                    _currentScrap -= amount;
                    OnResourceChanged?.Invoke(ResourceType.Scrap, _currentScrap);
                    break;

                case ResourceType.EnergyCore:
                    _currentEnergy = Mathf.Max(0f, _currentEnergy - amount);
                    OnResourceChanged?.Invoke(ResourceType.EnergyCore, _currentEnergy);
                    break;
            }

            return true;
        }

        #endregion

        #region Public API — Economic Operations

        /// <summary>
        /// Converts accumulated Identity Cores into Development Points using the
        /// exponential cost formula defined in SH_EconomySettings.
        /// Consumes the exact IC required for each DP gained.
        /// Fires OnDevelopmentPointsGained with the total DPs awarded in this operation.
        /// Does nothing if current IC is insufficient for even one DP.
        /// </summary>
        public void PurgeCores()
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] PurgeCores called before initialization.");
#endif
                return;
            }

                int dpGained = 0;

            // Iteratively convert IC to DP as long as the pilot has enough IC
            // to afford the next DP at the current progression level.
            while (true)
            {
                float costForNextDP = SH_ProgressionCalculator.GetICCostForNextDP(
                    _totalDPSpent, _settings);

                if (_currentIdentityCores < costForNextDP)
                    break;

                _currentIdentityCores -= Mathf.CeilToInt(costForNextDP);
                _totalDPSpent++;
                _totalDPEarned++;
                dpGained++;
            }

            if (dpGained > 0)
            {
                OnResourceChanged?.Invoke(ResourceType.IdentityCore, _currentIdentityCores);
                OnDevelopmentPointsGained?.Invoke(dpGained, _totalDPSpent);
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log($"[SH_ResourceSystem] Purge attempted but insufficient IC. Current IC: {_currentIdentityCores}. Cost for next DP: {SH_ProgressionCalculator.GetICCostForNextDP(_totalDPSpent, _settings):F1}.");
#endif
            }
        }

        /// <summary>
        /// Returns how many Development Points would be generated by purging
        /// the current Identity Core count, without executing the transaction.
        /// Used by SH_UIBridge to show the yield preview in the terminal menu.
        /// </summary>
        public int CalculatePurgeDPYield()
        {
            if (!_isInitialized || _currentIdentityCores <= 0) return 0;
            return Mathf.FloorToInt(SH_ProgressionCalculator.GetProgressToNextDP(
                _currentIdentityCores, _totalDPEarned, _settings));
        }

        /// <summary>
        /// Validates and executes the Scrap payment for a build reconfiguration.
        /// Applies the active reconfiguration cost modifier from economic events.
        /// Returns true and deducts Scrap if the pilot can afford the cost.
        /// Returns false without modifying state if Scrap is insufficient.
        /// The build system must call this before executing the actual DP reset.
        /// </summary>
        /// <returns>
        /// True if Scrap was successfully deducted and reconfiguration is authorized.
        /// </returns>
        public bool RequestReconfiguration()
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] RequestReconfiguration called before initialization.");
#endif
                return false;
            }

            float baseCost = SH_ProgressionCalculator.GetReconfigCost(
                _totalDPSpent, _settings);

            float finalCost = baseCost * _reconfigCostModifier;
            if (_currentScrap < finalCost) 
            {
#if UNITY_EDITOR
                Debug.Log($"[SH_ResourceSystem] Reconfiguration denied. Required Scrap: {finalCost:F1} (base: {baseCost:F1} x modifier: {_reconfigCostModifier:F2}). Current Scrap: {_currentScrap:F1}.");
#endif
                return false;
            }

            _currentScrap -= finalCost;
            OnResourceChanged?.Invoke(ResourceType.Scrap, _currentScrap);
            OnReconfigurationPaid?.Invoke(finalCost, _currentScrap);
            return true;
        }

        /// <summary>
        /// Applies the defeat penalty to IC and EC resources.
        /// IC: Loses the fraction defined by (1 - icDefeatRetentionRate).
        ///     Fractional losses are floored to avoid removing more than intended.
        /// EC: Enforces the energyDefeatFloor, ensuring the Mecha retains
        ///     at least the minimum Energy percentage defined in settings.
        /// Fires OnDefeatPenaltyApplied with the resulting state.
        /// Intended to be called by the system subscribed to SH_HealthComponent.OnDefeated.
        /// </summary>
        public void ApplyDefeatPenalty()
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] ApplyDefeatPenalty called before initialization.");
#endif
                return;
            }

            // Scrap: apply retention rate configured in settings.
            float scrapLost = _currentScrap * (1f - _settings.scrapDefeatRetentionRate);
            _currentScrap = Mathf.Max(0f, _currentScrap - scrapLost);
            OnResourceChanged?.Invoke(ResourceType.Scrap, _currentScrap);

            // Energy: apply retention rate but never drop below the configured floor.
            float energyFloor = _settings.maxEnergy * _settings.energyFloorFraction;
            float energyAfterPenalty = _currentEnergy * _settings.energyDefeatRetentionRate;
            _currentEnergy = Mathf.Max(energyFloor, energyAfterPenalty);
            OnResourceChanged?.Invoke(ResourceType.EnergyCore, _currentEnergy);

            // Identity Cores not yet purified are lost.
            _currentIdentityCores = 0;
            OnResourceChanged?.Invoke(ResourceType.IdentityCore, 0f);

#if UNITY_EDITOR
            Debug.Log($"[SH_ResourceSystem] Defeat penalty applied. " +
                      $"Scrap: {_currentScrap:F0} | Energy: {_currentEnergy:F1} " +
                      $"(floor: {energyFloor:F1}) | IC: 0.");
#endif
        }

        #endregion

        #region Public API — Event Modifiers

        /// <summary>
        /// Sets the active energy regeneration rate modifier.
        /// Called by SH_EconomicEventManager when an Energy Flux event activates or deactivates.
        /// A value of 1.0 represents the unmodified base regeneration rate.
        /// </summary>
        /// <param name="modifier"> Multiplier for energyRegenPerSecond. Must be non-negative. </param>
        public void SetEnergyRegenModifier(float modifier)
        {
            if (modifier < 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] SetEnergyRegenModifier received negative value ({modifier}). Clamping to 0.");
#endif
                modifier = 0f;
            }

            _energyRegenModifier = modifier;
        }

        /// <summary>
        /// Sets the active IC drop rate modifier.
        /// Called by SH_EconomicEventManager when an Identity Core Scarcity event
        /// activates or deactivates.
        /// A value of 1.0 represents the unmodified base drop rate.
        /// </summary>
        /// <param name="modifier"> Multiplier for IC acquisition. Must be between 0 and 1. </param>
        public void SetICDropRateModifier(float modifier)
        {
            if (modifier < 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] SetICDropRateModifier received negative value ({modifier}). Clamping to 0.");
#endif
                modifier = 0f;
            }

            _icDropRateModifier = modifier;
        }

        /// <summary>
        /// Sets the active reconfiguration cost modifier.
        /// Called by SH_EconomicEventManager when a Reconfiguration Overload event
        /// activates or deactivates.
        /// A value of 1.0 represents the unmodified base reconfiguration cost.
        /// </summary>
        /// <param name="modifier"> Multiplier for reconfiguration Scrap cost. Must be >= 1. </param>
        public void SetReconfigCostModifier(float modifier)
        {
            if (modifier < 1f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_ResourceSystem] SetReconfigCostModifier received value below 1.0 ({modifier}). Clamping to 1.0. Reconfiguration cost modifiers cannot reduce base cost.");
#endif
                modifier = 1f;
            }

            _reconfigCostModifier = modifier;
        }

        #endregion

        #region Editor Debug API

        /// <summary>
        /// Forces addition of resources directly from the Unity Inspector context menu.
        /// For debugging and playtesting only. Not available in release builds.
        /// </summary>
        [ContextMenu("Debug — Add 50 Identity Cores")]
        private void Debug_AddIdentityCores()
        {
            AddResource(ResourceType.IdentityCore, 50f);
        }

        [ContextMenu("Debug — Add 200 Scrap")]
        private void Debug_AddScrap()
        {
            AddResource(ResourceType.Scrap, 200f);
        }

        [ContextMenu("Debug — Fill Energy")]
        private void Debug_FillEnergy()
        {
            AddResource(ResourceType.EnergyCore, _settings != null ? _settings.maxEnergy : 100f);
        }

        [ContextMenu("Debug — Trigger Defeat Penalty")]
        private void Debug_TriggerDefeatPenalty()
        {
            ApplyDefeatPenalty();
        }

        [ContextMenu("Debug — Purge Cores")]
        private void Debug_PurgeCores()
        {
            PurgeCores();
        }

        public void SpendDevelopmentPoint(int amount)
        {
            _dpSpentOnActiveBuild = Mathf.Min(
                _dpSpentOnActiveBuild + amount, _totalDPEarned);
        }

        public void ReturnDevelopmentPoints(int amount)
        {
            _dpSpentOnActiveBuild = Mathf.Max(0, _dpSpentOnActiveBuild - amount);
        }

        #endregion
    }
}