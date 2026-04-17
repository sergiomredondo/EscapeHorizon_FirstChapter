using Game.Interaction;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Add to any GameObject that should react to the player's scan pulse:
    /// enemies, interactables, points of interest.
    ///
    /// On detection: swaps to _detectedMaterial and plays audio.
    /// After _resetDelay seconds (or when a new scan begins): reverts to base material.
    ///
    /// Assign _scanner from the scene (Bear's SH_ScannerController).
    /// </summary>
    public class SH_ScannableObject : MonoBehaviour
    {
        #region Inspector
        [Header("Scanner Reference")]
        [Tooltip("Reference to the SH_ScannerController on Bear. " +
                 "Assign in Inspector or leave empty to auto-find on Start.")]
        [SerializeField] private SH_ScannerController _scanner;
        
        [Header("Materials")]
        [Tooltip("Material applied when this object is detected by the scan pulse.")]
        [SerializeField] private Material _detectedMaterial;
        [Tooltip("Renderer to swap materials on when detected. " +
                 "If empty, will attempt to find a MeshRenderer in children.")]
        [SerializeField] private Renderer[] _targetRenderers;
        
        [Header("Detection Settings")]
        [Tooltip("Margin added to the scan radius for detection. " +
                 "Objects within this margin will be detected even if they are slightly outside the scan radius.")]
        [Min(0.05f)][SerializeField] private float _detectionMargin = 0.2f;

        [Header("Interactable Settings")]
        [Tooltip("Layer assigned to interactable objects.")]
        [SerializeField] private LayerMask _interactableLayer;
        [Tooltip("Duration the highlight persists for interactable objects after a scan.")]
        [SerializeField] private float _interactablePersistence = 10f;

        [Header("Audio")]
        [Tooltip("AudioClip played once when first detected by the pulse.")]
        [SerializeField] private AudioClip _detectedClip;
        [Tooltip("AudioClip played once when the detected highlight fades out.")]
        [SerializeField] private AudioClip _dismissClip;
        
        [Header("Time Settings")]
        [Tooltip("Seconds the detected highlight persists after the pulse passes. " +
                 "Set to 0 to use twice the scan duration automatically.")]
        [Min(0.01f)][SerializeField] private float _resetDelay = 0.01f;
        [Tooltip("Frequency of the detection reveal effect. " +
                 "Lower values result in a slower reveal, higher values result in a faster reveal.")]
        [Range(0f, 1f)][SerializeField] private float _detectionFrequency = 0.5f;

        #endregion

        #region Runtime State

        private Material[] _baseMaterials;
        private bool _detected;
        private float _resetTimer;
        private float _detectionTimer;
        bool lastChange = false;
        private float _resolvedResetDelay;
        private AudioSource _audioSource;
        private bool _hasTargetRenderer;
        private bool _isRevealed;
        protected SH_InteractableObject _interactableObject;

        #endregion

        #region Public API

        public bool IsDetected => _detected;

        /// <summary>
        /// Swaps the target renderers' materials between the base material and the detected material.
        /// </summary>
        /// <param name="toBaseMaterial">If true, sets the materials to the base materials; otherwise, sets them to the detected material.</param>
        public void ChangeMaterial(bool toBaseMaterial)
        {
            if (_hasTargetRenderer)
            {
                if (toBaseMaterial)
                {
                    for (int i = 0; i < _targetRenderers.Length; i++)
                    {
                        if (_targetRenderers[i] != null && _baseMaterials[i] != null)
                            _targetRenderers[i].material = _baseMaterials[i];
                    }
                }
                else
                {
                    foreach (var r in _targetRenderers)
                    {
                        if (r != null)
                        {
                            if (_interactableObject != null && _interactableObject.IsFocused)
                            {
                                r.material = _interactableObject.FocusMaterial;
                            }
                            else
                            {
                                r.material = _detectedMaterial;
                            }
                        }
                    }
                }

            }
        }

        /// <summary>
        /// Performs an alternate detection action at a specified frequency when detection and reveal conditions are
        /// met.
        /// </summary>
        /// <remarks>This method should be called regularly, such as within an update loop, to ensure
        /// detection timing is handled correctly. The detection action alternates state at each interval defined by the
        /// detection frequency.</remarks>
        public void AlternateDetection()
        {
            if (_detected && _isRevealed && _interactableObject != null)
            {
                _detectionTimer += Time.deltaTime;
                if (_detectionTimer >= _detectionFrequency)
                {
                    lastChange = !lastChange;
                    ChangeMaterial(lastChange);
                    _detectionTimer = 0f;
                }
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (_scanner == null)
                _scanner = FindFirstObjectByType<SH_ScannerController>();

            if (_targetRenderers == null || _targetRenderers.Length == 0)
                _targetRenderers = GetComponentsInChildren<Renderer>();

            _hasTargetRenderer = _targetRenderers != null && _targetRenderers.Length > 0;

            if (_hasTargetRenderer)
            {
                _baseMaterials = new Material[_targetRenderers.Length];
                for (int i = 0; i < _targetRenderers.Length; i++)
                {
                    _baseMaterials[i] = _targetRenderers[i].sharedMaterial;
                }
            }
            _interactableObject = GetComponent<SH_InteractableObject>();
            _audioSource = GetComponent<AudioSource>();
        }

        private void Update()
        {
            if (_scanner == null || !_hasTargetRenderer) return;

            // Detection check during active pulse.
            if (_scanner.IsScanActive)
            {
                Vector3 flatOrigin = new Vector3(_scanner.ScanOrigin.x, 0, _scanner.ScanOrigin.z);
                Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
                float dist = Vector3.Distance(flatPos, flatOrigin);

                if (!_detected && dist <= _scanner.ScanRadius && dist >= _scanner.ScanRadius - 1.5f)
                    OnDetected();
                    lastChange = false;
            }

            // Reset timer when not in scan and detected.
            if (_detected && !_scanner.IsScanActive)
            {
                _resetTimer += Time.deltaTime;
                if (_resetTimer >= _resolvedResetDelay)
                {
                    OnReset();
                }
                else
                {
                    AlternateDetection();
                }
            }
        }

        #endregion

        #region Detection Handlers

        private void OnDetected()
        {
            _detected = true;
            _isRevealed = true;
            _resetTimer = 0f;

            _resolvedResetDelay = ((1 << gameObject.layer) & _interactableLayer) != 0
                ? _interactablePersistence
                : _resetDelay;
            
            if (_hasTargetRenderer && _detectedMaterial != null)
            {
                ChangeMaterial(false);
            }

            PlayFeedbackAudio(_detectedClip);
        }

        private void OnReset()
        {
            _detected = false;
            _resetTimer = 0f;

            ChangeMaterial(true);

            PlayFeedbackAudio(_dismissClip);
        }

        private void PlayFeedbackAudio(AudioClip clip)
        {
            if (clip == null) return;

            if (_audioSource != null)
                _audioSource.PlayOneShot(clip);
            else
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        #endregion
    }
}