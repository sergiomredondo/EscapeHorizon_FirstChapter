using UnityEngine;

namespace Animation
{
    /// <summary>
    /// Bridge component to connect the Animator with the player context and state machine.
    /// Encapsulates all animation-related logic, including parameter updates and event callbacks.
    /// Designed to be called from the SH_PlayerStateMachine and its states to maintain separation of concerns.
    /// </summary>
    public class SH_AnimatorBridge : MonoBehaviour
    {
        #region Dependencies
        /// <summary> Bridge to the visual layer for feedback and skeletal state management. </summary>
        private Animator _animator;

        // Reference to the Animator component on the Mecha model. This should be assigned in the inspector or linked during initialization.
        private int _movementSpeedHash;

        // Precomputed hash for the "Movement_Blend" float parameter in the Animator. This allows for efficient updates to the movement speed parameter without string lookups.
        private int _dashForceHash;
        
        // Precomputed hash for the "Dash" trigger parameter in the Animator. This allows for efficient triggering of the dash animation without string lookups.
        private int _dashTriggerHash;

        // Precomputed hash for the "Attack" trigger parameter in the Animator. This allows for efficient triggering of the attack animation without string lookups.
        private int _attackTriggerHash;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the Animator Bridge with necessary references. This method should be called from the SH_PlayerStateMachine during its initialization phase, after all dependencies have been assigned.
        /// </summary>
        /// <param name="animator">A reference to the Animator component to control. This should be the Animator on the Mecha model, not the one on the StateMachine.</param>
        public void Initialize(Animator animator)
        {
            if (animator == null) { Debug.LogError($"[SH_AnimatorBridge] Initialization failed: Animator reference is null. Ensure that a valid Animator component is passed when calling Initialize."); return; }
            _animator = animator;

            // Precompute parameter hashes for performance optimization. This allows us to use integer hashes instead of string lookups when setting parameters, which is more efficient at runtime.
            _movementSpeedHash = Animator.StringToHash("Movement_Blend");
            _dashForceHash = Animator.StringToHash("DashForce");
            _dashTriggerHash = Animator.StringToHash("Dash");
            _attackTriggerHash = Animator.StringToHash("Attack");
        }

        #endregion

        /// <summary>
        /// Updates the movement speed parameter in the Animator to drive the blend tree. 
        /// This method should be called from the SH_PlayerStateMachine or its states whenever the movement speed changes, such as during locomotion updates. 
        /// The speed value should ideally be derived from the actual velocity of the Mecha to ensure synchronization between physical movement and animation, minimizing foot-sliding and enhancing visual feedback.
        /// </summary>
        /// <param name="normalizedSpeed"></param>
        public void UpdateMovement(float normalizedSpeed)
        {
            _animator.SetFloat(_movementSpeedHash, normalizedSpeed);
        }

        /// <summary>
        /// Triggers the dash animation by setting the corresponding trigger parameter in the Animator.
        /// This method should be called from the SH_PlayerStateMachine or its states when a dash action is initiated.
        /// The Animator should have a trigger parameter named "Dash" that transitions to the appropriate dash animation state when activated.
        /// </summary>
        public void TriggerDash(float normalizedSpeed)
        {
            // Sets the "Dash" trigger in the Animator to initiate the dash animation. Ensure that the Animator Controller has a trigger parameter named "Dash" and that it transitions to the correct animation state when this trigger is set. Additionally, we set the "DashForce" float parameter to allow for dynamic adjustment of the dash animation
            // based on the speed at which the dash is executed, providing more responsive and visually varied feedback.
            //_animator.SetTrigger(_dashTriggerHash);
            if (normalizedSpeed < 0) { Debug.Log($"[SH_AnimatorBridge] normalizedSpeed value ({normalizedSpeed})."); return; }
            if (_animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.60f && normalizedSpeed > 0f)
            {
                _animator.SetFloat(_dashForceHash, _animator.GetCurrentAnimatorStateInfo(0).normalizedTime + 0.61f);
            }
            else
            {
                _animator.SetFloat(_dashForceHash, normalizedSpeed);
            }
        }

        /// <summary>
        /// Triggers the attack animation by setting the corresponding trigger parameter in the Animator.
        /// This method should be called from the SH_PlayerStateMachine or its states when an attack action is initiated.
        /// The Animator should have a trigger parameter named "Attack" that transitions to the appropriate attack animation state when activated. Additionally, the attack animation should have an event at the frame where the hit should register, which calls the OnHitImpact() method in this bridge to connect with the combat system we will create in Stage 4.
        /// </summary>
        public void TriggerAttack()
        {
            // Sets the "Attack" trigger in the Animator to initiate the attack animation. Ensure that the Animator Controller has a trigger parameter named "Attack" and that it transitions to the correct animation state when this trigger is set. Additionally, make sure to add an animation event in the attack animation clip that calls the OnHitImpact() method at the appropriate frame to connect with the combat system.
            _animator.SetTrigger(_attackTriggerHash);
        }

        /// <summary>
        /// This method is intended to be called from an animation event within the attack animation clip at the frame where the hit should register. 
        /// It serves as a callback to connect the visual impact of the attack with the underlying combat logic that we will implement in Stage 4. 
        /// When this method is called, it should trigger the necessary logic to apply damage, play hit effects, and any other combat-related feedback that corresponds to the attack landing successfully.
        /// 
        /// </summary>
        public void OnHitImpact()
        {
            // This method is a placeholder for the logic that will be executed when the attack animation reaches the frame where the hit should register. In Stage 4, we will implement the combat system, and this method will be connected to trigger damage application, hit effects, and other combat feedback. For now, it simply logs a message to indicate that the impact has been detected.
            Debug.Log("[SH_AnimatorBridge] OnHitImpact called: This should trigger combat logic to apply damage and effects. Implement the combat system in Stage 4 to connect this callback with the actual damage application and feedback.");
        }
    }
}