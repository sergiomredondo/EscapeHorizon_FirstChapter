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

        private CharacterController _controller;
        private Animator _animator;
        private Vector2 _moveInput;
        private Vector3 _currentMoveDirection;
        private IA_PlayerControls _inputActions;

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

            _inputActions = new IA_PlayerControls();
        }

        // Enable input actions and register movement callbacks.
        void OnEnable()
        {
            if (_inputActions == null)
                _inputActions = new IA_PlayerControls();

            // Register named handlers for clean unsubscribe.
            _inputActions.Player.Move.performed += OnMovePerformed;
            _inputActions.Player.Move.canceled += OnMoveCanceled;
            _inputActions.Player.Enable();
        }

        // Callback invoked when move input is performed. Stores the 2D input vector.
        private void OnMovePerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            _moveInput = ctx.ReadValue<Vector2>();
        }

        // Callback invoked when move input is canceled. Resets the input vector.
        private void OnMoveCanceled(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            _moveInput = Vector2.zero;
        }

        // Disable input actions and unregister movement callbacks.
        void OnDisable()
        {
            if (_inputActions != null)
            {
                _inputActions.Player.Move.performed -= OnMovePerformed;
                _inputActions.Player.Move.canceled -= OnMoveCanceled;
                _inputActions.Player.Disable();
            }

            // Ensure animations reflect idle state when disabled.
            UpdateAnimations();
        }

        // Cleanup created resources when the object is destroyed.
        void OnDestroy()
        {
            if (_inputActions != null)
            {
                try { _inputActions.Dispose(); } catch { }
                _inputActions = null;
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
            // build desired input direction in world space
            Vector3 inputDir = new Vector3(_moveInput.x, 0f, _moveInput.y);
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
                // Compute acceleration from input
                Vector3 aInput = inputDir * currentAMax;

                // If there is no input, apply kinetic friction opposing velocity
                Vector3 horizontalVel = new Vector3(_velocity.x, 0f, _velocity.z);
                if (inputDir == Vector3.zero && horizontalVel.sqrMagnitude > 0.00001f)
                {
                    Vector3 vhat = horizontalVel.normalized;
                    Vector3 aFric = -vhat * (currentMuK * Mathf.Abs(gravity));
                    // total acceleration is just friction
                    _velocity += aFric * dt;
                }
                else
                {
                    // apply input acceleration
                    _velocity += aInput * dt;
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

            currentSpeed = _currentMoveDirection.magnitude * moveSpeed;
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
