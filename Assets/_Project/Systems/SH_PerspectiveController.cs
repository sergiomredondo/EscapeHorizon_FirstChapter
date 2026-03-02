using System;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Provides a deterministic spatial reference for camera-relative movement calculations.
    /// Enforces explicit camera assignment to eliminate implicit scene dependencies.
    /// Acts as the Single Source of Truth (SSOT) for world-to-perspective orientation.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_PerspectiveController : MonoBehaviour
    {
        [Header("Perspective Configuration")]
        [Tooltip("Explicit camera transform reference. Mandatory for deterministic orientation logic.")]
        [SerializeField] private Transform cameraTransform;

        /// <summary>
        /// Active spatial reference used by locomotion states to calculate relative vectors.
        /// Guaranteed to be valid if the component is enabled.
        /// </summary>
        public Transform ActiveCameraTransform { get; private set; }

        /// <summary>
        /// Event dispatched when the active perspective reference is updated at runtime.
        /// </summary>
        public event Action<Transform> OnActiveCameraChanged;

        private bool _isInitialized;

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Validates requirements and establishes the initial perspective reference.
        /// Disables the component to prevent undefined behavior if dependencies are missing.
        /// </summary>
        private void Initialize()
        {
            if (cameraTransform == null)
            {
                Debug.LogError($"[Perspective] Critical Failure: Camera reference missing on {name}. Execution halted.");
                enabled = false;
                return;
            }

            ActiveCameraTransform = cameraTransform;
            _isInitialized = true;

            OnActiveCameraChanged?.Invoke(ActiveCameraTransform);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Updates the active camera reference during runtime.
        /// Rejection criteria: null targets or redundant assignments.
        /// </summary>
        /// <param name="target">The new Transform to be used as a spatial reference.</param>
        public void SetActiveCamera(Transform target)
        {
            if (!_isInitialized) return;

            if (target == null)
            {
                Debug.LogError($"[Perspective] Runtime Error: Attempted to assign null camera to {name}.");
                return;
            }

            if (target == ActiveCameraTransform) return;

            ActiveCameraTransform = target;
            OnActiveCameraChanged?.Invoke(target);
        }

        #endregion
    }
}