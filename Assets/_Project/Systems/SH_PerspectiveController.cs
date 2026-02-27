using System;
using UnityEngine;

namespace Systems
{
    // Exposes the active camera transform for camera-relative systems.
    // The scene should have one SH_PerspectiveController that manages active camera selection.
    [DisallowMultipleComponent]
    public class SH_PerspectiveController : MonoBehaviour
    {
        // Publicly available active camera transform
        public Transform ActiveCameraTransform { get; private set; }

        // Event when active camera changes
        public event Action<Transform> OnActiveCameraChanged;

        [Tooltip("Optional explicit camera GameObject to use. If null, the controller will try to find Cam_Isometric or use Camera.main.")]
        public Transform explicitCamera;

        void Start()
        {
            if (explicitCamera != null)
            {
                SetActiveCamera(explicitCamera);
                return;
            }

            // Try to find GameObject named Cam_Isometric
            var go = GameObject.Find("Cam_Isometric");
            if (go != null)
            {
                SetActiveCamera(go.transform);
                return;
            }

            // Try Cinemachine virtual cameras by priority (if present)
            try
            {
                var vcams = FindObjectsOfType<Cinemachine.CinemachineVirtualCamera>();
                if (vcams != null && vcams.Length > 0)
                {
                    Cinemachine.CinemachineVirtualCamera best = null;
                    int bestPriority = int.MinValue;
                    foreach (var v in vcams)
                    {
                        if (v.gameObject.name == "Cam_Isometric")
                        {
                            SetActiveCamera(v.transform);
                            return;
                        }
                        if (v.Priority > bestPriority)
                        {
                            best = v;
                            bestPriority = v.Priority;
                        }
                    }

                    if (best != null)
                    {
                        SetActiveCamera(best.transform);
                        return;
                    }
                }
            }
            catch { }

            if (Camera.main != null)
                SetActiveCamera(Camera.main.transform);
        }

        public void SetActiveCamera(Transform t)
        {
            if (t == ActiveCameraTransform) return;
            ActiveCameraTransform = t;
            OnActiveCameraChanged?.Invoke(t);
        }
    }
}
