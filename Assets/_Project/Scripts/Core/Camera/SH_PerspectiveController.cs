using Data;
using UnityEngine;

namespace Core.Camera
{
    /// <summary>
    /// Central spatial authority for player orientation and input projection.
    /// Bridges camera-space, target-space (Lock-On), and world-space.
    /// Ensures movement intent remains consistent regardless of camera angle.
    /// </summary>
	[DisallowMultipleComponent]
    public class SH_PerspectiveController : MonoBehaviour
    {
        #region Dependencies

        [Header("References")]
        [Tooltip("Camera transform is used as the primary reference for projecting input into world space. Use the main camera or a dedicated camera rig.")]
        [SerializeField] private Transform _cameraTransform;

        [Tooltip("Transform of the current lock-on target. When assigned, it takes priority for orientation over the camera's forward direction.")]
        [SerializeField] private Transform _lockTarget;

        #endregion

        #region Initialization

        /// <summary> Initializes the perspective controller with necessary references. Ensures camera authority is established for input projection. </summary>
        private void Awake()
        {
            if (_cameraTransform == null) 
            {
#if UNITY_EDITOR
                Debug.LogError($"[SH_PerspectiveController] Falta referencia a la cámara en {gameObject.name}");
#endif
            }
        }

        /// <summary> Returns true if the system has an active target for orientation. </summary>
        public bool HasLockTarget => _lockTarget != null;

        /// <summary>
        /// Context-driven initialization to link movement data and camera authority.
        /// </summary>
        /// <param name="settings">Movement settings for spatial constraints.</param>
        /// <param name="camTransform">The camera transform to use as basis.</param>
        public void Initialize(SH_MovementSettings settings, Transform camTransform = null)
        {
            if (settings == null) {
#if UNITY_EDITOR
                Debug.LogError($"[SH_PerspectiveController] Initialization failed: MovementSettings data is null. Ensure that a valid SH_MovementSettings asset is assigned during initialization.");
#endif
                return;
            }

            // Fallback to main camera if no specific transform is provided, ensuring the system always has a reference for orientation.
            if (camTransform != null)
            {
                _cameraTransform = camTransform;
            }
            else if (_cameraTransform == null)
            {
                if (UnityEngine.Camera.main != null)
                {
                    _cameraTransform = UnityEngine.Camera.main.transform;
                }
                else
                {
#if UNITY_EDITOR
                    Debug.LogWarning("[SH_PerspectiveController] No Camera found. Perspective logic will use local forward.");
#endif
                }
            }
        }

        #endregion

        #region Directional Projection

        /// <summary>
        /// Projects 2D input into a 3D world-space direction vector on the XZ plane.
        /// </summary>
        /// <param name="input">Input vector from SH_InputHandler.</param>
        /// <returns>Normalized world-space direction.</returns>
        public Vector3 GetWorldSpaceDirection(Vector2 input)
        {
            // Early exit for negligible input to prevent jitter and unnecessary calculations
            if (input.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            // Resolve the forward and right vectors based on current perspective authority (Lock-On > Camera > Local)
            Vector3 forward = GetForward();
            Vector3 right = GetRight(forward);

            // Build the movement direction by combining the forward and right vectors scaled by input components
            Vector3 direction = (forward * input.y) + (right * input.x);

            return direction.normalized;
        }

        /// <summary>
        /// Resolves the current 'Forward' vector based on Lock-On priority or Camera orientation.
        /// </summary>
        /// <returns>Normalized forward vector projected on the horizontal plane.</returns>
        public Vector3 GetForward()
        {
            Vector3 forwardDirection;

            // Priority 1: Direction towards the lock-on target if it exists, ensuring the player faces the target regardless of camera angle.
            if (HasLockTarget)
            {
                forwardDirection = (_lockTarget.position - transform.position);
            }
            // Priority 2: Camera's forward direction, allowing movement to be relative to the player's view when not locked on.
            else if (_cameraTransform != null)
            {
                forwardDirection = _cameraTransform.forward;
            }
            // Priority 3: Fallback to the transform's local forward if no camera reference is available, ensuring the system remains functional even in edge cases.
            else
            {
                return transform.forward;
            }

            // Project the forward direction onto the horizontal plane to prevent unintended vertical movement, which is crucial for consistent locomotion behavior.
            forwardDirection.y = 0f;
            return forwardDirection.sqrMagnitude > 0.001f ? forwardDirection.normalized : transform.forward;
        }

        /// <summary>
        /// Resolves the 'Right' vector orthogonal to the provided forward vector.
        /// </summary>
        /// <param name="calculatedForward">The previously resolved forward direction.</param>
        /// <returns>Normalized right-hand side vector.</returns>
        public Vector3 GetRight(Vector3 calculatedForward)
        {
            // Cross product with the world up vector to ensure the right vector is always horizontal and orthogonal to the forward direction. The negative sign ensures a right-handed coordinate system, which is standard in Unity.
            return Vector3.Cross(Vector3.up, calculatedForward);
        }

        #endregion

        #region Lock-On Orchestration

        /// <summary>
        /// Sets the active target for the perspective system.
        /// </summary>
        /// <param name="target">Transform of the entity to track.</param>
        public void SetLockTarget(Transform target) => _lockTarget = target;

        /// <summary>
        /// Clears the lock target and returns to camera-relative perspective.
        /// </summary>
        public void ClearLockTarget() => _lockTarget = null;

        /// <summary>
        /// Returns the current lock target transform if available.
        /// </summary>
        public Transform GetLockTarget() => _lockTarget;

        #endregion
    }
}