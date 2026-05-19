using UnityEngine;
using System;
using Game.Economy.Data;

namespace Game.Economy
{
    /// <summary>
    /// Manages the structural integrity (Durability/HP) of the Mecha unit.
    /// Operates as a fully autonomous component: it does not access the resource
    /// system directly. Communication with external systems (economy, UI, FSM)
    /// occurs exclusively through the exposed C# events following the Observer pattern.
    /// 
    /// Responsibility boundaries:
    ///   - OWNS: Current and maximum Durability values.
    ///   - OWNS: Damage reception logic and defeat threshold detection.
    ///   - OWNS: Repair method (called by external systems that manage resource cost).
    ///   - DOES NOT OWN: Scrap consumption logic (belongs to SH_ResourceSystem).
    ///   - DOES NOT OWN: Defeat penalty on resources (belongs to SH_ResourceSystem).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_HealthComponent : MonoBehaviour
    {
        #region Dependencies

        /// <summary>
        /// Reference to the central economy settings asset.
        /// Provides maxDurability, defeatThreshold, and durabilityPerScrap values.
        /// </summary>
        private SH_EconomySettings _settings;

        /// <summary> Current Durability value. Clamped between 0 and maxDurability. </summary>
        private float _currentDurability;

        /// <summary>
        /// Tracks whether the component is in a defeated state to prevent
        /// multiple OnDefeated event firings from rapid successive damage calls.
        /// </summary>
        private bool _isDefeated;

        /// <summary>
        /// Optional threshold for triggering a retreat behavior before actual defeat.
        /// </summary>
        private float _retreatThreshold = 0f;
        private bool _retreatTriggered = false;

        /// <summary>
        /// Flag to indicate temporary invulnerability states (e.g., during certain actions or effects).
        /// When true, TakeDamage will ignore incoming damage and not trigger events.
        /// </summary>
        private bool _isInvulnerable;

        /// <summary>
        /// Tracks whether the component has been initialized with valid settings.
        /// Guards all public methods against execution before initialization.
        /// </summary>
        private bool _isInitialized;

        /// <summary> Current Durability value. Read-only from external systems. </summary>
        public float CurrentDurability => _currentDurability;

        /// <summary> Maximum Durability defined by the economy settings asset. </summary>
        public float MaxDurability => _settings != null ? _settings.maxDurability : 0f;

        /// <summary>
        /// Current Durability expressed as a normalized fraction (0.0 to 1.0).
        /// Used by the UI system to drive health bar fill without raw value exposure.
        /// </summary>
        public float NormalizedDurability =>
            _settings != null && _settings.maxDurability > 0f
                ? _currentDurability / _settings.maxDurability
                : 0f;

        /// <summary>
        /// Returns true if the Mecha is currently in a defeated state.
        /// External systems (FSM, narrative) query this to trigger escape sequences.
        /// </summary>
        public bool IsDefeated => _isDefeated;

        /// <summary>
        /// Returns true if the Mecha is currently invulnerable. Used by the FSM and action states
        /// to gate damage application during i-frame windows. This is a simple flag; the logic to set and clear it
        /// must be implemented by the relevant systems (e.g., SH_SurgeState sets it to true during surge activation).
        /// </summary>
        public bool IsInvulnerable => _isInvulnerable;

        #endregion

        #region Events (Observer Pattern)

        /// <summary>
        /// Fired immediately after any damage is applied and Durability changes.
        /// Parameters: (float newDurability, float maxDurability, float damageTaken).
        /// Consumed by: UI health bar, animation system, camera shake triggers.
        /// </summary>
        public event Action<float, float, float> OnDamageReceived;

        /// <summary>
        /// Fired immediately after a repair action restores Durability.
        /// Parameters: (float newDurability, float maxDurability, float amountRepaired).
        /// Consumed by: UI health bar, audio feedback system.
        /// </summary>
        public event Action<float, float, float> OnRepaired;

        /// <summary>
        /// Fired once when Durability drops at or below the defeat threshold.
        /// Parameters: none.
        /// Consumed by: SH_ResourceSystem (ApplyDefeatPenalty),
        ///              FSM (trigger escape sequence), narrative system.
        /// </summary>
        public event Action OnDefeated;

        /// <summary>
        /// Fired when Durability enters the critical warning zone (below 25% of max).
        /// Parameters: (float normalizedDurability).
        /// Consumed by: UI (critical health visual effect), audio (warning cue).
        /// </summary>
        public event Action<float> OnCriticalState;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during
        /// orchestration to inject the required settings reference.
        /// Sets Durability to maximum and resets defeat state.
        /// </summary>
        /// <param name="settings">
        /// The central economy settings asset. Must not be null.
        /// </param>
        public void Initialize(SH_EconomySettings settings)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_HealthComponent] Initialization failed on {gameObject.name}: SH_EconomySettings reference is null. Ensure a valid EconomySettings asset is assigned.");
#endif
                return;
            }

                _settings = settings;
            _currentDurability = _settings.maxDurability;
            _isDefeated = false;
            _isInitialized = true;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Sets the retreat health threshold in absolute durability units.
        /// Called by SH_PlayerContext during orchestration after EconomySettings are available.
        /// When current durability drops below this value, OnDefeated fires and the
        /// tactical retreat sequence begins. The Mecha is not destroyed at zero HP.
        /// </summary>
        public void SetRetreatThreshold(float thresholdAbsolute)
        {
            _retreatThreshold = Mathf.Max(0f, thresholdAbsolute);
        }

        /// <summary>
        /// Applies damage to the Mecha, reducing current Durability.
        /// Clamps the result to zero and evaluates defeat and critical state conditions.
        /// Does nothing if the component is already in a defeated state.
        /// </summary>
        /// <param name="damageAmount">
        /// Raw damage value to subtract. Must be greater than zero.
        /// </param>
        public void TakeDamage(float damageAmount)
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_HealthComponent] TakeDamage called on {gameObject.name} before initialization. Call Initialize() first.");
#endif
                return; 
            }
            if (_isDefeated) return;
            if (_isInvulnerable) return;
            if (damageAmount <= 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_HealthComponent] TakeDamage called with invalid damageAmount ({damageAmount}). Value must be greater than zero.");
#endif
                return; 
            }

            float previousDurability = _currentDurability;
            _currentDurability = Mathf.Max(0f, _currentDurability - damageAmount);
            float actualDamage = previousDurability - _currentDurability;

            OnDamageReceived?.Invoke(_currentDurability, _settings.maxDurability, actualDamage);

            EvaluateCriticalState();

            // Trigger tactical retreat when durability crosses the retreat threshold.
            // Guards against re-triggering if multiple hits land in the same frame.
            if (!_retreatTriggered && _retreatThreshold > 0f
                && _currentDurability <= _retreatThreshold)
            {
                _retreatTriggered = true;
                _isDefeated = true;
                OnDefeated?.Invoke();
            }
        }

        /// <summary>
        /// Restores Durability by the specified amount, clamped to maxDurability.
        /// This method does NOT consume Scrap. The caller (interlude UI or resource system)
        /// is responsible for verifying and deducting the Scrap cost before calling Repair.
        /// </summary>
        /// <param name="repairAmount">
        /// Durability points to restore. Must be greater than zero.
        /// </param>
        public void Repair(float repairAmount)
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_HealthComponent] Repair called on {gameObject.name} before initialization. Call Initialize() first.");
#endif
                return;
            }
            if (repairAmount <= 0f) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_HealthComponent] Repair called with invalid repairAmount ({repairAmount}). Value must be greater than zero.");
#endif
                return;
            }

            float previousDurability = _currentDurability;
            _currentDurability = Mathf.Min(_settings.maxDurability, _currentDurability + repairAmount);
            float actualRepair = _currentDurability - previousDurability;

            OnRepaired?.Invoke(_currentDurability, _settings.maxDurability, actualRepair);
        }

        /// <summary>
        /// Resets the component to its full Durability state and clears the defeated flag.
        /// Called by the game flow system when the pilot returns to base after a defeat,
        /// or at the start of a new run. Does not trigger any events.
        /// </summary>
        public void ResetToFull()
        {
            if (!_isInitialized) 
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_HealthComponent] ResetToFull called on {gameObject.name} before initialization. Call Initialize() first.");
#endif
                return;
            }

            _currentDurability = _settings.maxDurability;
            _isDefeated = false;
            _retreatTriggered = false;

            OnRepaired?.Invoke(_currentDurability, _settings.maxDurability, _settings.maxDurability);
        }

        /// <summary>
        /// Sets whether the object is invulnerable to damage or effects.
        /// When invulnerable, TakeDamage will ignore incoming damage and not trigger events.
        /// </summary>
        /// <param name="invulnerable">true to make the object invulnerable; otherwise, false.</param>
        public void SetInvulnerable(bool invulnerable) => _isInvulnerable = invulnerable;

        #endregion

        #region Internal Logic

        /// <summary>
        /// Evaluates whether the current Durability qualifies as a critical state
        /// (below 25% of maximum) and fires the corresponding event.
        /// The 25% threshold is a design constant for the warning zone,
        /// distinct from the defeat threshold defined in settings.
        /// </summary>
        private void EvaluateCriticalState()
        {
            const float criticalWarningThreshold = 0.25f;

            if (NormalizedDurability <= criticalWarningThreshold)
            {
                OnCriticalState?.Invoke(NormalizedDurability);
            }
        }

        /// <summary>
        /// Evaluates whether current Durability has reached or crossed the defeat threshold
        /// defined in SH_EconomySettings. Fires OnDefeated exactly once per defeat event.
        /// Uses the _isDefeated flag to prevent duplicate event firing.
        /// </summary>
        private void EvaluateDefeatCondition()
        {
            if (_isDefeated) return;

            float defeatDurabilityThreshold =
                _settings.maxDurability * _settings.defeatThreshold;

            if (_currentDurability <= defeatDurabilityThreshold)
            {
                _isDefeated = true;
                OnDefeated?.Invoke();
            }
        }

        #endregion
    }
}