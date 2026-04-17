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

        [Header("Audio")]
        [Tooltip("AudioClip played once when first detected by the pulse.")]
        [SerializeField] private AudioClip _detectedClip;
        [Tooltip("AudioClip played once when the detected highlight fades out.")]
        [SerializeField] private AudioClip _dismissClip;
        
        [Header("Reset Settings")]
        [Tooltip("Seconds the detected highlight persists after the pulse passes. " +
                 "Set to 0 to use twice the scan duration automatically.")]
        [Min(0.01f)][SerializeField] private float _resetDelay = 0.01f;

        #endregion

        #region Runtime State

        private Material[] _baseMaterials;
        private bool _detected;
        private float _resetTimer;
        private float _resolvedResetDelay;
        private AudioSource _audioSource;
        private bool _hasTargetRenderer;

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
            }

            // Reset timer when not in scan and detected.
            if (_detected && !_scanner.IsScanActive)
            {
                _resetTimer += Time.deltaTime;
                if (_resetTimer >= _resolvedResetDelay)
                    OnReset();
            }
        }

        #endregion

        #region Detection Handlers

        private void OnDetected()
        {
            _detected = true;
            _resetTimer = 0f;
            _resolvedResetDelay = _resetDelay;

            if (_hasTargetRenderer && _detectedMaterial != null)
            {
                foreach (var r in _targetRenderers)
                {
                    if (r != null) r.material = _detectedMaterial;
                }
            }

            PlayFeedbackAudio(_detectedClip);
        }

        private void OnReset()
        {
            _detected = false;
            _resetTimer = 0f;

            if (_hasTargetRenderer)
            {
                for (int i = 0; i < _targetRenderers.Length; i++)
                {
                    if (_targetRenderers[i] != null && _baseMaterials[i] != null)
                        _targetRenderers[i].material = _baseMaterials[i];
                }
            }

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