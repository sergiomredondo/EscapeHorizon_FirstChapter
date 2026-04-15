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

        [Tooltip("Reference to the SH_ScannerController on Bear. " +
                 "Assign in Inspector or leave empty to auto-find on Start.")]
        [SerializeField] private SH_ScannerController _scanner;

        [Header("Materials")]
        [Tooltip("Material applied when this object is detected by the scan pulse.")]
        [SerializeField] private Material _detectedMaterial;

        [Tooltip("Renderer whose material will be swapped. " +
                 "Leave empty to use the first MeshRenderer on this GameObject.")]
        [SerializeField] private Renderer _targetRenderer;

        [Header("Audio")]
        [Tooltip("AudioClip played once when first detected by the pulse.")]
        [SerializeField] private AudioClip _detectedClip;

        [Tooltip("AudioClip played once when the detected highlight fades out.")]
        [SerializeField] private AudioClip _dismissClip;

        [Tooltip("Seconds the detected highlight persists after the pulse passes. " +
                 "Set to 0 to use twice the scan duration automatically.")]
        [Min(0f)]
        [SerializeField] private float _resetDelay = 0f;

        #endregion

        #region Runtime State

        private Material _baseMaterial;
        private bool _detected;
        private float _resetTimer;
        private float _resolvedResetDelay;
        private AudioSource _audioSource;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (_scanner == null)
                _scanner = FindFirstObjectByType<SH_ScannerController>();

            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<MeshRenderer>();

            if (_targetRenderer != null)
                _baseMaterial = _targetRenderer.sharedMaterial;

            _audioSource = GetComponent<AudioSource>();
        }

        private void Update()
        {
            if (_scanner == null) return;

            // Detection check during active pulse.
            if (_scanner.IsScanActive)
            {
                float dist = Vector3.Distance(transform.position, _scanner.ScanOrigin);
                if (!_detected && dist < _scanner.ScanRadius - 0.5f)
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
            _resolvedResetDelay = _resetDelay > 0f
                ? _resetDelay
                : _scanner.ScanDuration * 2f;

            if (_targetRenderer != null && _detectedMaterial != null)
                _targetRenderer.material = _detectedMaterial;

            if (_detectedClip != null)
            {
                if (_audioSource != null)
                    _audioSource.PlayOneShot(_detectedClip);
                else
                    AudioSource.PlayClipAtPoint(_detectedClip, transform.position);
            }
        }

        private void OnReset()
        {
            _detected = false;
            _resetTimer = 0f;

            if (_targetRenderer != null && _baseMaterial != null)
                _targetRenderer.material = _baseMaterial;

            if (_dismissClip != null)
            {
                if (_audioSource != null)
                    _audioSource.PlayOneShot(_dismissClip);
                else
                    AudioSource.PlayClipAtPoint(_dismissClip, transform.position);
            }
        }

        #endregion
    }
}