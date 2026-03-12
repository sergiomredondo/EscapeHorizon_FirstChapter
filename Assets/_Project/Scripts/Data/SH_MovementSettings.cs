using Actions.Data;
using UnityEngine;

namespace Data
{
    /// <summary>
    /// Static data container for Mecha physical parameters.
    /// Includes range validation to ensure the integrity of Newtonian calculations.
    /// </summary>
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "ScapeHorizon/Data/MovementSettings", order = 100)]
    public class SH_MovementSettings : ScriptableObject
    {
        #region Physical Constants (Mass & Gravity)

        [Header("Global Physics")]
        [Tooltip("Mass of the Mecha in kg. Must be greater than 0 to avoid division by zero errors.")]
        [Min(0.1f)]
        public float mass = 3000f;

        [Tooltip("Custom gravity multiplier (usually -9.81).")]
        public float gravity = -9.81f;

        #endregion

        #region Locomotion Parameters

        [Header("Locomotion (Ground)")]
        [Tooltip("Maximum horizontal speed (m/s).")]
        [Min(0f)]
        public float maxSpeed = 20f;

        [Tooltip("Base walking speed (m/s). Used for animation blending and as a reference for acceleration curves.")]
        [Min(0f)]
        public float walkSpeed = 5f;

        [Tooltip("Base running speed (m/s). Used for animation blending and as a reference for acceleration curves.")]
        [Min(0f)]
        public float runSpeed = 7.5f;

        [Tooltip("Additional speed added when boosting (m/s).")]
        [Min(0f)]
        public float boostSpeed = 10f;

        [Tooltip("Time to reach max speed (seconds). Minimum 0.01s to prevent infinite acceleration.")]
        [Min(0.01f)]
        public float accelerationTime = 0.2f;

        [Tooltip("Time to stop when no input is provided (seconds).")]
        [Min(0f)]
        public float decelerationTime = 0.3f;

        #endregion

        #region Friction & Thresholds

        [Header("Forces & Friction")]
        [Tooltip("Kinetic friction coefficient (muK). Controls sliding on ground.")]
        [Range(0f, 1.5f)]
        public float muK = 0.5f;

        [Tooltip("Static friction coefficient (muS). Must be greater than or equal to muK to prevent perpetual sliding.")]
        [Range(0f, 1.5f)]
        public float muS = 0.8f;

        [Tooltip("Velocity threshold to consider the Mecha stopped.")]
        [Min(0.001f)]
        public float stopThreshold = 0.25f;

        #endregion

        #region Rotation

        [Header("Steering")]
        [Tooltip("Speed at which the Mecha rotates towards movement direction.")]
        [Min(0f)]
        public float rotationSpeed = 8f;

        #endregion

        #region Actions Objects

        [Header("Actions Objects")]
        [Tooltip("Dash action data defines the impulse, timing, and hitbox for the dash maneuver. Use SH_ActionData asset.")]
        public SH_ActionData dashAction;

        #endregion

        #region Editor Validation

        /// <summary>
        /// Ensures that values modified in the inspector do not break physics logic.
        /// Applied automatically when values change in the Unity Editor.
        /// </summary>
        private void OnValidate()
        {
            if (mass < 0.1f) mass = 0.1f;
            if (accelerationTime < 0.01f) accelerationTime = 0.01f;
            if (maxSpeed < 0f) maxSpeed = 0f;
            if (stopThreshold < 0.001f) stopThreshold = 0.001f;
        }

        #endregion
    }
}