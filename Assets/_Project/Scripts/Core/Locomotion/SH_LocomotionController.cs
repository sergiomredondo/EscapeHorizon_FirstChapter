using UnityEngine;
using Core.Physics;
using Core.Input;
using Core.Camera;
using Data;

namespace Core.Locomotion
{
    /// <summary>
    /// Component responsible for translating input intentions into locomotive forces.
    /// It coordinates with the PhysicsMotor to move and rotate the Mecha based on Data Assets.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_LocomotionController : MonoBehaviour
    {
        #region Dependencies
        /// <summary> InpuHandler provides normalized movement vectors and action states. </summary>
        private SH_InputHandler _input;

        /// <summary> MovementSettings defines mass, speed limits, acceleration times, and friction coefficients. </summary>
        private SH_MovementSettings _settings;

        /// <summary> PhysicsMotor is responsible for velocity integration, gravity, and friction. </summary>
        private SH_PhysicsMotor _physicsMotor;

        /// <summary> PerspectiveController provides world-space directions relative to camera or lock-on targets. </summary>
        private SH_PerspectiveController _perspective;

        /// <summary> Deterministic lock to suspend locomotion during high-commitment actions. </summary>
        private bool _movementLocked;

        #endregion
        #region Initialization

        /// <summary>
        /// Links the controller with external dependencies, decoupling it from a specific context instance.
        /// </summary>
        /// <param name="input">Input provider for movement intentions.</param>
        /// <param name="data">Data asset containing mass and acceleration parameters.</param>
        /// <param name="physic">Reference to the PhysicsMotor for applying forces.</param>
        public void Initialize(SH_InputHandler input, SH_MovementSettings settings, SH_PhysicsMotor physics, SH_PerspectiveController perspective)
        {
            if (input == null) { Debug.LogError($"[SH_LocomotionController] Initialization failed: InputHandler reference is null. Ensure that a valid SH_InputHandler component is assigned during initialization."); return; }
            if (settings == null) { Debug.LogError($"[SH_LocomotionController] Initialization failed: MovementSettings reference is null. Ensure that a valid SH_MovementSettings asset is assigned during initialization."); return; }
            if (physics == null) { Debug.LogError($"[SH_LocomotionController] Initialization failed: PhysicsMotor reference is null. Ensure that a valid SH_PhysicsMotor component is assigned during initialization."); return; }
            if (perspective == null) { Debug.LogError($"[SH_LocomotionController] Initialization failed: PerspectiveController reference is null. Ensure that a valid SH_PerspectiveController component is assigned during initialization."); return; }

            _input = input;
            _settings = settings;
            _physicsMotor = physics;
            _perspective = perspective;
        }

        /// <summary> Allows the FSM to lock locomotion to prioritize other movement modes. </summary>
        public void SetMovementLock(bool locked) => _movementLocked = locked;

        #endregion

        #region Update Logic

        /// <summary>
        /// Called explicitly by the FSM.
        /// DeltaTime is injected to preserve determinism.
        /// </summary>
        /// <param name="dt">Delta time for kinematic calculations.</param>
        public void Tick(float dt)
        {
            if (dt <= 0) { Debug.LogError($"[SH_LocomotionController] Tick failed: Invalid delta time value ({dt}). Ensure that a positive, non-zero value is passed when calling Tick."); return; }

            // 1. Process Input and determine intended movement direction in world space
            Vector2 input = _input.MoveInput;
            Vector3 direction = _perspective.GetWorldSpaceDirection(input);
            
            // 2. If no significant input, apply deceleration to come to a smooth stop
            if (input.sqrMagnitude < 0.01f)
            {
                //ApplyDeceleration(dt); // Optional: Uncomment to enable smooth stopping when input ceases
                return;
            }

            // 3. Apply acceleration towards the target velocity based on input and settings
            ApplyAcceleration(direction, dt);
            ApplyRotation(direction, dt);
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Calculates linear movement velocity by interpolating current state towards input target.
        /// </summary>
        private void ApplyAcceleration(Vector3 direction, float dt)
        {
            if (direction == null) { Debug.LogError($"[SH_LocomotionController] ApplyAcceleration failed: Direction vector is null. Ensure that the input processing correctly maps to a valid world-space direction."); return; }
            if (dt <= 0) { Debug.LogError($"[SH_LocomotionController] ApplyAcceleration failed: Invalid delta time value ({dt}). Ensure that a positive, non-zero value is passed when calling ApplyAcceleration."); return; }

            // Extract horizontal velocity (ignoring vertical component for grounded movement)
            Vector3 currentVelocity = _physicsMotor.CurrentVelocity;
            Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

            // Determine target speed based on input (boost vs run) to allow for dynamic speed changes without needing separate states or complex logic.
            float targetSpeed = _input.BoostInput ? _settings.boostSpeed : _settings.runSpeed;

            // Calculate the desired target velocity vector based on input direction and target speed.
            Vector3 targetVelocity = direction * targetSpeed;

            // Compute the velocity delta needed to reach the target velocity
            Vector3 velocityDelta = targetVelocity - horizontalVelocity;

            // Calculate maximum acceleration based on the time to reach max speed, ensuring we don't exceed physical limits.
            float accelTime = Mathf.Max(0.01f, _settings.accelerationTime);

            // Compute the required acceleration to reach the target velocity within the specified time frame.
            Vector3 requiredAcceleration = velocityDelta / accelTime;

            // Convert the required acceleration into a force using Newton's second law (F = m * a), where mass is defined in the settings.
            Vector3 force = requiredAcceleration * _settings.mass;

            // Apply the calculated acceleration to the PhysicsMotor, which will handle the actual velocity change
            _physicsMotor.ApplyForce(_settings, force, dt);
        }

        /* Optional Deceleration Logic: Uncomment to enable smooth stopping when input ceases. This will apply a counter-force to bring the Mecha to a stop rather than allowing it to coast indefinitely.
        private void ApplyDeceleration(float dt)
        {
            // Extract horizontal velocity only (grounded deceleration)
            Vector3 currentVelocity = _physicsMotor.CurrentVelocity;
            Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

            // If already nearly stopped, avoid micro-adjustments
            if (horizontalVelocity.sqrMagnitude < 0.0001f)
                return;

            // Target velocity when no input is zero
            Vector3 targetVelocity = Vector3.zero;

            // Compute maximum deceleration based on desired stop time
            float maxDeceleration = horizontalVelocity.magnitude / Mathf.Max(0.01f, _settings.decelerationTime);

            // Compute required velocity delta to stop
            Vector3 velocityDelta = targetVelocity - horizontalVelocity;

            // Clamp the deceleration to avoid overshooting
            Vector3 decelerationForce = Vector3.ClampMagnitude(
                velocityDelta,
                maxDeceleration * dt
            );

            // Apply deceleration through physics motor
            _physicsMotor.AddHorizontalVelocity(decelerationForce);
        }
        */

        /// <summary>
        /// Aligns the Mecha's transform with the current movement direction using smooth interpolation.
        /// </summary>
        private void ApplyRotation(Vector3 direction, float dt)
        {
            if (direction == null) { Debug.LogError($"[SH_LocomotionController] ApplyRotation failed: Direction vector is null. Ensure that the input processing correctly maps to a valid world-space direction."); return; }
            if (dt <= 0) { Debug.LogError($"[SH_LocomotionController] ApplyRotation failed: Invalid delta time value ({dt}). Ensure that a positive, non-zero value is passed when calling ApplyRotation."); return; }

            // Only rotate if there is significant movement input to avoid jitter when idle
            if (direction.sqrMagnitude < 0.0001f)
                return;
            {
                // Calculate the target rotation based on the movement direction. We use LookRotation to create a quaternion that faces the direction of movement.
                Quaternion targetRotation = Quaternion.LookRotation(direction);

                // Smoothly interpolate the current rotation towards the target rotation based on settings
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    _settings.rotationSpeed * dt
                );
            }
        }

        #endregion
    }
}