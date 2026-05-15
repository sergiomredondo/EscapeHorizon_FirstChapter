using System;
using UnityEngine;
using Core;
using Game.Economy;

namespace Game.Combat.Core
{
    /// <summary>
    /// Manages the Energy Surge bar accumulation, activation trigger, and post-Surge
    /// cooldown penalty defined in GDD §5.3.2.
    ///
    /// Surge bar fills when the player:
    ///   - Deals damage to enemies (OnHitDealt event).
    ///   - Receives damage from enemies (OnDamageReceived event).
    ///
    /// When the bar reaches 100%, SH_PlayerCombatController.ActivateSurge() is called
    /// automatically. The bar drains progressively during the Surge state.
    ///
    /// After Surge ends, a Cooldown period begins where the bar cannot refill and
    /// player stats are reduced by surgeCooldownPenalty (SH_CombatSettings).
    ///
    /// Lives on the Bear GameObject. Initialized by SH_PlayerContext.
    ///
    /// Terminology (GDD §5.3.2 → code):
    ///   Barra de Sobrecarga (100%)  → SurgeBar (0–1 normalized)
    ///   Sobrecarga activa           → IsSurgeActive (on SH_PlayerCombatController)
    ///   Enfriamiento                → IsInSurgeCooldown (on SH_PlayerCombatController)
    ///
    /// Responsibility boundaries:
    ///   OWNS: Bar accumulation logic, activation threshold, drain rate.
    ///   DOES NOT OWN: Surge state itself (SH_PlayerCombatController).
    ///   DOES NOT OWN: Damage formula multipliers (SH_CombatSettings/SH_DamageCalculator).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_EnergySurgeSystem : MonoBehaviour
    {
        #region Dependencies

        private SH_PlayerContext          _context;
        private SH_PlayerCombatController _combatController;
        private SH_HealthComponent        _health;
        private bool                      _isInitialized;

        #endregion

        #region Serialized Settings

        [Header("Surge Bar Settings")]

        [Tooltip("Fraction of the surge bar filled per unit of damage dealt to an enemy. " +
                 "GDD §5.3.2: bar fills from damage dealt or received.")]
        [Range(0f, 1f)]
        [SerializeField] private float _gainPerDamageDealt = 0.012f;

        [Tooltip("Fraction of the surge bar filled per unit of damage received from an enemy. " +
                 "Taking damage also fills the surge bar, encouraging aggressive play.")]
        [Range(0f, 1f)]
        [SerializeField] private float _gainPerDamageReceived = 0.008f;

        [Tooltip("Rate at which the surge bar drains per second while Surge is active. " +
                 "At 0.1/s the bar lasts 10 seconds at full capacity.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _drainRatePerSecond = 0.1f;

        [Tooltip("Rate at which the surge bar refills per second when idle (no Surge, no cooldown). " +
                 "Set to 0 to disable passive regen — bar only fills from combat.")]
        [Range(0f, 0.1f)]
        [SerializeField] private float _passiveDecayRate = 0.005f;

        [Tooltip("Minimum SurgeBar fraction (0–1) required to manually activate Surge. " +
         "Prevents activation below this threshold even when SurgePressed is true.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float _activationThreshold = 0.8f;

        #endregion

        #region Runtime State

        /// <summary>
        /// Current surge bar value, normalized 0–1.
        /// 0 = empty, 1 = full (auto-activates Surge).
        /// </summary>
        public float SurgeBar { get; private set; }

        /// <summary>
        /// True if the player can manually activate Surge (e.g. by holding the Surge button).
        /// </summary>
        public bool CanActivateSurge =>
            _isInitialized
            && SurgeBar >= _activationThreshold
            && !_combatController.IsSurgeActive
            && !_combatController.IsInSurgeCooldown;

        #endregion

        #region Events

        /// <summary>
        /// Fired whenever the surge bar value changes.
        /// Parameters: (float normalizedValue 0–1).
        /// Consumed by: HUD surge bar, SH_Debugger.
        /// </summary>
        public event Action<float> OnSurgeBarChanged;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext.OrchestrateSubsystems().
        /// Subscribes to HealthComponent.OnDamageReceived for bar accumulation on hits taken.
        /// </summary>
        public void Initialize(SH_PlayerContext context, SH_PlayerCombatController combatController)
        {
            if (context == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_EnergySurgeSystem] Initialize: null context on {gameObject.name}.");
#endif
                return;
            }
            if (combatController == null)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_EnergySurgeSystem] Initialize: null combatController on {gameObject.name}.");
#endif
                return;
            }

            _context          = context;
            _combatController = combatController;
            _health           = context.Health;

            // Subscribe: damage received fills the bar
            if (_health != null)
                _health.OnDamageReceived += OnDamageReceived;

            SurgeBar        = 0f;
            _isInitialized  = true;
        }

        private void OnDestroy()
        {
            if (_health != null)
                _health.OnDamageReceived -= OnDamageReceived;
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (!_isInitialized) return;

            bool surgeActive   = _combatController.IsSurgeActive;
            bool surgeCooldown = _combatController.IsInSurgeCooldown;

            if (surgeActive)
            {
                TickDrainBar();
            }
            else if (!surgeCooldown && SurgeBar > 0f)
            {
                // Passive slow decay when not in surge or cooldown
                float prev = SurgeBar;
                SurgeBar = Mathf.Max(0f, SurgeBar - _passiveDecayRate * Time.deltaTime);
                if (!Mathf.Approximately(SurgeBar, prev))
                    OnSurgeBarChanged?.Invoke(SurgeBar);
            }
        }

        #endregion

        #region Bar Accumulation

        /// <summary>
        /// Notifies the surge system that the player dealt damage to an enemy.
        /// Called by SH_HitboxController after a successful hit delivery.
        /// Fills the bar proportionally to damage dealt.
        /// </summary>
        /// <param name="effectiveDamage">The EffectiveDamage value from SH_DamagePayload.</param>
        public void NotifyDamageDealt(float effectiveDamage)
        {
            if (!_isInitialized || _combatController.IsSurgeActive ||
                _combatController.IsInSurgeCooldown) return;

            float gain = effectiveDamage * _gainPerDamageDealt;
            AddToBar(gain);
        }

        /// <summary>
        /// Receives damage-taken events from SH_HealthComponent.
        /// Fills the bar proportionally to damage received.
        /// </summary>
        private void OnDamageReceived(float newDurability, float maxDurability, float damageTaken)
        {
            if (!_isInitialized || _combatController.IsSurgeActive ||
                _combatController.IsInSurgeCooldown) return;

            float gain = damageTaken * _gainPerDamageReceived;
            AddToBar(gain);
        }

        private void AddToBar(float amount)
        {
            float prev = SurgeBar;
            SurgeBar = Mathf.Clamp01(SurgeBar + amount);

            if (!Mathf.Approximately(SurgeBar, prev))
                OnSurgeBarChanged?.Invoke(SurgeBar);
        }

        private void TickDrainBar()
        {
            float prev = SurgeBar;
            SurgeBar = Mathf.Max(0f, SurgeBar - _drainRatePerSecond * Time.deltaTime);

            if (!Mathf.Approximately(SurgeBar, prev))
                OnSurgeBarChanged?.Invoke(SurgeBar);

            // Surge ends when bar is drained
            if (SurgeBar <= 0f)
            {
                // SH_PlayerCombatController manages its own EndSurge() via its timer.
                // The bar reaching 0 is the visual signal; CombatController drives state.
                SurgeBar = 0f;
            }
        }

        #endregion

        #region Debug API

        [ContextMenu("Debug — Fill Surge Bar")]
        private void Debug_FillBar()
        {
            if (!Application.isPlaying) return;
            AddToBar(1f);
        }

        [ContextMenu("Debug — Empty Surge Bar")]
        private void Debug_EmptyBar()
        {
            SurgeBar = 0f;
            OnSurgeBarChanged?.Invoke(SurgeBar);
        }

        #endregion
    }
}
