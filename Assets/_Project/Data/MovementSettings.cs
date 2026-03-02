using UnityEngine;

namespace Data
{
    /// <summary>
    /// ScriptableObject that centralizes all Newtonian locomotion parameters.
    /// Defines the deterministic physical profile of the Mecha unit.
    /// All values are validated to ensure numerical stability during physics integration.
    /// </summary>
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "ScapeHorizon/MovementSettings", order = 100)]
    public class MovementSettings : ScriptableObject
    {
        #region Global Physics

        [Header("Global Physics")]

        [Tooltip("Gravity acceleration (m/s^2). Standard Earth gravity is -9.81.")]
        public float gravity = -9.81f;

        [Tooltip("Mecha mass (kg). Critical for force-to-velocity (F=ma) calculations.")]
        [Min(0.001f)]
        public float mass = 3000f;

        [Tooltip("Downward snap velocity applied when grounded to maintain slope adhesion.")]
        [Min(0f)]
        public float groundSnapVelocity = 2f;

        #endregion

        #region Locomotion (Acceleration Model)

        [Header("Locomotion")]

        [Tooltip("Maximum horizontal terminal velocity (m/s) allowed by the locomotion system.")]
        [Min(0f)]
        public float vMax = 10f;

        [Tooltip("Maximum engine-driven acceleration (m/s^2). Independent of mass due to electronic stabilization.")]
        [Min(0f)]
        public float aMax = 9.81f;

        [Tooltip("Kinetic friction coefficient (mu_k). Active during grounded states to dissipate energy.")]
        [Range(0.0f, 2.0f)]
        public float muK = 0.4f;

        [Tooltip("Speed threshold (m/s) below which all kinetic energy is neutralized to prevent jitter.")]
        [Min(0f)]
        public float stopThreshold = 0.05f;

        #endregion

        #region Rotation & Orientation

        [Header("Rotation & Orientation")]

        [Tooltip("Angular speed (deg/s) for procedural body alignment with movement vector.")]
        [Min(0f)]
        public float rotationSpeed = 10f;

        [Tooltip("Minimum input magnitude required to initiate orientation changes.")]
        [Range(0f, 1f)]
        public float rotationThreshold = 0.1f;

        #endregion

        #region Dash System (Impulse Model)

        [Header("Dash System")]

        [Tooltip("Instantaneous force magnitude (N) injected into the system during a dash.")]
        [Min(0f)]
        public float dashForce = 30000f;

        [Tooltip("Temporal window (s) of the kinetic impulse application.")]
        [Min(0.01f)]
        public float dashDuration = 0.15f;

        [Tooltip("Mandatory mechanical cooldown (s) before the system can re-inject a dash impulse.")]
        [Min(0f)]
        public float dashCooldown = 1.5f;

        #endregion

        #region Boost System

        [Header("Boost System")]

        [Tooltip("Locomotion acceleration multiplier applied while the overcharge system is active.")]
        [Min(1f)]
        public float boostMultiplier = 1.5f;

        #endregion

        #region Validation Logic

        /// <summary>
        /// Validates parameters to maintain physical consistency and prevent runtime exceptions.
        /// Executed automatically by the Unity Editor on value changes.
        /// </summary>
        private void OnValidate()
        {
            // Ensure mass never reaches zero to prevent infinite acceleration (a = F/0)
            mass = Mathf.Max(0.001f, mass);

            vMax = Mathf.Max(0f, vMax);
            aMax = Mathf.Max(0f, aMax);

            // Dash validation for stable impulse calculations
            dashForce = Mathf.Max(0f, dashForce);
            dashDuration = Mathf.Max(0.01f, dashDuration);
            dashCooldown = Mathf.Max(0f, dashCooldown);

            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            stopThreshold = Mathf.Max(0f, stopThreshold);

            boostMultiplier = Mathf.Max(1f, boostMultiplier);

            // Clamp friction within realistic, numerically stable bounds
            muK = Mathf.Clamp(muK, 0f, 2f);

            groundSnapVelocity = Mathf.Max(0f, groundSnapVelocity);
        }

        #endregion
    }
}