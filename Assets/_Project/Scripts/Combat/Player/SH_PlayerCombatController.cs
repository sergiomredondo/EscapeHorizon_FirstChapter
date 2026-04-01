using UnityEngine;
using Core;
using Actions.Data;
using Game.Combat.Data;

namespace Game.Combat.Core
{
    /// <summary>
    /// Orchestrates player-initiated combat actions for the Mecha Bear.
    /// Reads AttackPressed / AttackHeld from SH_InputHandler, determines
    /// light vs heavy attack type, requests the appropriate SH_ActionState
    /// via SH_PlayerStateMachine.RequestAction(), and connects
    /// SH_AnimatorBridge.OnHitImpact() to SH_HitboxController.
    ///
    /// Lives on the Bear GameObject. Initialized by SH_PlayerContext.
    ///
    /// Input model (GDD §5.1.1):
    ///   Tap  (released before heavyAttackHoldThreshold) → Light attack
    ///   Hold (held at least heavyAttackHoldThreshold)   → Heavy attack
    ///
    /// Integration points closed by this script:
    ///   ✓ OnAttack placeholder in SH_InputHandler — consumed here
    ///   ✓ SH_AnimatorBridge.OnHitImpact() placeholder — wired here
    ///
    /// Responsibility boundaries:
    ///   OWNS: Attack input reading, hold-duration classification, action request.
    ///   OWNS: OnHitImpact callback routing to SH_HitboxController.
    ///   OWNS: Energy Surge state tracking (activation condition, duration, cooldown).
    ///   DOES NOT OWN: Damage formula (SH_DamageCalculator).
    ///   DOES NOT OWN: Hit overlap scan (SH_HitboxController).
    ///   DOES NOT OWN: FSM transitions (SH_PlayerStateMachine.RequestAction).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_PlayerCombatController : MonoBehaviour
    {
        #region Dependencies

        private SH_PlayerContext    _context;
        private SH_HitboxController _hitbox;
        private SH_CombatSettings   _combatSettings;
        private bool                _isInitialized;

        #endregion

        #region Serialized Fields (set via PlayerStateMachine Inspector)

        [Header("Attack Actions")]

        [Tooltip("SH_ActionData asset for the light/tap attack. " +
                 "Assign LightAttack.asset from Settings/Actions/.")]
        [SerializeField] private SH_ActionData _lightAttackAction;

        [Tooltip("SH_ActionData asset for the heavy/hold attack. " +
                 "Assign HeavyAttack.asset from Settings/Actions/.")]
        [SerializeField] private SH_ActionData _heavyAttackAction;

        #endregion

        #region Runtime State — Attack Input

        /// <summary>
        /// Time (Time.time) when the attack button was first pressed.
        /// Used to measure hold duration for light/heavy classification.
        /// </summary>
        private float _attackPressTime;

        /// <summary>
        /// True while the attack button is being held and a decision has not
        /// yet been made to commit to light vs heavy.
        /// </summary>
        private bool _attackInputPending;

        /// <summary>
        /// Attack type committed for the current action window.
        /// Set once when the action is requested, read by ActivateHitDetection.
        /// </summary>
        private AttackType _committedAttackType;

        #endregion

        #region Runtime State — Energy Surge

        /// <summary>
        /// Whether the Energy Surge (Sobrecarga de Energía) state is currently active.
        /// GDD §5.3.2: activated when the Surge bar reaches 100%.
        /// For Stage A, surge is not yet triggered automatically; this flag
        /// is available for editor testing via ContextMenu.
        /// </summary>
        public bool IsSurgeActive { get; private set; }

        /// <summary>
        /// Elapsed time of the current Surge or post-Surge cooldown.
        /// </summary>
        private float _surgeTimer;

        /// <summary>
        /// True during the post-Surge cooldown penalty period.
        /// During this period stats are reduced below base (surgeCooldownPenalty).
        /// </summary>
        public bool IsInSurgeCooldown { get; private set; }

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization called by SH_PlayerContext during orchestration.
        /// </summary>
        public void Initialize(
            SH_PlayerContext    context,
            SH_HitboxController hitbox,
            SH_CombatSettings   combatSettings)
        {
            if (context == null)
            {
                Debug.LogError($"[SH_PlayerCombatController] Initialize: context is null on {gameObject.name}.");
                return;
            }
            if (hitbox == null)
            {
                Debug.LogError($"[SH_PlayerCombatController] Initialize: hitbox is null on {gameObject.name}.");
                return;
            }
            if (combatSettings == null)
            {
                Debug.LogError($"[SH_PlayerCombatController] Initialize: combatSettings is null on {gameObject.name}.");
                return;
            }

            _context        = context;
            _hitbox         = hitbox;
            _combatSettings = combatSettings;
            _isInitialized  = true;

            // Wire the AnimatorBridge callback so OnHitImpact routes here
            // instead of the placeholder Debug.Log.
            _context.AnimatorBridge.SetHitImpactCallback(ActivateHitDetection);
        }

        #endregion

        #region Per-Frame Tick (called by FSM states via context)

        /// <summary>
        /// Per-frame combat tick called by SH_IdleState and SH_MoveState Update().
        /// Handles attack input reading, hold-duration classification, and Surge timers.
        /// </summary>
        public void Tick()
        {
            if (!_isInitialized) return;

            TickAttackInput();
            TickSurge();
        }

        #endregion

        #region Attack Input

        private void TickAttackInput()
        {
            // Button pressed this frame: start tracking hold duration
            if (_context.Input.AttackPressed)
            {
                _context.Input.ConsumeAttackPressed();
                _attackPressTime    = Time.time;
                _attackInputPending = true;
            }

            // Button released: commit to light attack if below hold threshold
            if (_attackInputPending && !_context.Input.AttackHeld)
            {
                float holdDuration = Time.time - _attackPressTime;
                AttackType type = holdDuration >= _combatSettings.heavyAttackHoldThreshold
                    ? AttackType.Heavy
                    : AttackType.Light;

                CommitAttack(type);
                _attackInputPending = false;
            }

            // Button still held beyond threshold: commit to heavy immediately
            // so the mecha responds before the player releases the button.
            if (_attackInputPending && _context.Input.AttackHeld)
            {
                float holdDuration = Time.time - _attackPressTime;
                if (holdDuration >= _combatSettings.heavyAttackHoldThreshold)
                {
                    CommitAttack(AttackType.Heavy);
                    _attackInputPending = false;
                }
            }
        }

        /// <summary>
        /// Requests the appropriate action from the state machine and records
        /// the committed attack type for later hitbox activation.
        /// </summary>
        private void CommitAttack(AttackType type)
        {
            SH_ActionData action = type == AttackType.Heavy
                ? _heavyAttackAction
                : _lightAttackAction;

            if (action == null)
            {
                Debug.LogWarning(
                    $"[SH_PlayerCombatController] {type} attack action is not assigned. " +
                    $"Assign the action asset in the Inspector.");
                return;
            }

            _committedAttackType = type;

            // RequestAction enforces cooldown and priority checks. If the request
            // is denied (e.g. currently in recovery), no state transition occurs.
            // The hitbox will only activate if the state machine accepts the request
            // and the animation event fires.
            bool accepted = _context.StateMachine.RequestAction(action);

            if (!accepted)
            {
                Debug.Log($"[SH_PlayerCombatController] {type} attack denied by state machine " +
                          $"(cooldown or priority).");
            }
        }

        #endregion

        #region Hit Impact Callback

        /// <summary>
        /// Called by SH_AnimatorBridge.OnHitImpact() via the callback registered
        /// in Initialize(). This replaces the placeholder Debug.Log.
        ///
        /// Activates the hitbox detection for this attack frame.
        /// The attack type and surge state were captured at CommitAttack() time.
        /// </summary>
        public void ActivateHitDetection()
        {
            if (!_isInitialized) return;

            SH_ActionData action = _committedAttackType == AttackType.Heavy
                ? _heavyAttackAction
                : _lightAttackAction;

            if (action == null) return;

            _hitbox.ActivateHitDetection(action, _committedAttackType, IsSurgeActive);
        }

        #endregion

        #region Energy Surge (GDD §5.3.2 — Sobrecarga de Energía)

        /// <summary>
        /// Manages the Energy Surge state duration and cooldown timers.
        /// Called every frame from SH_IdleState and SH_MoveState Update() via context.
        /// </summary>
        private void TickSurge()
        {
            if (!IsSurgeActive && !IsInSurgeCooldown) return;

            _surgeTimer += Time.deltaTime;

            if (IsSurgeActive)
            {
                if (_context.SurgeSystem != null && _context.SurgeSystem.SurgeBar <= 0f)
                    EndSurge();
            }
            else if (IsInSurgeCooldown)
            {
                if (_surgeTimer >= _combatSettings.surgeCooldownDuration)
                {
                    IsInSurgeCooldown = false;
                    _surgeTimer = 0f;
                }
            }
        }

        /// <summary>
        /// Activates the Energy Surge state.
        /// Called when the Surge bar reaches 100% (Stage B: surge bar system).
        /// For Stage A, callable from the Inspector ContextMenu for testing.
        /// </summary>
        public void ActivateSurge()
        {
            if (!_isInitialized || IsSurgeActive || IsInSurgeCooldown) return;

            IsSurgeActive = true;
            _surgeTimer   = 0f;

            Debug.Log("[SH_PlayerCombatController] Energy Surge activated.");
        }

        private void EndSurge()
        {
            IsSurgeActive     = false;
            IsInSurgeCooldown = true;
            _surgeTimer       = 0f;

            Debug.Log("[SH_PlayerCombatController] Energy Surge ended. Cooldown started.");
        }

        #endregion

        #region Editor Debug

        [ContextMenu("Debug — Activate Energy Surge")]
        private void Debug_ActivateSurge() => ActivateSurge();

        [ContextMenu("Debug — Simulate Light Attack Hit")]
        private void Debug_SimulateLightHit()
        {
            if (!Application.isPlaying) return;
            _committedAttackType = AttackType.Light;
            ActivateHitDetection();
        }

        [ContextMenu("Debug — Simulate Heavy Attack Hit")]
        private void Debug_SimulateHeavyHit()
        {
            if (!Application.isPlaying) return;
            _committedAttackType = AttackType.Heavy;
            ActivateHitDetection();
        }

        #endregion
    }
}
