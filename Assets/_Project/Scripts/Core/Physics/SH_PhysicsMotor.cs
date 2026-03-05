using UnityEngine;
using Data;

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
        #region Serialized Fields

        [Header("Settings & Configuration")]
        [Tooltip("Link to the physical profile defining mass, gravity, and friction coefficients.")]
        [SerializeField] private SH_MovementSettings settings;

        #endregion

        #region Private Fields

        private CharacterController _controller;

        // --- Internal Physical State ---
        private Vector3 _velocity;
        private Vector3 _activeForce;
        private float _forceTimer;

        #endregion

        #region Public Telemetry (Debug API)

        /// <summary> Current integrated velocity vector in world space. </summary>
        public Vector3 CurrentVelocity => _velocity;

        /// <summary> Stores the vector of the last force or impulse processed for telemetry visualization. </summary>
        public Vector3 LastAppliedForce { get; private set; }

        /// <summary> Indicates if there is a sustained force currently influencing acceleration. </summary>
        public bool HasActiveForce => _forceTimer > 0f;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// Central integration loop. Processes forces, gravity, and friction before moving the controller.
        /// This method is called by the State Machine to ensure deterministic execution.
        /// </summary>
        /// <param name="dt">Delta time (usually Time.fixedDeltaTime or Time.deltaTime).</param>
        public void Tick(float dt)
        {
            ApplyGravity(dt);
            ApplyActiveForce(dt); // Integrates sustained external thrusts
            ApplyFriction(dt);    // Integrates mass-based drag

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
        public void ApplyForce(Vector3 force, float duration)
        {
            if (duration <= 0f)
            {
                ApplyImpulse(force);
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
        public void ApplyImpulse(Vector3 force)
        {
            if (settings == null || settings.mass <= 0) return;

            // F = m * a  =>  a = F / m
            Vector3 acceleration = force / settings.mass;
            _velocity += acceleration;
            LastAppliedForce = force;
        }

        /// <summary>
        /// Internal integration of sustained forces over the current frame time.
        /// </summary>
        private void ApplyActiveForce(float dt)
        {
            if (_forceTimer <= 0f)
                return;

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
        private void ApplyGravity(float dt)
        {
            if (_controller.isGrounded && _velocity.y < 0f)
            {
                // Small constant force to maintain ground contact
                _velocity.y = -2f;
            }
            else
            {
                _velocity.y += settings.gravity * dt;
            }
        }

        /// <summary>
        /// Applies kinetic friction on the horizontal plane.
        /// Friction acceleration is derived from gravity: a = μ * |g|.
        /// </summary>
        private void ApplyFriction(float dt)
        {
            if (!_controller.isGrounded)
                return;

            Vector2 horizontalVel = new Vector2(_velocity.x, _velocity.z);

            if (horizontalVel.magnitude < settings.stopThreshold)
            {
                horizontalVel = Vector2.zero;
            }
            else
            {
                // Deceleration force relative to gravity and the friction coefficient
                float frictionAcc = settings.muK * Mathf.Abs(settings.gravity);
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
        public void SetHorizontalVelocity(Vector3 horizontalVelocity)
        {
            _velocity.x = horizontalVelocity.x;
            _velocity.z = horizontalVelocity.z;
        }

        /// <summary>
        /// Adds a direct velocity delta to current movement. 
        /// </summary>
        public void AddHorizontalVelocity(Vector3 delta)
        {
            _velocity.x += delta.x;
            _velocity.z += delta.z;
        }

        #endregion
    }
}