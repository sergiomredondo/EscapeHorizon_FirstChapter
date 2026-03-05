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

        [Header("References")]
        [Tooltip("Input handler is responsible for processing raw input and providing normalized movement vectors and action states. Use SH_InputHandler component.")]
        [SerializeField] private SH_InputHandler _inputHandler;

        [Tooltip("Movement settings is responsible for defining mass, speed limits, acceleration times, and friction coefficients. Use SH_MovementSettings asset.")]
        [SerializeField] private SH_MovementSettings _settings;

        [Tooltip("Perspective controller is responsible for converting input vectors into world space directions based on camera orientation. Use SH_PerspectiveController component.")]
        [SerializeField] private SH_PerspectiveController _perspectiveController;

        private SH_PhysicsMotor _physicsMotor;

        /// <summary> Deterministic lock to suspend locomotion during high-commitment actions. </summary>
        private bool _movementLocked;

        #endregion
        #region Initialization

        /// Explicit initialization method called by the State Machine to inject dependencies.
        private void Awake()
        {
            _physicsMotor = GetComponent<SH_PhysicsMotor>();

            if (_inputHandler == null) Debug.LogError($"[SH_LocomotionController] Falta referencia a SH_InputHandler en {gameObject.name}");
            if (_settings == null) Debug.LogError($"[SH_LocomotionController] Falta referencia a SH_MovementSettings en {gameObject.name}");
            if (_perspectiveController == null) Debug.LogError($"[SH_LocomotionController] Falta referencia a SH_PerspectiveController en {gameObject.name}");
        }

        /// <summary>
        /// Links the controller with external dependencies, decoupling it from a specific context instance.
        /// </summary>
        /// <param name="input">Input provider for movement intentions.</param>
        /// <param name="data">Data asset containing mass and acceleration parameters.</param>
        public void Initialize(SH_InputHandler input, SH_MovementSettings data)
        {
            _physicsMotor = GetComponent<SH_PhysicsMotor>();
            _inputHandler = input;
            _settings = data;
        }

        /// <summary> Allows the FSM to lock locomotion to prioritize other movement modes (Dashes/Attacks). </summary>
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
            if (_movementLocked || _inputHandler == null || _settings == null)
                return;

            // 1. Process Input and determine intended movement direction in world space
            Vector2 input = _inputHandler.MoveInput;
            Vector3 direction = _perspectiveController.GetWorldSpaceDirection(input);

            // 2. If no significant input, apply deceleration to come to a smooth stop
            if (input.sqrMagnitude < 0.01f)
            {
                ApplyDeceleration(direction, dt);
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
            // Extract horizontal velocity (ignoring vertical component for grounded movement)
            Vector3 currentVelocity = _physicsMotor.CurrentVelocity;
            Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

            // Determine target speed based on input (boost vs walk)
            float targetSpeed = _inputHandler.BoostInput ? _settings.boostSpeed : _settings.walkSpeed;
            Vector3 moveInput = new Vector3(_inputHandler.MoveInput.x, 0f, _inputHandler.MoveInput.y);

            // Normalize input to prevent faster diagonal movement
            if (moveInput.sqrMagnitude > 1f)
                moveInput.Normalize();

            // Scale input by target speed to get desired velocity vector
            Vector3 targetVelocity = moveInput * targetSpeed;

            // Calculate maximum acceleration based on settings to ensure smooth transitions
            float maxAcceleration = targetSpeed / Mathf.Max(0.01f, _settings.accelerationTime);

            // Compute the velocity delta needed to reach the target velocity
            Vector3 velocityDelta = targetVelocity - horizontalVelocity;

            // Clamp the acceleration to prevent overshooting and ensure smooth movement
            Vector3 accelerationForce = Vector3.ClampMagnitude(velocityDelta, maxAcceleration * dt);

            // Apply the calculated acceleration to the PhysicsMotor, which will handle the actual velocity change
            _physicsMotor.AddHorizontalVelocity(accelerationForce);
        }

        private void ApplyDeceleration(Vector3 direction, float dt)
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

        /// <summary>
        /// Aligns the Mecha's transform with the current movement direction using smooth interpolation.
        /// </summary>
        private void ApplyRotation(Vector3 direction, float dt)
        {
            // Only rotate if there is significant movement input to avoid jitter when idle
            if (_inputHandler.MoveInput.sqrMagnitude > 0.01f)
            {
                Vector3 targetDirection = new Vector3(_inputHandler.MoveInput.x, 0f, _inputHandler.MoveInput.y);
                Quaternion targetRotation = Quaternion.LookRotation(targetDirection);

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