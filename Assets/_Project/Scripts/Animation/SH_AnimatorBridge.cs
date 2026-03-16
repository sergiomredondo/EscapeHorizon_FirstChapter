using System;
using UnityEngine;

namespace Animation
{
    /// <summary>
    /// Bridge between the Animator Controller and the player's sub-systems.
    /// Encapsulates all animation parameter writes and serves as the
    /// routing point for animation events.
    ///
    /// Change log vs. previous version:
    ///   + SetHitImpactCallback(Action) — allows SH_PlayerCombatController to
    ///     register itself as the receiver of OnHitImpact animation events.
    ///     Replaces the placeholder Debug.Log with a real dispatch to the combat system.
    ///   + OnHitImpact() — now invokes _hitImpactCallback if registered; falls back
    ///     to a warning if no combat controller has connected yet.
    /// </summary>
    public class SH_AnimatorBridge : MonoBehaviour
    {
        #region Dependencies

        private Animator _animator;

        // Precomputed parameter hashes
        private int _movementSpeedHash;
        private int _dashForceHash;
        private int _dashTriggerHash;
        private int _attackTriggerHash;

        /// <summary>
        /// Callback registered by SH_PlayerCombatController.
        /// Invoked by OnHitImpact() when an animation event fires.
        /// </summary>
        private Action _hitImpactCallback;

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during orchestration.
        /// Caches the Animator reference and precomputes parameter hashes.
        /// </summary>
        public void Initialize(Animator animator)
        {
            if (animator == null)
            {
                Debug.LogError("[SH_AnimatorBridge] Initialize: Animator reference is null.");
                return;
            }

            _animator = animator;
            _movementSpeedHash = Animator.StringToHash("Movement_Blend");
            _dashForceHash = Animator.StringToHash("DashForce");
            _dashTriggerHash = Animator.StringToHash("Dash");
            _attackTriggerHash = Animator.StringToHash("Attack");
        }

        /// <summary>
        /// Registers the callback that will be invoked when OnHitImpact fires.
        /// Called by SH_PlayerCombatController.Initialize() to wire the combat system
        /// to the animation event without creating a direct dependency between them.
        ///
        /// Replaces the placeholder Debug.Log that previously existed in OnHitImpact.
        /// </summary>
        /// <param name="callback">
        /// The method to call when the hit-impact animation event fires.
        /// Typically SH_PlayerCombatController.ActivateHitDetection.
        /// </param>
        public void SetHitImpactCallback(Action callback)
        {
            _hitImpactCallback = callback;
        }

        #endregion

        #region Animation Parameter API

        /// <summary>
        /// Updates the Movement_Blend float parameter to drive the locomotion blend tree.
        /// Called every frame by SH_IdleState and SH_MoveState.
        /// </summary>
        public void UpdateMovement(float normalizedSpeed)
        {
            _animator.SetFloat(_movementSpeedHash, normalizedSpeed);
        }

        /// <summary>
        /// Updates DashForce for the dash animation blend.
        /// Called by SH_ActionState during the active dash phase.
        /// </summary>
        public void TriggerDash(float normalizedSpeed)
        {
            if (normalizedSpeed < 0f)
            {
                Debug.Log($"[SH_AnimatorBridge] TriggerDash: negative normalizedSpeed ({normalizedSpeed}).");
                return;
            }

            if (_animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.60f && normalizedSpeed > 0f)
                _animator.SetFloat(_dashForceHash,
                    _animator.GetCurrentAnimatorStateInfo(0).normalizedTime + 0.61f);
            else
                _animator.SetFloat(_dashForceHash, normalizedSpeed);
        }

        /// <summary>
        /// Sets the Attack trigger on the Animator to initiate the attack animation.
        /// The animation clip must have an Animation Event at the impact frame
        /// that calls OnHitImpact() on this component.
        /// </summary>
        public void TriggerAttack()
        {
            _animator.SetTrigger(_attackTriggerHash);
        }

        #endregion

        #region Animation Event Callbacks

        /// <summary>
        /// Called by the Animation Event placed at the hit-impact frame in the
        /// attack animation clip. Routes to SH_PlayerCombatController via the
        /// registered callback.
        ///
        /// Setup required in Unity:
        ///   1. Open the attack animation clip in the Animation window.
        ///   2. Add an Animation Event at the frame where the hit should register.
        ///   3. Set the function to "OnHitImpact" on the SH_AnimatorBridge component.
        ///   No parameters needed — the combat controller reads its own state.
        /// </summary>
        public void OnHitImpact()
        {
            if (_hitImpactCallback != null)
            {
                _hitImpactCallback.Invoke();
            }
            else
            {
                Debug.LogWarning(
                    "[SH_AnimatorBridge] OnHitImpact fired but no callback is registered. " +
                    "Ensure SH_PlayerCombatController.Initialize() has been called and " +
                    "SetHitImpactCallback() was called with a valid delegate.");
            }
        }

        #endregion
    }
}
