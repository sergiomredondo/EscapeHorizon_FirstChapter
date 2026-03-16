using System;
using System.Collections.Generic;
using UnityEngine;
using Core;
using Game.Enemy;

namespace Game.Combat.Core
{
    /// <summary>
    /// Central difficulty management system for Escape Horizon.
    /// Implements the two-layer difficulty model from GDD §5.3.6:
    ///
    ///   Layer 1 — Fixed zone scaling:
    ///     Each zone has a base multiplier applied to all enemy HP and AI aggressiveness
    ///     when the player enters. Enemies in zone 2 are 10% harder than zone 1, etc.
    ///     Boss fights spike beyond the zone curve (applied separately by the encounter
    ///     trigger that spawns the boss).
    ///
    ///   Layer 2 — Dynamic AI adjustment (mini-loop, GDD §5.3.6.4):
    ///     Every 60 seconds of active combat, the system measures two player performance
    ///     metrics and adjusts enemy AI aggressiveness within a ±20% band:
    ///       RDIR (DamageDealtReceivedRatio): damage dealt / damage received.
    ///       SER  (SuccessfulEvasionRate):    evasion events per minute.
    ///
    ///   Layer 3 — Configurable difficulty presets (GDD §5.3.6.5):
    ///     Applied as flat multipliers on top of zone scaling. Selected at game start.
    ///
    /// Responsibility boundaries:
    ///   OWNS: Zone scaling application, dynamic AI loop, difficulty preset multipliers.
    ///   DOES NOT OWN: Enemy HP or stats (owned by SH_EnemyController).
    ///   DOES NOT OWN: Player performance measurement (reads from event subscriptions).
    ///   DOES NOT OWN: UI difficulty display (fires events for the UI system).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_DifficultyManager : MonoBehaviour
    {
        #region Dependencies

        private SH_PlayerContext _context;
        private bool             _isInitialized;

        #endregion

        #region Configuration

        [Header("Difficulty")]

        [Tooltip("Currently active difficulty preset. Applied on top of zone scaling. " +
                 "GDD §5.3.6 table: Normal=1.0×, Hard=1.2× HP / 1.3× ATK, etc.")]
        [SerializeField] private DifficultyLevel _activeDifficulty = DifficultyLevel.Normal;

        [Header("Zone Scaling")]

        [Tooltip("HP multiplier increase per zone above zone 1. " +
                 "GDD §5.3.6: linear +10% per zone → default 0.1.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _hpScalingPerZone = 0.1f;

        [Tooltip("Current zone index (1-based). Updated by NotifyZoneEntered().")]
        [SerializeField, Min(1)] private int _currentZone = 1;

        [Header("Dynamic AI Loop (GDD §5.3.6.4)")]

        [Tooltip("Interval in seconds between AI aggressiveness evaluations. " +
                 "GDD §5.3.6.4: 60 seconds of active combat.")]
        [Min(10f)]
        [SerializeField] private float _dynamicLoopInterval = 60f;

        [Tooltip("RDIR threshold above which the player is considered dominant. " +
                 "If RDIR > this AND SER > serDominantThreshold, AI aggressiveness +10%.")]
        [Min(1f)]
        [SerializeField] private float _rdirDominantThreshold = 3.0f;

        [Tooltip("RDIR threshold below which the player is considered struggling. " +
                 "If RDIR < this AND SER < serStruggleThreshold, AI aggressiveness -5%.")]
        [Min(0.1f)]
        [SerializeField] private float _rdirStruggleThreshold = 1.0f;

        [Tooltip("SER (evasions/min) threshold for dominant detection.")]
        [Min(0f)]
        [SerializeField] private float _serDominantThreshold = 0.8f;

        [Tooltip("SER (evasions/min) threshold for struggle detection.")]
        [Min(0f)]
        [SerializeField] private float _serStruggleThreshold = 0.4f;

        [Tooltip("Maximum deviation from base AI aggressiveness allowed by the dynamic loop. " +
                 "GDD §5.3.6.4: ±20%.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _maxAIDeviation = 0.2f;

        #endregion

        #region Runtime State

        /// <summary>
        /// Current AI aggressiveness multiplier applied by the dynamic loop.
        /// 1.0 = base. Clamped within [1 - _maxAIDeviation, 1 + _maxAIDeviation].
        /// </summary>
        public float CurrentAIMultiplier { get; private set; } = 1f;

        /// <summary>
        /// Cumulative zone factor (1.0 for zone 1, 1.1 for zone 2, etc.).
        /// </summary>
        public float CurrentZoneFactor => 1f + (_currentZone - 1) * _hpScalingPerZone;

        // ─── Performance metrics (reset each dynamic loop interval) ─────────

        private float _damageDealtThisInterval;
        private float _damageReceivedThisInterval;
        private int   _evasionEventsThisInterval;
        private float _dynamicLoopTimer;

        // ─── Active enemies tracked for scaling ─────────────────────────────
        private readonly List<SH_EnemyController> _trackedEnemies =
            new List<SH_EnemyController>();

        #endregion

        #region Events

        /// <summary>
        /// Fired after the dynamic AI loop updates CurrentAIMultiplier.
        /// Parameters: (float newMultiplier).
        /// Consumed by: SH_Debugger telemetry, UI difficulty indicator.
        /// </summary>
        public event Action<float> OnAIMultiplierChanged;

        /// <summary>
        /// Fired when the zone changes.
        /// Parameters: (int newZoneIndex, float zoneFactor).
        /// Consumed by: narrative system, zone transition UI.
        /// </summary>
        public event Action<int, float> OnZoneChanged;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext.OrchestrateSubsystems().
        /// Subscribes to HealthComponent events to measure damage received.
        /// </summary>
        public void Initialize(SH_PlayerContext context)
        {
            if (context == null)
            {
                Debug.LogError($"[SH_DifficultyManager] Initialize: null context on {gameObject.name}.");
                return;
            }

            _context = context;

            // Subscribe to player damage received for RDIR measurement
            if (_context.Health != null)
                _context.Health.OnDamageReceived += OnPlayerDamageReceived;

            _isInitialized = true;

            Debug.Log($"[SH_DifficultyManager] Initialized. " +
                      $"Difficulty: {_activeDifficulty}, Zone: {_currentZone}.");
        }

        private void OnDestroy()
        {
            if (_context?.Health != null)
                _context.Health.OnDamageReceived -= OnPlayerDamageReceived;
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (!_isInitialized) return;

            _dynamicLoopTimer += Time.deltaTime;
            if (_dynamicLoopTimer >= _dynamicLoopInterval)
            {
                _dynamicLoopTimer = 0f;
                RunDynamicAILoop();
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Sets the active difficulty level and immediately re-scales all tracked enemies.
        /// Called from the options menu at game start or during gameplay if allowed.
        /// </summary>
        public void SetDifficulty(DifficultyLevel level)
        {
            _activeDifficulty = level;
            RescaleAllTrackedEnemies();
            Debug.Log($"[SH_DifficultyManager] Difficulty set to {level}.");
        }

        /// <summary>
        /// Notifies the manager that the player has entered a new zone.
        /// Applies zone scaling to all tracked enemies and fires OnZoneChanged.
        /// </summary>
        /// <param name="zoneIndex">New 1-based zone index.</param>
        public void NotifyZoneEntered(int zoneIndex)
        {
            _currentZone = Mathf.Max(1, zoneIndex);
            RescaleAllTrackedEnemies();
            OnZoneChanged?.Invoke(_currentZone, CurrentZoneFactor);

            Debug.Log($"[SH_DifficultyManager] Zone {_currentZone} entered. " +
                      $"Zone factor: {CurrentZoneFactor:F2}.");
        }

        /// <summary>
        /// Registers a newly spawned enemy for zone scaling and dynamic tracking.
        /// Call this immediately after instantiating an SH_EnemyController.
        /// </summary>
        public void RegisterEnemy(SH_EnemyController enemy)
        {
            if (enemy == null) return;
            if (!_trackedEnemies.Contains(enemy))
                _trackedEnemies.Add(enemy);

            enemy.ApplyZoneScaling(CurrentZoneFactor, _activeDifficulty);
        }

        /// <summary>
        /// Removes a defeated or despawned enemy from the tracking list.
        /// </summary>
        public void UnregisterEnemy(SH_EnemyController enemy)
        {
            _trackedEnemies.Remove(enemy);
        }

        /// <summary>
        /// Notifies the system that the player successfully dealt damage.
        /// Called by SH_HitboxController or SH_PlayerCombatController after hit delivery.
        /// </summary>
        public void NotifyDamageDealt(float amount)
        {
            _damageDealtThisInterval += amount;
        }

        /// <summary>
        /// Notifies the system that the player successfully evaded an attack.
        /// Called by the dash/dodge system when an evasion i-frame covers an incoming hit.
        /// </summary>
        public void NotifySuccessfulEvasion()
        {
            _evasionEventsThisInterval++;
        }

        #endregion

        #region Dynamic AI Loop (GDD §5.3.6.4)

        private void RunDynamicAILoop()
        {
            if (_damageReceivedThisInterval <= 0f && _damageDealtThisInterval <= 0f)
            {
                ResetIntervalMetrics();
                return;
            }

            float rdir = _damageReceivedThisInterval > 0f
                ? _damageDealtThisInterval / _damageReceivedThisInterval
                : _damageDealtThisInterval > 0f ? 10f : 1f;

            float ser  = _evasionEventsThisInterval / (_dynamicLoopInterval / 60f);

            float prevMultiplier = CurrentAIMultiplier;

            if (rdir > _rdirDominantThreshold && ser > _serDominantThreshold)
            {
                CurrentAIMultiplier += 0.1f;
                Debug.Log($"[SH_DifficultyManager] Player dominant (RDIR={rdir:F1}, SER={ser:F1}). " +
                          $"AI aggression +10%.");
            }
            else if (rdir < _rdirStruggleThreshold && ser < _serStruggleThreshold)
            {
                CurrentAIMultiplier -= 0.05f;
                Debug.Log($"[SH_DifficultyManager] Player struggling (RDIR={rdir:F1}, SER={ser:F1}). " +
                          $"AI aggression -5%.");
            }

            // Clamp within ±20% of base (1.0)
            CurrentAIMultiplier = Mathf.Clamp(
                CurrentAIMultiplier,
                1f - _maxAIDeviation,
                1f + _maxAIDeviation);

            if (!Mathf.Approximately(CurrentAIMultiplier, prevMultiplier))
            {
                ApplyAIMultiplierToTrackedEnemies();
                OnAIMultiplierChanged?.Invoke(CurrentAIMultiplier);
            }

            ResetIntervalMetrics();
        }

        private void ApplyAIMultiplierToTrackedEnemies()
        {
            for (int i = _trackedEnemies.Count - 1; i >= 0; i--)
            {
                if (_trackedEnemies[i] == null || _trackedEnemies[i].IsDead)
                {
                    _trackedEnemies.RemoveAt(i);
                    continue;
                }
                _trackedEnemies[i].ApplyZoneScaling(
                    CurrentZoneFactor * CurrentAIMultiplier, _activeDifficulty);
            }
        }

        private void RescaleAllTrackedEnemies()
        {
            for (int i = _trackedEnemies.Count - 1; i >= 0; i--)
            {
                if (_trackedEnemies[i] == null || _trackedEnemies[i].IsDead)
                {
                    _trackedEnemies.RemoveAt(i);
                    continue;
                }
                _trackedEnemies[i].ApplyZoneScaling(CurrentZoneFactor, _activeDifficulty);
            }
        }

        private void ResetIntervalMetrics()
        {
            _damageDealtThisInterval    = 0f;
            _damageReceivedThisInterval = 0f;
            _evasionEventsThisInterval  = 0;
        }

        #endregion

        #region Event Handlers

        private void OnPlayerDamageReceived(float newDurability, float maxDurability, float damageTaken)
        {
            _damageReceivedThisInterval += damageTaken;
        }

        #endregion

        #region Public Accessors for Telemetry

        public DifficultyLevel ActiveDifficulty => _activeDifficulty;
        public int             CurrentZone      => _currentZone;
        public int             TrackedEnemyCount => _trackedEnemies.Count;

        #endregion

        #region Editor Debug

        [ContextMenu("Debug — Advance to Zone 2")]
        private void Debug_Zone2() { if (Application.isPlaying) NotifyZoneEntered(2); }

        [ContextMenu("Debug — Advance to Zone 3")]
        private void Debug_Zone3() { if (Application.isPlaying) NotifyZoneEntered(3); }

        [ContextMenu("Debug — Set Hard Difficulty")]
        private void Debug_Hard()  { if (Application.isPlaying) SetDifficulty(DifficultyLevel.Hard); }

        [ContextMenu("Debug — Run Dynamic Loop Now")]
        private void Debug_RunLoop() { if (Application.isPlaying) RunDynamicAILoop(); }

        #endregion
    }

    /// <summary>
    /// The four player-selectable difficulty presets from GDD §5.3.6.5.
    /// Multipliers are applied by SH_DifficultyManager and SH_EnemyController.
    ///
    /// GDD terminology → enum:
    ///   Fácil (Analista)    → Easy
    ///   Normal (Ingeniero)  → Normal
    ///   Difícil (Maestría)  → Hard
    ///   Pesadilla (Líder)   → Nightmare
    /// </summary>
    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard,
        Nightmare
    }
}
