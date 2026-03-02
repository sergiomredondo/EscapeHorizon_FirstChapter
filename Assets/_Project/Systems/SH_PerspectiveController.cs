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

            // Try Cinemachine virtual cameras by priority (if present).
            // Use reflection so this code compiles even when Cinemachine package is absent.
            try
            {
                var vcamType = Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine");
                if (vcamType != null)
                {
                    var objs = Resources.FindObjectsOfTypeAll(vcamType);
                    if (objs != null && objs.Length > 0)
                    {
                        object best = null;
                        int bestPriority = int.MinValue;
                        foreach (var o in objs)
                        {
                            var comp = o as Component;
                            if (comp == null) continue;
                            if (comp.gameObject.name == "Cam_Isometric")
                            {
                                SetActiveCamera(comp.transform);
                                return;
                            }
                            // try to read Priority via reflection
                            try
                            {
                                var pr = vcamType.GetProperty("Priority");
                                if (pr != null)
                                {
                                    int p = (int)pr.GetValue(o);
                                    if (p > bestPriority)
                                    {
                                        bestPriority = p;
                                        best = comp;
                                    }
                                }
                                else if (best == null)
                                {
                                    best = comp;
                                }
                            }
                            catch { if (best == null) best = comp; }
                        }
                        if (best is Component bc)
                        {
                            // Prefer the actual runtime Camera (Camera.main) so camera-relative input
                            // uses the rendered camera orientation and not the virtual camera GameObject.
                            if (Camera.main != null)
                                SetActiveCamera(Camera.main.transform);
                            else
                                SetActiveCamera(bc.transform);
                            return;
                        }
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
