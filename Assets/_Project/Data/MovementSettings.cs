using UnityEngine;

namespace Data
{
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "ScapeHorizon/MovementSettings", order = 100)]
    public class MovementSettings : ScriptableObject
    {
        [Header("Physics")]
        [Tooltip("Gravity applied to the character (negative value).")]
        public float gravity = -9.81f;

        [Header("Physical Parameters")]
        [Tooltip("Mass of the mech in kilograms.")]
        public float mass = 3000f;

        [Tooltip("Maximum linear acceleration (m/s^2) applied by the motors.")]
        public float aMax = 9.81f;

        [Tooltip("Maximum horizontal speed (m/s).")]
        public float vMax = 10f;

        [Tooltip("Kinetic friction coefficient (used when no input is applied). Typical metal-ground ~0.4.")]
        public float muK = 0.4f;

        [Tooltip("Static friction coefficient (reserved for future use).")]
        public float muS = 0.6f;

        [Header("Smoothing")]
        [Tooltip("Time in seconds to smooth horizontal velocity towards target when using legacy smoothing.")]
        public float velocitySmoothTime = 0.25f;

        [Header("Dash / Boost")]
        [Tooltip("Dash impulse force in Newtons. Used to compute Δv = (dashForce * dashDuration) / mass.")]
        public float dashForce = 30000f;

        [Tooltip("Dash duration in seconds (impulse window).")]
        public float dashDuration = 0.15f;

        [Tooltip("Dash distance in meters (informational).")]
        public float dashDistance = 6f;

        [Tooltip("Dash cooldown in seconds.")]
        public float dashCooldown = 1.5f;

        [Tooltip("Post-dash recovery time in seconds where control is limited.")]
        public float dashRecovery = 0.3f;

        [Tooltip("Boost increases aMax by this multiplier while active.")]
        public float boostAMultiplier = 1.5f;

        [Tooltip("Duration of temporary boost in seconds.")]
        public float boostDuration = 8f;
    }
}
