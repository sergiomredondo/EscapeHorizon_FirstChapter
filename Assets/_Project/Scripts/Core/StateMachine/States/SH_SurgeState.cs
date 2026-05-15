using Actions.Data;
using Animation;
using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Discrete activation state for the Energy Surge mechanic (GDD §5.3.2).
    ///
    /// Owns the surge ACTIVATION sequence: plays the startup animation via
    /// SH_AnimatorBridge and calls CombatController.ActivateSurge() through
    /// the OnActiveBegin phase callback. Transitions back to Idle or Move on complete.
    ///
    /// Sustained surge effects are owned by the existing architecture:
    ///   SH_EnergySurgeSystem         — bar drain per frame
    ///   SH_PlayerCombatController    — EndSurge() when bar reaches zero
    ///   SH_IdleState / SH_MoveState  — speed and friction modifiers via IsSurgeActive
    ///   SH_EnemyController           — Evade state transition via IsSurgeActive
    ///
    /// Structural contract is homologous to SH_ActionState:
    ///   + SH_ActionData drives animation config and FSM priority
    ///   + SH_AnimatorBridge.PlayActionClip() for clip dispatch
    ///   + Bridge phase callbacks for lifecycle events
    ///   + Movement NOT locked — player repositions during startup animation
    ///   + No hitbox, no staminaCost gate, no RegisterActionCooldown on complete
    /// </summary>
    public class SH_SurgeState : SH_BaseState
    {
        #region Fields

        private readonly SH_ActionData _actionData;
        private readonly SH_ActionAnimationMap _animationMap;

        private bool _surgeActivated;
        private bool _transitionRequested;
        private bool _callbacksSubscribed;

        #endregion

        #region Priority

        public override int Priority => _actionData != null ? _actionData.priority : 3;

        #endregion

        #region Constructor

        public SH_SurgeState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            SH_ActionData actionData,
            SH_ActionAnimationMap animationMap = null)
            : base(context, stateMachine)
        {
            if (actionData == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_SurgeState] actionData is null. Assign SurgeActivation.asset " +
                               "to the surgeAction field in SH_MovementSettings.");
#endif
            }
            _actionData = actionData;
            _animationMap = animationMap;
        }

        #endregion

        #region Lifecycle

        public override void Enter()
        {
            _surgeActivated = false;
            _transitionRequested = false;
            _callbacksSubscribed = false;

            _context.Locomotion.SetMovementLock(false);

            DispatchAnimation();
        }

        public override void Update()
        {
            // Combat tick — buffers attack input committed during startup animation.
            _context.CombatController?.Tick();

            _context.Interaction?.Tick();
            if (_context.Input.InteractPressed)
            {
                _context.Input.ConsumeInteractPressed();
                _context.Interaction?.NotifyInteractPressed();
            }
            if (_context.Input.InteractReleased)
            {
                _context.Input.ConsumeInteractReleased();
                _context.Interaction?.NotifyInteractReleased();
            }

            // Dash takes priority over surge activation — abort and let it run.
            if (_context.Input.DashInput)
            {
                _stateMachine.RequestAction(_context.Settings.dashAction);
                return;
            }

            SyncMovementAnimation();
        }

        public override void PhysicsUpdate(float dt)
        {
            if (dt <= 0f)
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_SurgeState] PhysicsUpdate: invalid dt ({dt}).");
#endif
                return;
            }

            // Surge is not yet active during startup — normal physics.
            // Idle/Move will pick up the surge modifiers after HandleActionComplete
            // transitions and IsSurgeActive is true.
            _context.Physics.SetFrictionMultiplier(1f);
            _context.Physics.SetSpeedMultiplier(1f);

            _context.Locomotion.Tick(dt);
            _context.Physics.Tick(_context.Settings, dt);
        }

        public override void Exit()
        {
            UnsubscribeFromBridgeCallbacks();
            _context.AnimatorBridge?.StopActionClip();

            _context.Physics.SetFrictionMultiplier(1f);
            _context.Physics.SetSpeedMultiplier(1f);

            // EndSurge is intentionally NOT called here.
            // If _surgeActivated is true, SH_PlayerCombatController.TickSurge()
            // owns the end-of-surge lifecycle and calls EndSurge() when SurgeBar hits zero.
            // If _surgeActivated is false the surge was never started — no cleanup needed.
        }

        #endregion

        #region Animation Dispatch

        private void DispatchAnimation()
        {
            if (_context.AnimatorBridge == null || _actionData == null) return;
            if (string.IsNullOrEmpty(_actionData.animationTrigger)) return;

            AnimationClip clip = _animationMap?.GetClip(_actionData);

            if (clip == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_SurgeState] No clip found for '{_actionData.name}' " +
                                 $"in SH_ActionAnimationMap. Phase callbacks will still fire.");
#endif
            }
            SubscribeToBridgeCallbacks();

            _context.AnimatorBridge.PlayActionClip(
                clip,
                _actionData.TotalDuration,
                _actionData.startupTime,
                _actionData.activeTime);
        }

        #endregion

        #region Bridge Callbacks

        private void SubscribeToBridgeCallbacks()
        {
            if (_callbacksSubscribed || _context.AnimatorBridge == null) return;

            _context.AnimatorBridge.OnActiveBegin += HandleActiveBegin;
            _context.AnimatorBridge.OnActionComplete += HandleActionComplete;

            _callbacksSubscribed = true;
        }

        private void UnsubscribeFromBridgeCallbacks()
        {
            if (!_callbacksSubscribed || _context.AnimatorBridge == null) return;

            _context.AnimatorBridge.OnActiveBegin -= HandleActiveBegin;
            _context.AnimatorBridge.OnActionComplete -= HandleActionComplete;

            _callbacksSubscribed = false;
        }

        private void HandleActiveBegin()
        {
            _context.CombatController?.ActivateSurge();
            _surgeActivated = true;

            if (_actionData.activationEffectPrefab != null)
            {
                GameObject fx = UnityEngine.Object.Instantiate(
                    _actionData.activationEffectPrefab,
                    _context.Transform.position,
                    _context.Transform.rotation);

                UnityEngine.Object.Destroy(fx, _actionData.effectAutoDestroyTime);
            }
        }

        private void HandleActionComplete()
        {
            if (_transitionRequested) return;
            _transitionRequested = true;

            bool isMoving = _context.Input.MoveInput.sqrMagnitude > 0.01f;
            _stateMachine.ChangeState(isMoving
                ? (SH_BaseState)new SH_MoveState(_context, _stateMachine)
                : new SH_IdleState(_context, _stateMachine));
        }

        #endregion

        #region Animation Sync

        private void SyncMovementAnimation()
        {
            if (_context.AnimatorBridge == null) return;

            Vector3 velocity = _context.Physics.CurrentVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float normalizedSpeed = 0f;

            if (horizontalSpeed > _context.Settings.stopThreshold)
            {
                if (horizontalSpeed <= _context.Settings.walkSpeed)
                    normalizedSpeed = (horizontalSpeed / _context.Settings.walkSpeed) * 0.5f;
                else
                {
                    float t = Mathf.InverseLerp(
                        _context.Settings.walkSpeed,
                        _context.Settings.runSpeed,
                        horizontalSpeed);
                    normalizedSpeed = 0.5f + (t * 0.5f);
                }
            }

            _context.AnimatorBridge.UpdateMovement(normalizedSpeed);
        }

        #endregion
    }
}