using UnityEngine;

namespace Actions.Data
{
    /// <summary>
    /// Atomic and declarative definition of an executable action unit.
    /// Models the temporal, dynamic (physics), and spatial behavior of any Mecha interaction.
    /// Acts as the Data-Driven source for SH_ActionState.
    /// </summary>
    [CreateAssetMenu(fileName = "ActionData", menuName = "ScapeHorizon/Actions/ActionData", order = 200)]
    public class SH_ActionData : ScriptableObject
    {
        #region Temporal Structure (Tactical Commitment)

        [Header("Temporal Structure")]
        [Tooltip("Phase 1: Preparation time before effect activation (seconds).")]
        [Min(0f)] public float startupTime = 0.2f;

        [Tooltip("Phase 2: Time window where the effect/hitbox is functional (seconds).")]
        [Min(0f)] public float activeTime = 0.15f;

        [Tooltip("Phase 3: Post-action vulnerability time (seconds).")]
        [Min(0f)] public float recoveryTime = 0.3f;

        [Tooltip("Point in time (from T:0) where the action can be interrupted or cancelled.")]
        [Min(0f)] public float cancelWindowStart = 0.4f;

        #endregion

        #region Impulse Dynamics (Newtonian Physics)

        [Header("Newtonian Impulse")]
        [Tooltip("Force magnitude applied (Newtons). The PhysicsMotor converts this to DeltaV based on mass.")]
        [Min(0f)] public float impulseMagnitude = 20000f;

        [Tooltip("Force injection duration (seconds). 0 for instant impulse, >0 for sustained thrust.")]
        [Min(0f)] public float impulseDuration = 0.1f;

        [Tooltip("Method for determining the thrust or orientation vector.")]
        public DirectionMode directionMode = DirectionMode.Forward;

        [Tooltip("Direction vector used only if the mode is set to 'Custom'.")]
        public Vector3 customDirection = Vector3.forward;

        #endregion

        #region Spatial Interaction (Hitbox & Damage)

        [Header("Spatial Interaction")]
        [Tooltip("Detection volume radius (spherical). A value of 0 disables impact detection.")]
        [Min(0f)] public float hitboxRadius = 0f;

        [Tooltip("Relative offset from the Mecha's origin for the detection center.")]
        public Vector3 hitboxOffset = new Vector3(0, 0, 1.2f);

        [Tooltip("Collision masks used to identify valid targets.")]
        public LayerMask targetLayers;

        [Tooltip("Base damage load inflicted on initial contact.")]
        [Min(0f)] public float damage = 10f;

        [Tooltip("Magnitude of the knockback impulse applied to the target.")]
        [Min(0f)] public float staggerImpulse = 1000f;

        [Tooltip("Duration of the temporal pause (Frame Freeze) upon a successful impact.")]
        [Min(0f)] public float hitstopDuration = 0.05f;

        #endregion

        #region Systemic Meta & Priority

        [Header("Systemic Meta")]
        [Tooltip("Interruption hierarchy. Higher values can override lower priority actions.")]
        public int priority = 2; // Default above Locomotion (1)

        [Tooltip("Energy resource consumption required to initiate the action.")]
        [Min(0f)] public float staminaCost = 10f;

        [Tooltip("String identifier for the Animator trigger.")]
        public string animationTrigger = "Action_Base";

        [Tooltip("If true, movement input is ignored during the commitment phase.")]
        public bool locksMovement = true;

        [Tooltip("Determines if translation is controlled by animation (Root Motion) or the PhysicsMotor.")]
        public bool useRootMotion = false;

        #endregion

        #region Derived Properties

        /// <summary> Total duration of the action lifecycle (Startup + Active + Recovery). </summary>
        public float TotalDuration => startupTime + activeTime + recoveryTime;

        #endregion

        #region Validation Logic

        private void OnValidate()
        {
            startupTime = Mathf.Max(0f, startupTime);
            activeTime = Mathf.Max(0f, activeTime);
            recoveryTime = Mathf.Max(0f, recoveryTime);

            // Structural Integrity: Cancel window cannot occur before the effect activation
            float minCancelThreshold = startupTime + activeTime;
            cancelWindowStart = Mathf.Max(cancelWindowStart, minCancelThreshold);

            impulseMagnitude = Mathf.Max(0f, impulseMagnitude);
            impulseDuration = Mathf.Max(0f, impulseDuration);
        }

        #endregion
    }

    /// <summary> Defines orientation logic for the application of physical impulses or action directions. </summary>
    public enum DirectionMode
    {
        Forward,        // Towards the transform's current forward
        InputDirection, // Towards the player's movement input (Camera-relative)
        LockOnTarget,   // Towards the target assigned in SH_PerspectiveController
        Custom          // Uses a predefined static vector
    }
}