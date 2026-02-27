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
        public float moveSpeed = 5f;
        public float rotationSpeed = 10f;
        [Header("Physics")]
        [Tooltip("Gravity applied to the character (negative value).")]
        public float gravity = -9.81f;

        [Header("Physical Parameters")]
        [Tooltip("Mass of the mech in kg.")]
        public float mass = 3000f;

        [Tooltip("Maximum linear acceleration (m/s^2) applied by the motors.")]
        public float aMax = 9.81f;

        [Tooltip("Maximum horizontal speed (m/s).")]
        public float vMax = 10f;

        [Tooltip("Kinetic friction coefficient (used when no input is applied).")]
        public float muK = 0.4f;

        [Tooltip("Static friction coefficient (unused for now, reserved for transitions).")]
        public float muS = 0.6f;

        [Header("Camera Relative")]
        [Tooltip("When true, movement input is interpreted relative to the camera orientation.")]
        public bool useCameraRelative = true;

        [Tooltip("Optional reference to camera transform to use for camera-relative movement. If null, Camera.main is used.")]
        public Transform cameraTransform;

        [Header("Smoothing")]
        [Tooltip("Time in seconds to smooth horizontal velocity towards target. Higher = heavier feeling.")]
        public float velocitySmoothTime = 0.25f;

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

        // Current computed horizontal speed (for inspector or UI binding).
        [SerializeField]
        private float currentSpeed;

        [Header("Dash / Boost")]
        [Tooltip("Dash impulse force in Newtons (instantaneous impulse applied).")]
        public float dashForce = 30000f;

        [Tooltip("Dash duration in seconds (impulse window).")]
        public float dashDuration = 0.15f;

        [Tooltip("Dash distance in meters (informational).")]
        public float dashDistance = 6f;

        [Tooltip("Dash cooldown in seconds.")]
        public float dashCooldown = 1.5f;

        [Tooltip("Post-dash recovery time in seconds where control is limited.")]
        public float dashRecovery = 0.3f;

        // internal dash state
        private bool _isDashing;
        private float _dashTimer;
        private float _dashCooldownTimer;
        private float _dashRecoveryTimer;

        [Tooltip("Boost increases aMax by this multiplier while active.")]
        public float boostAMultiplier = 1.5f;

        [Tooltip("Duration of temporary boost in seconds.")]
        public float boostDuration = 8f;

        private bool _isBoosted;
        private float _boostTimer;

        [Header("Debug")]
        [Tooltip("When enabled, logs speed each frame. Turn off for release builds.")]
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
            float currentAMax = _isBoosted ? aMax * boostAMultiplier : aMax;
            float currentMuK = _isBoosted ? muK * 0.5f : muK;

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
                // Compute acceleration from input. Use smoothing when there is input to simulate heavy inertia.
                Vector3 horizontalVel = new Vector3(_velocity.x, 0f, _velocity.z);
                if (inputDir == Vector3.zero && horizontalVel.sqrMagnitude > 0.00001f)
                {
                    // No input: apply kinetic friction opposing velocity
                    Vector3 vhat = horizontalVel.normalized;
                    Vector3 aFric = -vhat * (currentMuK * Mathf.Abs(gravity));
                    _velocity += aFric * dt;
                }
                else if (inputDir != Vector3.zero)
                {
                    // Input present: compute desired velocity and smooth towards it
                    Vector3 desiredVel = inputDir * vMax;
                    Vector3 newHor = Vector3.SmoothDamp(horizontalVel, desiredVel, ref _velocitySmoothRef, velocitySmoothTime, Mathf.Infinity, dt);
                    _velocity.x = newHor.x;
                    _velocity.z = newHor.z;
                }

                // clamp horizontal speed
                Vector3 hv = new Vector3(_velocity.x, 0f, _velocity.z);
                float hvMag = hv.magnitude;
                float vmaxCurrent = vMax; // could be modified by stats
                if (hvMag > vmaxCurrent)
                    hv = hv.normalized * vmaxCurrent;
                _velocity.x = hv.x; _velocity.z = hv.z;
            }

            // gravity
            if (_controller.isGrounded)
                _verticalVelocity = -1f; // keep snapped
            _verticalVelocity += gravity * dt;

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
                Debug.Log($"Position: {transform.position}, MoveDir: {_currentMoveDirection}, Speed: {currentSpeed}");

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

            float dashSpeed = dashDistance / Mathf.Max(0.0001f, dashDuration);
            _velocity = dir * dashSpeed;
            _isDashing = true;
            _dashTimer = dashDuration;
            _dashCooldownTimer = dashCooldown;
            _dashRecoveryTimer = 0f;
        }

        /// <summary>
        /// Trigger a temporary boost that increases acceleration and reduces friction.
        /// </summary>
        public void TriggerBoost()
        {
            _isBoosted = true;
            _boostTimer = boostDuration;
        }

        // Validate and clamp inspector values when properties are changed in the editor.
        void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);

            if (gravity > 0f)
                gravity = -gravity;
        }
    }
}
