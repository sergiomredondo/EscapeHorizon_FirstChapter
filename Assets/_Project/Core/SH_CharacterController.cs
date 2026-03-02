using UnityEngine;
using Data;

namespace PlayerMovement
{
    /// <summary>
    /// Physical execution engine for the Mecha unit.
    /// Implements a locomotion model based on Newton's Second Law (F = ma) featuring:
    /// - Motor force governed by maximum acceleration (aMax).
    /// - Kinetic friction (muK) for inertial energy dissipation.
    /// - Persistent velocity integration compatible with external impulses (Dash/Knockback).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class SH_CharacterController : MonoBehaviour
    {
        // Core references for physical and visual feedback
        private CharacterController _unityController;
        private Animator _animator;

        // Velocity vectors for the integration loop
        private Vector3 _currentVelocity;
        private float _verticalVelocity;

        // Optimization: Pre-hashed parameter for the Animator component
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        private void Awake()
        {
            _unityController = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
        }

        /// <summary>
        /// Primary entry point for locomotion execution.
        /// Processes the world-space directional intent provided by the FSM layer.
        /// Performs physical integration and procedural orientation.
        /// </summary>
        /// <param name="worldMoveDirection">Normalized vector representing player intent.</param>
        /// <param name="inputMagnitude">Scalar [0-1] defining motor intensity.</param>
        /// <param name="settings">Validated physical constants (SSOT).</param>
        public void Move(Vector3 worldMoveDirection, float inputMagnitude, MovementSettings settings)
        {
            // 1. Force Integration Phase
            ApplyPhysicalForces(worldMoveDirection, inputMagnitude, settings);
            ApplyGravity(settings);

            // 2. Spatial Resolution Phase:
            // Captures pre/post position to derive real-world velocity after collision constraints.
            Vector3 previousPosition = transform.position;
            _unityController.Move(_currentVelocity * Time.fixedDeltaTime);

            // 3. Feedback Calibration:
            // Calculates actual horizontal displacement for precise animation blending.
            Vector3 realDisplacement = transform.position - previousPosition;
            float realHorizontalSpeed = new Vector3(realDisplacement.x, 0f, realDisplacement.z).magnitude / Time.fixedDeltaTime;

            RotateTowards(worldMoveDirection, settings.rotationSpeed, settings.rotationThreshold);
            UpdateAnimations(realHorizontalSpeed, settings.vMax);
        }

        /// <summary>
        /// Calculates net horizontal acceleration derived from the sum of forces: Motor + Friction.
        /// Employs a semi-implicit Euler integration model stable against framerate variations.
        /// </summary>
        private void ApplyPhysicalForces(Vector3 moveDir, float inputMag, MovementSettings settings)
        {
            Vector3 horizontalVel = new Vector3(_currentVelocity.x, 0f, _currentVelocity.z);
            float mass = Mathf.Max(0.0001f, settings.mass);

            // ===== MOTOR ACCELERATION (Proportional Model) =====
            // Acceleration is proportional to velocity delta, ensuring an asymptotic approach to vMax.
            Vector3 targetVelocity = moveDir * settings.vMax * inputMag;
            Vector3 velocityDelta = targetVelocity - horizontalVel;

            Vector3 desiredAcceleration = velocityDelta * settings.aMax / Mathf.Max(settings.vMax, 0.0001f);
            Vector3 motorAcceleration = Vector3.ClampMagnitude(desiredAcceleration, settings.aMax);
            Vector3 motorForce = motorAcceleration * mass;

            // ===== KINETIC FRICTION (Dissipative Model) =====
            // Applied only when grounded to simulate mechanical drag and energy loss.
            Vector3 frictionForce = Vector3.zero;
            if (_unityController.isGrounded && horizontalVel.magnitude > settings.stopThreshold)
            {
                float normalForce = mass * Mathf.Abs(settings.gravity);
                float frictionMagnitude = settings.muK * normalForce;
                frictionForce = -horizontalVel.normalized * frictionMagnitude;
            }

            // ===== NET FORCE INTEGRATION =====
            Vector3 netForce = motorForce + frictionForce;
            Vector3 resultingAcceleration = netForce / mass;
            Vector3 deltaVelocity = resultingAcceleration * Time.fixedDeltaTime;

            // Overshoot Protection: 
            // Prevents friction from inducing negative velocity during deceleration (jitter prevention).
            if (inputMag < 0.01f && frictionForce != Vector3.zero)
            {
                if (deltaVelocity.magnitude > horizontalVel.magnitude)
                    horizontalVel = Vector3.zero;
                else
                    horizontalVel += deltaVelocity;
            }
            else
            {
                horizontalVel += deltaVelocity;
            }

            // Stability Clamp: Neutralizes micro-sliding below the established threshold.
            if (inputMag < 0.01f && horizontalVel.magnitude < settings.stopThreshold)
                horizontalVel = Vector3.zero;

            _currentVelocity.x = horizontalVel.x;
            _currentVelocity.z = horizontalVel.z;
        }

        /// <summary>
        /// Vertical integration phase. 
        /// Ensures mechanical adhesion to surfaces and free-fall acceleration.
        /// </summary>
        private void ApplyGravity(MovementSettings settings)
        {
            if (_unityController.isGrounded)
            {
                // Prevents accidental lift-off when traversing downward slopes
                if (_verticalVelocity < 0f)
                    _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity += settings.gravity * Time.fixedDeltaTime;
            }

            _currentVelocity.y = _verticalVelocity;
        }

        /// <summary>
        /// Procedurally rotates the Mecha towards the directional intent vector.
        /// Uses Spherical Linear Interpolation (Slerp) for smooth angular transitions.
        /// </summary>
        private void RotateTowards(Vector3 direction, float rotationSpeed, float rotationThreshold)
        {
            if (direction.sqrMagnitude < rotationThreshold * rotationThreshold)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Instantaneously neutralizes horizontal velocity. 
        /// Reserved for high-priority system events: Stuns, cutscenes, or respawns.
        /// </summary>
        public void HardStop()
        {
            _currentVelocity.x = 0f;
            _currentVelocity.z = 0f;
        }

        /// <summary>
        /// Injects an instantaneous velocity change (Δv). 
        /// Facilitates Dash maneuvers and combat knockback integration.
        /// </summary>
        public void AddForce(Vector3 deltaVelocity)
        {
            _currentVelocity += deltaVelocity;
        }

        /// <summary>
        /// Synchronizes the visual representation with the calculated physical displacement.
        /// </summary>
        private void UpdateAnimations(float horizontalSpeed, float vMax)
        {
            if (_animator == null)
                return;

            float speedNormalized = horizontalSpeed / Mathf.Max(0.0001f, vMax);
            _animator.SetFloat(SpeedHash, speedNormalized);
        }
    }
}