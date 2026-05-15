using Data;
using UnityEngine;

namespace Core.Physics
{
    /// <summary>
    /// Authoritative physical integration module for the Mecha unit.
    /// Handles velocity integration, gravity, kinetic friction, and external force application
    /// based on a mass-dependent Newtonian model (F = m * a).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SH_PhysicsMotor : MonoBehaviour
    {
        #region Dependencies
        /// <summary> Reference to the CharacterController component for movement and collision handling. </summary>
        private CharacterController _controller;

        // --- Internal Physical State ---
        private Vector3 _velocity;
        private Vector3 _activeForce;
        private float _frictionMultiplier = 1f;
        private float _forceTimer;
        private float _currentFrictionMultiplier = 1f;
        private float _speedMultiplier = 1f;

        #endregion

        #region Public Telemetry (Debug API)

        /// <summary> Current integrated velocity vector in world space. </summary>
        public Vector3 CurrentVelocity => _velocity;

        /// <summary> Stores the vector of the last force or impulse processed for telemetry visualization. </summary>
        public Vector3 LastAppliedForce { get; private set; }

        /// <summary> Exposes the current friction multiplier for debugging purposes. </summary>
        public float frictionMultiplier => _frictionMultiplier;

        /// <summary> Indicates if there is a sustained force currently influencing acceleration. </summary>
        public bool HasActiveForce => _forceTimer > 0f;

        #endregion

        #region Unity Lifecycle

        /// Initializes references and validates critical dependencies. Logs errors if essential components are missing.
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// Central integration loop. Processes forces, gravity, and friction before moving the controller.
        /// This method is called by the State Machine to ensure deterministic execution.
        /// </summary>
        /// <param name="dt">Delta time (usually Time.fixedDeltaTime or Time.deltaTime).</param>
        public void Tick(SH_MovementSettings settings, float dt)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] Tick failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling Tick.");
#endif
                return;
            }
            if (dt <= 0) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] Tick failed: Delta time must be greater than zero. Received dt={dt}. Ensure that the calling code provides a valid delta time.");
#endif
                return; 
            }

                ApplyGravity(settings, dt);     // Integrates vertical acceleration and ensures grounding stability
            ApplyActiveForce(settings, dt); // Integrates sustained external thrusts
            ApplyFriction(settings, dt);          // Integrates mass-based drag
            ClampHorizontalVelocity(settings);  // Enforces maximum speed limits based on settings

            // Final integration of velocity into world-space movement
            _controller.Move(_velocity * dt);
        }

        #endregion

        #region External Forces (Newtonian Application)

        /// <summary>
        /// Applies a force vector (N) over a specific time window. 
        /// Automatically handles instantaneous impulses if duration is zero or less.
        /// Useful for sustained thrusters or wind.
        /// </summary>
        /// <param name="force">Force vector in Newtons.</param>
        /// <param name="duration">Duration in seconds for the force application.</param>
        public void ApplyForce(SH_MovementSettings settings, Vector3 force, float duration)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyForce failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling ApplyForce."); 
#endif
                return; 
            }
            if (force == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyForce failed: Force vector is null. Ensure that a valid Vector3 force is provided when calling ApplyForce."); 
#endif
                return; 
            }
            if (duration < 0) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyForce failed: Duration cannot be negative. Received duration={duration}. Ensure that the calling code provides a valid duration."); 
#endif
                return; 
            }

            if (duration <= 0f)
            {
                ApplyImpulse(settings, force);
                return;
            }

            _activeForce = force;
            _forceTimer = duration;
            LastAppliedForce = force;
        }

        /// <summary>
        /// Applies an instantaneous change in momentum (Impulse). 
        /// Calculated as Δv = Force / Mass. Useful for explosions or instant dashes.
        /// </summary>
        /// <param name="force">The force vector in Newtons.</param>
        public void ApplyImpulse(SH_MovementSettings settings, Vector3 force)
        {
            if (settings == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyImpulse failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling ApplyImpulse."); 
#endif
                return; 
            }
            if (force == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyImpulse failed: Force vector is null. Ensure that a valid Vector3 force is provided when calling ApplyImpulse."); 
#endif
                return; 
            }
            if (settings.mass <= 0) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyImpulse failed: Mass must be greater than zero. Received mass={settings.mass}. Ensure that the SH_MovementSettings has a valid mass value."); 
#endif
                return;   
            }

            // F = m * a  =>  a = F / m
            Vector3 acceleration = force / settings.mass;
            _velocity += acceleration;
            LastAppliedForce = force;
        }

        /// <summary>
        /// Internal integration of sustained forces over the current frame time.
        /// </summary>
        private void ApplyActiveForce(SH_MovementSettings settings, float dt)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyActiveForce failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling ApplyActiveForce."); 
#endif
                return; 
            }
            if (dt <= 0) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyActiveForce failed: Delta time must be greater than zero. Received dt={dt}. Ensure that the calling code provides a valid delta time."); 
#endif
                return; 
            }

            // No active force to apply or force duration has expired
            if (_forceTimer <= 0f)
                return;

            // If the applied force is below the static friction threshold, it should not cause movement.
            if (_activeForce.magnitude <= settings.muS * Mathf.Abs(settings.gravity) * settings.mass) 
            { 
#if UNITY_EDITOR
                Debug.LogWarning($"[SH_PhysicsMotor] ApplyActiveForce: Applied force is below static friction threshold."); 
#endif
                _activeForce = Vector3.zero;
            }

            // F = m * a  =>  a = F / m
            Vector3 acceleration = _activeForce / settings.mass;
            _velocity += acceleration * dt;

            _forceTimer -= dt;

            if (_forceTimer <= 0f)
            {
                _activeForce = Vector3.zero;
            }
        }

        #endregion

        #region Environmental Physics (Gravity & Friction)

        /// <summary>
        /// Handles vertical acceleration and grounding stability.
        /// Ensures the Mecha snaps to the ground when moving down slopes.
        /// </summary>
        private void ApplyGravity(SH_MovementSettings settings, float dt)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyGravity failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling ApplyGravity."); 
#endif
                return; 
            }
            if (dt <= 0) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyGravity failed: Delta time must be greater than zero. Received dt={dt}. Ensure that the calling code provides a valid delta time."); 
#endif
                return; 
            }

            if (_controller.isGrounded && _velocity.y < 0f)
            {
                // Small constant force to maintain ground contact
                _velocity.y = settings.gravity * dt;
            }
            else
            {
                _velocity.y += settings.gravity * dt;
            }
        }

        /// <summary> 
        /// Allows external systems to modify the friction multiplier, enabling dynamic changes to friction (e.g., slippery surfaces or speed boosts). 
        /// </summary>
        public void SetFrictionMultiplier(float multiplier)
        {
            if (multiplier < 0f) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] SetFrictionMultiplier failed: Multiplier cannot be negative. Received multiplier={multiplier}. Ensure that the calling code provides a valid non-negative multiplier."); 
#endif
                return; 
            }

            _frictionMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// Allows external systems to modify the speed multiplier, enabling dynamic changes to max speed (e.g., speed boosts or slow debuffs).
        /// </summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            if (multiplier < 0f) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] SetSpeedMultiplier failed: multiplier cannot be negative. Received multiplier={multiplier}."); 
#endif
                return; 
            }
            _speedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// Applies kinetic friction on the horizontal plane.
        /// Friction acceleration is derived from gravity: a = μ * |g|.
        /// </summary>
        private void ApplyFriction(SH_MovementSettings settings, float dt)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyFriction failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling ApplyFriction."); 
#endif
                return; 
            }
            if (dt <= 0) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ApplyFriction failed: Delta time must be greater than zero. Received dt={dt}. Ensure that the calling code provides a valid delta time."); 
#endif
                return; 
            }

            // Friction only applies when grounded
            if (!_controller.isGrounded) return;

            Vector2 horizontalVel = new Vector2(_velocity.x, _velocity.z);

            if (horizontalVel.magnitude < settings.stopThreshold)
            {
                horizontalVel = Vector2.zero;
            }
            else
            {
                // Gradually adjust the friction multiplier towards the target value for smoother transitions
                if (_currentFrictionMultiplier < _frictionMultiplier) { _currentFrictionMultiplier++; }

                // Friction acceleration: a = μ * |g|
                float frictionAcc = settings.muK * _frictionMultiplier * Mathf.Abs(settings.gravity);
                horizontalVel -= horizontalVel.normalized * frictionAcc * dt;
            }

            _velocity.x = horizontalVel.x;
            _velocity.z = horizontalVel.y;
        }

        #endregion

        #region Velocity Manipulation API

        /// <summary>
        /// Direct override of horizontal velocity. Used primarily by the Locomotion system.
        /// </summary>
        public void SetHorizontalVelocity(Vector3 horizontalVel)
        {
            if (horizontalVel == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] SetHorizontalVelocity failed: Horizontal velocity vector is null. Ensure that a valid Vector3 is provided when calling SetHorizontalVelocity."); 
#endif
                return; 
            }

            _velocity.x = horizontalVel.x;
            _velocity.z = horizontalVel.z;
        }

        /// <summary>
        /// Zeroes out horizontal velocity immediately.
        /// Called by SH_ActionState on Enter() to prevent residual locomotion
        /// momentum from interfering with attack animation and hitbox positioning.
        /// Vertical velocity (gravity) is preserved.
        /// </summary>
        public void CancelHorizontalVelocity()
        {
            _velocity.x = 0f;
            _velocity.z = 0f;
        }

        /// <summary>
        /// Adds a direct velocity delta to current movement. 
        /// </summary>
        public void AddHorizontalVelocity(Vector3 delta)
        {
            if (delta == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] AddHorizontalVelocity failed: Delta velocity vector is null. Ensure that a valid Vector3 is provided when calling AddHorizontalVelocity."); 
#endif
                return; 
            }

            _velocity.x += delta.x;
            _velocity.z += delta.z;
        }

        /// <summary>
        /// Clamps the horizontal velocity to the maximum defined in settings.
        /// </summary>
        private void ClampHorizontalVelocity(SH_MovementSettings settings)
        {
            if (settings == null) 
            { 
#if UNITY_EDITOR
                Debug.LogError($"[SH_PhysicsMotor] ClampHorizontalVelocity failed: Movement settings data is null. Ensure that valid SH_MovementSettings are passed when calling ClampHorizontalVelocity."); 
#endif
                return; 
            }

            Vector2 horizontalVel = new Vector2(_velocity.x, _velocity.z);

            float maxSpeed = settings.maxSpeed * _speedMultiplier;

            if (horizontalVel.sqrMagnitude > maxSpeed * maxSpeed)
            {
                horizontalVel = horizontalVel.normalized * maxSpeed;

                _velocity.x = horizontalVel.x;
                _velocity.z = horizontalVel.y;
            }
        }
        #endregion
    }
}