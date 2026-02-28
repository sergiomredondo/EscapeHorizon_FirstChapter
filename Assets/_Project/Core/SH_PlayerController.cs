using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Simple character controller for the player character.
namespace PlayerMovement
{
    // Ensure the GameObject has a CharacterController component.
    [RequireComponent(typeof(CharacterController))]
    public class SH_CharacterController : MonoBehaviour
    {
        [Header("Movement settings")]
        [Tooltip("Rotation speed used to smoothly rotate the character toward movement direction.")]
        public float rotationSpeed = 10f;
        [Header("Physics")]
        [SerializeField, HideInInspector]
        private float gravity = -9.81f;

        [Header("Physical Parameters")]
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
        [SerializeField, HideInInspector]
        private float velocitySmoothTime = 0.25f;

        [Tooltip("When true uses acceleration integration (physical). When false uses legacy SmoothDamp smoothing.")]
        public bool useAccelerationIntegration = true;

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

        // Called when the script instance is loaded. Cache components and prepare input actions.
        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();

            if (_controller == null)
                Debug.LogWarning("SH_CharacterController requires a CharacterController component.");
            if (_animator == null)
                Debug.LogWarning("SH_CharacterController: Animator not found. Animations will be disabled.");

            // inputHandler should be assigned in the Inspector or discovered at runtime
        }

        // Enable subscriptions to the centralized input handler.
        void OnEnable()
        {
            if (inputHandler == null)
                inputHandler = FindObjectOfType<Core.SH_InputHandler>();
            if (inputHandler != null)
            {
                inputHandler.OnMove += HandleMove;
                inputHandler.OnDash += HandleDashInput;
                inputHandler.OnBoost += HandleBoostInput;
            }
        }

        // Unsubscribe from the input handler.
        void OnDisable()
        {
            if (inputHandler != null)
            {
                inputHandler.OnMove -= HandleMove;
                inputHandler.OnDash -= HandleDashInput;
                inputHandler.OnBoost -= HandleBoostInput;
            }
        }

        // Receive move input from SH_InputHandler.
        private void HandleMove(Vector2 v)
        {
            _moveInput = v;
        }

        // Called when dash input received from SH_InputHandler.
        private void HandleDashInput()
        {
            TriggerDash(Vector3.zero);
        }

        // Called when boost input received from SH_InputHandler.
        private void HandleBoostInput()
        {
            TriggerBoost();
        }

        // Try to find supporting managers (Input / Perspective) at start if not assigned.
        void Start()
        {
            if (inputHandler == null)
                inputHandler = FindObjectOfType<Core.SH_InputHandler>();

            // If no explicit cameraTransform, try to get it from the PerspectiveController
            if (cameraTransform == null)
            {
                var p = FindObjectOfType<Systems.SH_PerspectiveController>();
                if (p != null && p.ActiveCameraTransform != null)
                    cameraTransform = p.ActiveCameraTransform;
            }
        }

        // Called each frame. Apply movement and update animations.
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

        // FixedUpdate used for physics integration (stable timestep)
        void FixedUpdate()
        {
            PhysicsStep(Time.fixedDeltaTime);
        }

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
                _velocity.x = hv.x; _velocity.z = hv.z;
            }

            // gravity
            if (_controller.isGrounded)
                _verticalVelocity = -1f; // keep snapped
            _verticalVelocity += GravityValue * dt;

            // final move vector
            Vector3 move = new Vector3(_velocity.x, 0f, _velocity.z) + Vector3.up * _verticalVelocity;

            _controller.Move(move * dt);

            // rotation toward movement direction if moving
            Vector3 horizontalDir = new Vector3(_velocity.x, 0f, _velocity.z);
            if (horizontalDir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(horizontalDir.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * dt);
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
                Debug.Log($"Position: {transform.position}, MoveDir: {_currentMoveDirection}, Speed: {currentSpeed}, Dash:{dashActive}, Boost:{boostActive}, UsingSettings:{settingsName} (id:{settingsId}), useAccel:{useAccelerationIntegration}, aMax:{AMaxValue}, vMax:{VMaxValue}, muK:{MuKValue}");
            }

            OnSpeedLogged?.Invoke(currentSpeed);
        }

        /// <summary>
        /// Trigger a dash in the given direction. Sets velocity to dash speed and starts timers.
        /// </summary>
        /// <param name="direction">World-space direction of the dash. If zero, uses current move direction.</param>
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
            // ?v = J / m
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

        // Validate and clamp inspector values when properties are changed in the editor.
        void OnValidate()
        {
            rotationSpeed = Mathf.Max(0f, rotationSpeed);

            if (gravity > 0f)
                gravity = -gravity;
        }
    }
}
