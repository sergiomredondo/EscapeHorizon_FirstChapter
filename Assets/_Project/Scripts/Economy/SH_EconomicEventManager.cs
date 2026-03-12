using UnityEngine;
using System;
using System.Collections.Generic;
using Game.Economy.Data;

namespace Game.Economy
{
    /// <summary>
    /// Manages the lifecycle of dynamic economic events that temporarily modify
    /// resource acquisition rates and progression costs at runtime.
    /// 
    /// Implements the three event categories defined in GDD §5.5.4:
    ///   I.   Identity Core Scarcity   — reduces IC drop rate.
    ///   II.  Reconfiguration Overload — increases Scrap reconfig cost.
    ///   III. Energy Flux              — modifies Energy regeneration rate.
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Event activation, duration tracking, and deactivation logic.
    ///   - OWNS: Reconfig frequency tracking for overload threshold detection.
    ///   - OWNS: Probabilistic Energy Flux roll on elite encounters.
    ///   - DOES NOT OWN: Resource values (belongs to SH_ResourceSystem).
    ///   - DOES NOT OWN: Progression math (belongs to SH_ProgressionCalculator).
    ///   - COMMUNICATES WITH SH_ResourceSystem exclusively via modifier setters.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_EconomicEventManager : MonoBehaviour
    {
        #region Dependencies

        /// <summary>
        /// Reference to the economic event settings asset.
        /// Injected via Initialize(). Provides all event parameters and thresholds.
        /// </summary>
        private SH_EconomicEventSettings _eventSettings;

        /// <summary>
        /// Reference to the resource system.
        /// Used exclusively to apply and reset modifier values when events
        /// activate or deactivate. No resource state is read or written directly.
        /// </summary>
        private SH_ResourceSystem _resourceSystem;

        /// <summary>
        /// Tracks the remaining duration (in seconds) for each active event type.
        /// A value of zero or less indicates the event is inactive.
        /// Keyed by EconomicEventType for O(1) lookup.
        /// </summary>
        private readonly Dictionary<EconomicEventType, float> _activeEventTimers =
            new Dictionary<EconomicEventType, float>
            {
                { EconomicEventType.IdentityCoreScarcity,    0f },
                { EconomicEventType.ReconfigurationOverload, 0f },
                { EconomicEventType.EnergyFlux,              0f }
            };

        /// <summary>
        /// Tracks the number of build reconfigurations performed within
        /// the active time window, for overload threshold detection.
        /// </summary>
        private int _reconfigCountInWindow;

        /// <summary>
        /// Timestamp (Time.time) of the oldest reconfig in the current window.
        /// Used to determine when the window has expired and the count should reset.
        /// </summary>
        private float _reconfigWindowStartTime;

        /// <summary>
        /// Guards all public methods against execution before Initialize().
        /// </summary>
        private bool _isInitialized;

        #endregion

        #region Events (Observer Pattern)

        /// <summary>
        /// Fired when any economic event becomes active.
        /// Parameters: (EconomicEventType eventType, float duration).
        /// Consumed by: UI (display active event warning), SH_Debugger telemetry.
        /// </summary>
        public event Action<EconomicEventType, float> OnEventActivated;

        /// <summary>
        /// Fired when any economic event expires or is manually deactivated.
        /// Parameters: (EconomicEventType eventType).
        /// Consumed by: UI (clear active event warning), SH_Debugger telemetry.
        /// </summary>
        public event Action<EconomicEventType> OnEventDeactivated;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during orchestration.
        /// Validates references, resets all event timers, and initializes
        /// the reconfig tracking window.
        /// </summary>
        /// <param name="eventSettings">
        /// The economic event settings asset. Must not be null.
        /// </param>
        /// <param name="resourceSystem">
        /// Reference to the active resource system. Must not be null.
        /// </param>
        public void Initialize(
            SH_EconomicEventSettings eventSettings,
            SH_ResourceSystem resourceSystem)
        {
            if (eventSettings == null) { Debug.LogError($"[SH_EconomicEventManager] Initialization failed on {gameObject.name}: SH_EconomicEventSettings is null."); return;}
            if (resourceSystem == null) { Debug.LogError($"[SH_EconomicEventManager] Initialization failed on {gameObject.name}: SH_ResourceSystem is null."); return;}

            _eventSettings = eventSettings;
            _resourceSystem = resourceSystem;

            // Reset all event timers to inactive state.
            _activeEventTimers[EconomicEventType.IdentityCoreScarcity] = 0f;
            _activeEventTimers[EconomicEventType.ReconfigurationOverload] = 0f;
            _activeEventTimers[EconomicEventType.EnergyFlux] = 0f;

            // Reset reconfig tracking window.
            _reconfigCountInWindow = 0;
            _reconfigWindowStartTime = Time.time;

            _isInitialized = true;
        }

        #endregion

        #region Unity Lifecycle

        /// <summary>
        /// Ticks down active event durations each frame.
        /// Deactivates events whose timers have expired and restores
        /// the corresponding modifiers in SH_ResourceSystem to their neutral values.
        /// </summary>
        private void Update()
        {
            if (!_isInitialized)
                return;

            TickEvent(EconomicEventType.IdentityCoreScarcity, Time.deltaTime);
            TickEvent(EconomicEventType.ReconfigurationOverload, Time.deltaTime);
            TickEvent(EconomicEventType.EnergyFlux, Time.deltaTime);
        }

        #endregion

        #region Public API — Event Triggers

        /// <summary>
        /// Notifies the manager that the pilot has entered a new critical region.
        /// Activates an Identity Core Scarcity event for the duration defined
        /// in the event settings asset, applying the IC drop rate reduction
        /// to SH_ResourceSystem immediately.
        /// If a scarcity event is already active, its timer is refreshed.
        /// </summary>
        /// <param name="regionId">
        /// Identifier of the region entered. Reserved for future filtering logic
        /// (e.g., certain regions may not trigger scarcity). Currently unused
        /// beyond logging.
        /// </param>
        public void NotifyRegionChange(string regionId)
        {
            if (!_isInitialized) { Debug.LogWarning($"[SH_EconomicEventManager] NotifyRegionChange called before initialization."); return;}

            ActivateEvent(
                EconomicEventType.IdentityCoreScarcity,
                _eventSettings.scarcityDuration);
        }

        /// <summary>
        /// Notifies the manager that the pilot has performed a build reconfiguration.
        /// Increments the reconfig counter within the active time window.
        /// If the counter reaches the overload threshold, activates a
        /// Reconfiguration Overload event automatically.
        /// Reconfigurations outside the time window do not accumulate toward the threshold.
        /// </summary>
        public void NotifyReconfigurationPerformed()
        {
            if (!_isInitialized) { Debug.LogWarning($"[SH_EconomicEventManager] NotifyReconfigurationPerformed called before initialization."); return;}

            float currentTime = Time.time;
            float windowElapsed = currentTime - _reconfigWindowStartTime;

            // If the time window has expired, reset the counter and start a new window.
            if (windowElapsed > _eventSettings.reconfigWindowSeconds)
            {
                _reconfigCountInWindow = 0;
                _reconfigWindowStartTime = currentTime;
            }

            _reconfigCountInWindow++;

            if (_reconfigCountInWindow >= _eventSettings.reconfigTriggerThreshold)
            {
                ActivateEvent(
                    EconomicEventType.ReconfigurationOverload,
                    _eventSettings.overloadDuration);

                // Reset counter after triggering to allow a fresh window afterward.
                _reconfigCountInWindow = 0;
                _reconfigWindowStartTime = currentTime;
            }
        }

        /// <summary>
        /// Executes a probabilistic roll to determine whether an Energy Flux event
        /// triggers upon entering combat with an elite enemy.
        /// If triggered, randomly assigns either a positive or negative flux modifier
        /// to SH_ResourceSystem for the duration defined in event settings.
        /// If an Energy Flux event is already active, the new roll replaces it,
        /// allowing dynamic shifts during prolonged elite encounters.
        /// </summary>
        public void RollEnergyEventOnEliteEncounter()
        {
            if (!_isInitialized) { Debug.LogWarning($"[SH_EconomicEventManager] RollEnergyEventOnEliteEncounter called before initialization."); return;}

            float roll = UnityEngine.Random.value;

            if (roll > _eventSettings.energyFluxChance)
            {
                Debug.Log($"[SH_EconomicEventManager] Energy Flux not triggered.");
                return;
            }

            // 50/50 split between positive and negative flux.
            bool isPositive = UnityEngine.Random.value >= 0.5f;

            float modifier = isPositive
                ? _eventSettings.energyFluxPositiveMultiplier
                : _eventSettings.energyFluxNegativeMultiplier;

            // Store flux direction for use in ApplyEventModifier.
            _pendingEnergyFluxModifier = modifier;

            ActivateEvent(
                EconomicEventType.EnergyFlux,
                _eventSettings.energyFluxDuration);
        }

        #endregion

        #region Public API — Event State Queries

        /// <summary>
        /// Returns true if the specified event type is currently active.
        /// Used by UI and debug systems to reflect active event state.
        /// </summary>
        /// <param name="eventType"> The event type to query. </param>
        public bool IsEventActive(EconomicEventType eventType)
        {
            if (!_activeEventTimers.TryGetValue(eventType, out float timer))
                return false;

            return timer > 0f;
        }

        /// <summary>
        /// Returns the remaining duration in seconds of the specified event.
        /// Returns 0 if the event is not active.
        /// Used by UI to display event countdown timers.
        /// </summary>
        /// <param name="eventType"> The event type to query. </param>
        public float GetEventRemainingDuration(EconomicEventType eventType)
        {
            if (!_activeEventTimers.TryGetValue(eventType, out float timer))
                return 0f;

            return Mathf.Max(0f, timer);
        }

        #endregion

        #region Internal Event Lifecycle

        /// <summary>
        /// Stores the pending energy flux modifier between the roll decision
        /// and the ActivateEvent call, so ApplyEventModifier can read it
        /// without requiring a parameter on the shared activation path.
        /// </summary>
        private float _pendingEnergyFluxModifier = 1f;

        /// <summary>
        /// Activates or refreshes an economic event by setting its timer
        /// and applying the corresponding modifier to SH_ResourceSystem.
        /// If the event is already active, its duration is replaced with
        /// the new value (refresh semantics, not additive).
        /// </summary>
        /// <param name="eventType"> The event type to activate. </param>
        /// <param name="duration"> Duration in seconds for the event. </param>
        private void ActivateEvent(EconomicEventType eventType, float duration)
        {
            bool wasAlreadyActive = IsEventActive(eventType);

            _activeEventTimers[eventType] = duration;

            ApplyEventModifier(eventType, active: true);

            if (!wasAlreadyActive)
            {
                OnEventActivated?.Invoke(eventType, duration);
            }
        }

        /// <summary>
        /// Decrements the timer for the specified event type by the given delta.
        /// If the timer expires (reaches zero or below), deactivates the event
        /// and restores the corresponding modifier to its neutral value.
        /// </summary>
        /// <param name="eventType"> The event type to tick. </param>
        /// <param name="deltaTime"> Time elapsed since last frame. </param>
        private void TickEvent(EconomicEventType eventType, float deltaTime)
        {
            if (!_activeEventTimers.TryGetValue(eventType, out float timer))
                return;

            if (timer <= 0f)
                return;

            timer -= deltaTime;
            _activeEventTimers[eventType] = timer;

            if (timer <= 0f)
            {
                DeactivateEvent(eventType);
            }
        }

        /// <summary>
        /// Deactivates an event, resets its timer to zero, restores the neutral
        /// modifier in SH_ResourceSystem, and fires OnEventDeactivated.
        /// </summary>
        /// <param name="eventType"> The event type to deactivate. </param>
        private void DeactivateEvent(EconomicEventType eventType)
        {
            _activeEventTimers[eventType] = 0f;

            ApplyEventModifier(eventType, active: false);

            OnEventDeactivated?.Invoke(eventType);
        }

        /// <summary>
        /// Applies or removes the modifier associated with the given event type
        /// to SH_ResourceSystem. When active is true, applies the event modifier.
        /// When active is false, restores the neutral value (1.0).
        /// </summary>
        /// <param name="eventType"> The event type whose modifier to apply or reset. </param>
        /// <param name="active"> True to apply the event modifier. False to restore neutral. </param>
        private void ApplyEventModifier(EconomicEventType eventType, bool active)
        {
            switch (eventType)
            {
                case EconomicEventType.IdentityCoreScarcity:
                    _resourceSystem.SetICDropRateModifier(
                        active ? _eventSettings.scarcityCoefficient : 1f);
                    break;

                case EconomicEventType.ReconfigurationOverload:
                    _resourceSystem.SetReconfigCostModifier(
                        active ? _eventSettings.overloadCoefficient : 1f);
                    break;

                case EconomicEventType.EnergyFlux:
                    _resourceSystem.SetEnergyRegenModifier(
                        active ? _pendingEnergyFluxModifier : 1f);
                    break;
            }
        }

        #endregion

        #region Editor Debug API

        /// <summary>
        /// Forces activation of the Identity Core Scarcity event from the Inspector.
        /// For debugging and playtesting only.
        /// </summary>
        [ContextMenu("Debug — Activate IC Scarcity")]
        private void Debug_ActivateScarcity()
        {
            if (!_isInitialized) return;
            NotifyRegionChange("DEBUG_REGION");
        }

        /// <summary>
        /// Forces activation of the Reconfiguration Overload event from the Inspector.
        /// For debugging and playtesting only.
        /// </summary>
        [ContextMenu("Debug — Activate Reconfig Overload")]
        private void Debug_ActivateOverload()
        {
            if (!_isInitialized) return;
            ActivateEvent(
                EconomicEventType.ReconfigurationOverload,
                _eventSettings.overloadDuration);
        }

        /// <summary>
        /// Forces an Energy Flux roll from the Inspector, ignoring probability.
        /// Directly activates a random positive or negative Energy Flux.
        /// For debugging and playtesting only.
        /// </summary>
        [ContextMenu("Debug — Force Energy Flux Roll")]
        private void Debug_ForceEnergyFluxRoll()
        {
            if (!_isInitialized) return;
            RollEnergyEventOnEliteEncounter();
        }

        /// <summary>
        /// Deactivates all currently active events and restores all modifiers
        /// to their neutral values. For debugging and playtesting only.
        /// </summary>
        [ContextMenu("Debug — Deactivate All Events")]
        private void Debug_DeactivateAllEvents()
        {
            if (!_isInitialized) return;

            foreach (EconomicEventType eventType in _activeEventTimers.Keys)
            {
                if (IsEventActive(eventType))
                {
                    DeactivateEvent(eventType);
                }
            }
        }

        #endregion
    }
}