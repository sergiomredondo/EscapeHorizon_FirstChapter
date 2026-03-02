using System;
using UnityEngine;
using UnityEngine.InputSystem;

// This script provides a character controller using Unity's CharacterController component.
namespace PlayerMovement
{
    // Ensure the GameObject has a CharacterController component.
    [RequireComponent(typeof(CharacterController))]
    public class SH_CharacterController : MonoBehaviour
    {
        [Header("Movement settings")]
        [SerializeField, HideInInspector]
        private float rotationSpeed = 10f;
        [Header("Physics")]
        // Physics: gravity affects acceleration and friction computations (F_friction = muK * m * g).
        [SerializeField, HideInInspector]
        private float gravity = -9.81f;

        [Header("Physical Parameters")]
        // Physical Parameters: mass used in F = m * a and impulse calculations Δv = J / m.
        [SerializeField, HideInInspector]
        private float mass = 3000f;

        [SerializeField, HideInInspector]
        private float aMax = 9.81f;

        [SerializeField, HideInInspector]
        private float vMax = 10f;

        [SerializeField, HideInInspector]
        private float muK = 0.4f;

        [SerializeField, HideInInspector]
        private float muS = 0.6f;

        [Header("Camera Relative")]
        [Tooltip("When true, movement input is interpreted relative to the camera orientation.")]
        public bool useCameraRelative = true;

        [Tooltip("Optional reference to camera transform to use for camera-relative movement. If null, Camera.main is used.")]
        public Transform cameraTransform;

        [Header("Smoothing")]
        // Smoothing: affects how quickly velocity converges to target when using legacy smoothing (SmoothDamp).
        [SerializeField, HideInInspector]
        private float velocitySmoothTime = 0.25f;

        [SerializeField, HideInInspector]
        private bool useAccelerationIntegration = true;

        // Accessors prefer MovementSettings when present
        private float RotationSpeedValue => movementSettings != null ? movementSettings.rotationSpeed : rotationSpeed;
        private bool UseAccelerationIntegrationValue => movementSettings != null ? movementSettings.useAccelerationIntegration : useAccelerationIntegration;

        [Header("Stability")]
        [SerializeField, HideInInspector]
        private float stopThreshold = 0.05f;

        [SerializeField, HideInInspector]
        private float rotationThreshold = 0.1f;

        // Stability values prefer MovementSettings if present.
        private float StopThresholdValue => movementSettings != null ? movementSettings.stopThreshold : stopThreshold;
        private float RotationThresholdValue => movementSettings != null ? movementSettings.rotationThreshold : rotationThreshold;

        [Header("Tuning Asset")]
        [Tooltip("Optional MovementSettings asset. If assigned, its values override the fields below.")]
        public Data.MovementSettings movementSettings;

        // SmoothDamp velocity reference for horizontal smoothing
        private Vector3 _velocitySmoothRef;

        private CharacterController _controller;
        private Animator _animator;
        private Vector2 _moveInput;
        private Vector3 _currentMoveDirection;
        // Reference to centralized input handler (should be provided by SH_InputHandler)
        public Core.SH_InputHandler inputHandler;

        // Vertical velocity used to apply gravity to the CharacterController.
        private float _verticalVelocity;

        // Horizontal velocity (world-space XZ).
        private Vector3 _velocity;

        // Animator parameter hash for the 'Speed' float parameter.
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        // Property accessors that prefer MovementSettings if provided.
        private float GravityValue => movementSettings != null ? movementSettings.gravity : gravity;
        private float MassValue => movementSettings != null ? movementSettings.mass : mass;
        private float AMaxValue => movementSettings != null ? movementSettings.aMax : aMax;
        private float VMaxValue => movementSettings != null ? movementSettings.vMax : vMax;
        private float MuKValue => movementSettings != null ? movementSettings.muK : muK;
        private float MuSValue => movementSettings != null ? movementSettings.muS : muS;
        private float VelocitySmoothTimeValue => movementSettings != null ? movementSettings.velocitySmoothTime : velocitySmoothTime;
        private float DashForceValue => movementSettings != null ? movementSettings.dashForce : dashForce;
        private float DashDurationValue => movementSettings != null ? movementSettings.dashDuration : dashDuration;
        private float DashDistanceValue => movementSettings != null ? movementSettings.dashDistance : dashDistance;
        private float DashCooldownValue => movementSettings != null ? movementSettings.dashCooldown : dashCooldown;
        private float DashRecoveryValue => movementSettings != null ? movementSettings.dashRecovery : dashRecovery;
        private float BoostAMultiplierValue => movementSettings != null ? movementSettings.boostAMultiplier : boostAMultiplier;
        private float BoostDurationValue => movementSettings != null ? movementSettings.boostDuration : boostDuration;

        // Current computed horizontal speed (for inspector or UI binding).
        [SerializeField]
        private float currentSpeed;

        [Header("Dash / Boost")]
        // Dash/Boost: dash impulse J = dashForce * dashDuration, Δv = J / mass. boost modifies aMax and muK.
        [SerializeField, HideInInspector]
        private float dashForce = 30000f;

        [SerializeField, HideInInspector]
        private float dashDuration = 0.15f;

        [SerializeField, HideInInspector]
        private float dashDistance = 6f;

        [SerializeField, HideInInspector]
        private float dashCooldown = 1.5f;

        [SerializeField, HideInInspector]
        private float dashRecovery = 0.3f;

        // internal dash state
        private bool _isDashing;
        private float _dashTimer;
        private float _dashCooldownTimer;
        private float _dashRecoveryTimer;

        [SerializeField, HideInInspector]
        private float boostAMultiplier = 1.5f;

        [SerializeField, HideInInspector]
        private float boostDuration = 8f;

        private bool _isBoosted;
        private float _boostTimer;

        [Header("Debug")]
        [Tooltip("When enabled, logs position, move direction and speed each frame. Turn off for release builds.")]
        public bool debugSpeed = false;

        // Event invoked with the current speed value each update.
        public Action<float> OnSpeedLogged;

        // -----------------
        // Unity lifecycle
        // -----------------
        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();

            if (_controller == null)
                Debug.LogWarning("SH_CharacterController requires a CharacterController component.");
            if (_animator == null)
                Debug.LogWarning("SH_CharacterController: Animator not found. Animations will be disabled.");

            if (movementSettings == null)
                Debug.LogWarning("SH_CharacterController: MovementSettings asset not assigned. Using component fallbacks. Assign a MovementSettings asset to centralize tuning.");
        }

        // Subscribe to input events on enable. We look for a SH_InputHandler in the scene to subscribe to high-level input events.
        void OnEnable()
        {
            if (inputHandler == null)
                inputHandler = UnityEngine.Object.FindFirstObjectByType<Core.SH_InputHandler>();
            if (inputHandler != null)
            {
                inputHandler.OnMove += HandleMove;
                inputHandler.OnDash += HandleDashInput;
                inputHandler.OnBoost += HandleBoostInput;
            }
        }

        // Try to find a camera transform for camera-relative movement if not explicitly assigned.
        void Start()
        {
            if (inputHandler == null)
                inputHandler = UnityEngine.Object.FindFirstObjectByType<Core.SH_InputHandler>();

            // If no explicit cameraTransform, try to get it from the PerspectiveController
            if (cameraTransform == null)
            {
                var p = UnityEngine.Object.FindFirstObjectByType<Systems.SH_PerspectiveController>();
                if (p != null && p.ActiveCameraTransform != null)
                    cameraTransform = p.ActiveCameraTransform;
            }
        }

        // Update is used for input and animation updates, while physics and movement are handled in FixedUpdate.
        void Update()
        {
            // Keep input and animation updates on Update
            UpdateAnimations();
            // Timers that should run regardless of physics update
            if (_dashCooldownTimer > 0f) _dashCooldownTimer -= Time.deltaTime;
            if (_dashRecoveryTimer > 0f) _dashRecoveryTimer -= Time.deltaTime;
            if (_isBoosted)
            {
                _boostTimer -= Time.deltaTime;
                if (_boostTimer <= 0f) _isBoosted = false;
            }
        }

        // FixedUpdate is used for physics updates to ensure consistent timing. We call a custom PhysicsStep method to handle movement and physics integration.
        void FixedUpdate()
        {
            PhysicsStep(Time.fixedDeltaTime);
        }

        // Unsubscribe from input events on disable to avoid memory leaks or unintended behavior.
        void OnDisable()
        {
            if (inputHandler != null)
            {
                inputHandler.OnMove -= HandleMove;
                inputHandler.OnDash -= HandleDashInput;
                inputHandler.OnBoost -= HandleBoostInput;
            }
        }

        // Ensure we clean up subscriptions on destroy as well, in case the object is destroyed without being disabled first.
        void OnDestroy()
        {
            // Ensure we clean up subscriptions
            if (inputHandler != null)
            {
                try
                {
                    inputHandler.OnMove -= HandleMove;
                    inputHandler.OnDash -= HandleDashInput;
                    inputHandler.OnBoost -= HandleBoostInput;
                }
                catch { }
            }
        }

        // Validate tuning parameters to ensure they are within reasonable ranges. This is called when values are changed in the inspector.
        void OnValidate()
        {
            rotationSpeed = Mathf.Max(0f, rotationSpeed);

            if (gravity > 0f)
                gravity = -gravity;
        }

        // -----------------
        // Input callbacks
        // -----------------
        // Handle movement input by storing the current input vector. The actual movement is applied in the PhysicsStep method.
        private void HandleMove(Vector2 v)
        {
            _moveInput = v;
        }

        // Handle dash input by triggering a dash in the current movement direction. The actual dash impulse is applied in the TriggerDash method.
        private void HandleDashInput()
        {
            TriggerDash(Vector3.zero);
        }

        // Handle boost input by triggering a temporary boost state that modifies acceleration and friction. The actual boost effect is applied in the PhysicsStep method.
        private void HandleBoostInput()
        {
            TriggerBoost();
        }

        // -----------------
        // Public API
        // -----------------
        /// <summary>
        /// Trigger a dash in the given direction. Applies impulse according to tuning.
        /// </summary>
        public void TriggerDash(Vector3 direction)
        {
            if (_dashCooldownTimer > 0f || _isDashing)
                return; // still cooling down

            Vector3 dir = direction;
            if (dir.sqrMagnitude < 0.0001f)
                dir = _currentMoveDirection;
            if (dir.sqrMagnitude < 0.0001f)
                return; // no direction available

            dir = dir.normalized;

            // Apply dash as physical impulse: impulse J = dashForce * dashDuration (N*s)
            float impulse = DashForceValue * DashDurationValue;
            float deltaV = impulse / Mathf.Max(0.0001f, MassValue);
            _velocity += dir * deltaV;
            _isDashing = true;
            _dashTimer = DashDurationValue;
            _dashCooldownTimer = DashCooldownValue;
            _dashRecoveryTimer = 0f;
        }

        /// <summary>
        /// Trigger a temporary boost that increases acceleration and reduces friction.
        /// </summary>
        public void TriggerBoost()
        {
            _isBoosted = true;
            _boostTimer = BoostDurationValue;
        }

        // -----------------
        // Private helpers
        // -----------------
        // Apply horizontal movement, gravity and handle rotation toward movement direction.
        // Physics integration step. Uses simple Newtonian integration with friction and dash/boost states.
        private void PhysicsStep(float dt)
        {
            // build desired input direction in world space, optionally relative to camera
            Vector3 inputDir = new Vector3(_moveInput.x, 0f, _moveInput.y);
            if (useCameraRelative)
            {
                Transform cam = cameraTransform != null ? cameraTransform : Camera.main != null ? Camera.main.transform : null;
                if (cam != null)
                {
                    Vector3 camForward = cam.forward;
                    camForward.y = 0f;
                    camForward.Normalize();
                    Vector3 camRight = cam.right;
                    camRight.y = 0f;
                    camRight.Normalize();
                    inputDir = camForward * _moveInput.y + camRight * _moveInput.x;
                }
            }

            if (inputDir.sqrMagnitude > 0.0001f)
                inputDir = inputDir.normalized;
            else
                inputDir = Vector3.zero;

            _currentMoveDirection = inputDir;

            // compute current max acceleration and friction
            float currentAMax = _isBoosted ? AMaxValue * BoostAMultiplierValue : AMaxValue;
            float currentMuK = _isBoosted ? MuKValue * 0.5f : MuKValue;

            // DASH handling: during dash we set a target dash velocity and ignore friction/input
            if (_isDashing)
            {
                _dashTimer -= dt;
                if (_dashTimer <= 0f)
                {
                    _isDashing = false;
                    _dashRecoveryTimer = dashRecovery;
                }
                // velocity is unchanged after dash impulse - it was set on trigger
            }
            else
            {
                // Compute acceleration from input. Two modes:
                // - Acceleration integration (physical): use aMax to change velocity.
                // - Legacy smoothing: SmoothDamp towards desired velocity.
                Vector3 horizontalVel = new Vector3(_velocity.x, 0f, _velocity.z);
                if (inputDir == Vector3.zero && horizontalVel.sqrMagnitude > 0.00001f)
                {
                    // No input: apply kinetic friction opposing velocity
                    Vector3 vhat = horizontalVel.normalized;
                    Vector3 aFric = -vhat * (currentMuK * Mathf.Abs(GravityValue));
                    _velocity += aFric * dt;
                }
                else if (inputDir != Vector3.zero)
                {
                    if (useAccelerationIntegration)
                    {
                        // Physical acceleration integration: a = inputDir * aMax
                        Vector3 aInput = inputDir * currentAMax;
                        _velocity += aInput * dt;
                    }
                    else
                    {
                        // Legacy smoothing: move towards desired velocity using SmoothDamp
                        Vector3 desiredVel = inputDir * VMaxValue;
                        Vector3 newHor = Vector3.SmoothDamp(horizontalVel, desiredVel, ref _velocitySmoothRef, VelocitySmoothTimeValue, Mathf.Infinity, dt);
                        _velocity.x = newHor.x;
                        _velocity.z = newHor.z;
                    }
                }

                // clamp horizontal speed
                Vector3 hv = new Vector3(_velocity.x, 0f, _velocity.z);
                float hvMag = hv.magnitude;
                float vmaxCurrent = VMaxValue; // could be modified by stats
                if (hvMag > vmaxCurrent)
                    hv = hv.normalized * vmaxCurrent;
                // Snap to zero below threshold to avoid numerical jitter
                if (hv.magnitude < StopThresholdValue)
                {
                    _velocity.x = 0f; _velocity.z = 0f;
                }
                else
                {
                    _velocity.x = hv.x; _velocity.z = hv.z;
                }
            }

            // gravity
            if (_controller.isGrounded)
                _verticalVelocity = -1f; // keep snapped
            _verticalVelocity += GravityValue * dt;

            // final move vector
            Vector3 move = new Vector3(_velocity.x, 0f, _velocity.z) + Vector3.up * _verticalVelocity;

            _controller.Move(move * dt);

            // rotation toward movement direction if moving (only when above rotation threshold)
            Vector3 horizontalDir = new Vector3(_velocity.x, 0f, _velocity.z);
            if (horizontalDir.sqrMagnitude > RotationThresholdValue * RotationThresholdValue)
            {
                Quaternion targetRotation = Quaternion.LookRotation(horizontalDir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, RotationSpeedValue * dt);
            }
        }

        // Update animator parameters and notify listeners about the current speed.
        private void UpdateAnimations()
        {
            if (_animator == null)
                return;

            // Use actual horizontal velocity magnitude for animator and UI.
            currentSpeed = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            _animator.SetFloat(SpeedHash, currentSpeed);

            if (debugSpeed)
            {
                string settingsName = movementSettings != null ? movementSettings.name : "(none)";
                int settingsId = movementSettings != null ? movementSettings.GetInstanceID() : 0;
                int dashActive = _isDashing ? 1 : 0;
                int boostActive = _isBoosted ? 1 : 0;
                if (movementSettings != null)
                {
                    Debug.Log($"Position: {transform.position}, MoveDir: {_currentMoveDirection}, Speed: {currentSpeed}, Dash:{dashActive}, Boost:{boostActive}, UsingSettings:{settingsName} (id:{settingsId}), useAccel:{movementSettings.useAccelerationIntegration}\n" +
                              $"gravity:{movementSettings.gravity} m/s^2, mass:{movementSettings.mass} kg, aMax:{movementSettings.aMax} m/s^2, vMax:{movementSettings.vMax} m/s, muK:{movementSettings.muK}, muS:{movementSettings.muS}\n" +
                              $"velocitySmoothTime:{movementSettings.velocitySmoothTime} s, stopThreshold:{movementSettings.stopThreshold} m/s, rotationThreshold:{movementSettings.rotationThreshold} m\n" +
                              $"dashForce:{movementSettings.dashForce} N, dashDuration:{movementSettings.dashDuration} s, dashDistance:{movementSettings.dashDistance} m, dashCooldown:{movementSettings.dashCooldown} s, dashRecovery:{movementSettings.dashRecovery} s\n" +
                              $"boostAMultiplier:{movementSettings.boostAMultiplier}, boostDuration:{movementSettings.boostDuration} s, rotationSpeed:{movementSettings.rotationSpeed} deg/s");
                }
                else
                {
                    Debug.Log($"Position: {transform.position}, MoveDir: {_currentMoveDirection}, Speed: {currentSpeed}, Dash:{dashActive}, Boost:{boostActive}, UsingSettings:(none) -- fallbacks used\n" +
                              $"gravity:{gravity} m/s^2, mass:{mass} kg, aMax:{aMax} m/s^2, vMax:{vMax} m/s, muK:{muK}, muS:{muS}\n" +
                              $"velocitySmoothTime:{velocitySmoothTime} s, stopThreshold:{stopThreshold} m/s, rotationThreshold:{rotationThreshold} m\n" +
                              $"dashForce:{dashForce} N, dashDuration:{dashDuration} s, dashDistance:{dashDistance} m, dashCooldown:{dashCooldown} s, dashRecovery:{dashRecovery} s\n" +
                              $"boostAMultiplier:{boostAMultiplier}, boostDuration:{boostDuration} s, rotationSpeed:{rotationSpeed} deg/s, useAccelerationIntegration:{useAccelerationIntegration}");
                }
            }

            OnSpeedLogged?.Invoke(currentSpeed);
        }
    }
}
