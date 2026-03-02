using UnityEngine;

namespace Data
{
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "ScapeHorizon/MovementSettings", order = 100)]
    public class MovementSettings : ScriptableObject
    {
        [Header("Physics")]
        [Tooltip("Physics - affects friction: F_friction = muK * m * g. Gravity applied to the character (negative value). Units: m/s^2.")]
        public float gravity = -9.81f;

        [Header("Physical Parameters")]
        [Tooltip("Physical Parameters - mass used in F = m * a and impulse Δv = J / m. Units: kg.")]
        public float mass = 3000f;

        [Tooltip("Maximum linear acceleration applied by the motors. Units: m/s^2.")]
        public float aMax = 9.81f;

        [Tooltip("Maximum horizontal speed. Units: m/s.")]
        public float vMax = 10f;

        [Tooltip("Kinetic friction coefficient (dimensionless). Used in F_friction = -muK * m * g. Typical metal-ground ~0.4. Unit: dimensionless.")]
        public float muK = 0.4f;

        [Tooltip("Static friction coefficient (reserved for future use). Unit: dimensionless.")]
        public float muS = 0.6f;

        [Header("Smoothing")]
        [Tooltip("Smoothing - time in seconds used by legacy SmoothDamp. Affects how quickly velocity approaches target. Units: s.")]
        public float velocitySmoothTime = 0.25f;

        [Header("Stability")]
        [Tooltip("Stability - thresholds used to avoid jitter. stopThreshold: below this horizontal speed (m/s) velocity is snapped to zero. Rotation is only applied when speed exceeds rotationThreshold. Units: m/s (speed) and m (distance).")]
        public float stopThreshold = 0.05f;

        [Tooltip("Minimum horizontal magnitude required to trigger body rotation. Units: m.")]
        public float rotationThreshold = 0.1f;

        [Header("Misc")]
        [Tooltip("Rotation speed used to smoothly rotate the character toward movement direction. Units: degrees per second.")]
        public float rotationSpeed = 10f;

        [Tooltip("When true uses acceleration integration (physical). When false uses legacy SmoothDamp smoothing.")]
        public bool useAccelerationIntegration = true;

        void OnValidate()
        {
            velocitySmoothTime = Mathf.Max(0f, velocitySmoothTime);
            mass = Mathf.Max(0.0001f, mass);
            vMax = Mathf.Max(0f, vMax);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            dashDuration = Mathf.Max(0.0001f, dashDuration);
            dashForce = Mathf.Max(0f, dashForce);
        }

        [Header("Dash / Boost")]
        [Tooltip("Dash / Boost - dash impulse force in Newtons. Used to compute Δv = (dashForce * dashDuration) / mass. Units: N.")]
        public float dashForce = 30000f;

        [Tooltip("Dash duration (impulse window). Units: s.")]
        public float dashDuration = 0.15f;

        [Tooltip("Dash distance (informational). Units: m.")]
        public float dashDistance = 6f;

        [Tooltip("Dash cooldown time. Units: s.")]
        public float dashCooldown = 1.5f;

        [Tooltip("Post-dash recovery time where control is limited. Units: s.")]
        public float dashRecovery = 0.3f;

        [Tooltip("Boost increases aMax by this multiplier while active. Unit: multiplier (dimensionless).")]
        public float boostAMultiplier = 1.5f;

        [Tooltip("Duration of temporary boost. Units: s.")]
        public float boostDuration = 8f;
    }
}
