using Core.Input;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Wraps the Terrain Scanner asset (SensorDetector pattern) and integrates
    /// it with SH_InputHandler instead of Unity's legacy Input system.
    ///
    /// Place on Bear. Assign the CameraEffect, sensor material and audio source
    /// from the Terrain Scanner asset setup in the Inspector.
    ///
    /// Exposes IsScanActive, ScanOrigin and ScanRadius so SH_ScannableObject
    /// instances can react without any direct coupling to this component.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_ScannerController : MonoBehaviour
    {
        #region Inspector References

        [Header("Terrain Scanner Asset References")]
        [Tooltip("CameraEffect component from the Terrain Scanner asset. " +
                 "Add it to the Main Camera and assign here.")]
        [SerializeField] private TerrainScanner.CameraEffect _cameraEffect;

        [Tooltip("Sensor material from the Terrain Scanner asset (e.g. RevealPost.mat).")]
        [SerializeField] private Material _sensorMaterial;

        [Header("Scanner Parameters")]
        [Tooltip("Maximum radius the scan pulse reaches before stopping.")]
        [Min(1f)]
        [SerializeField] private float _maxDistance = 25f;

        [Tooltip("Expansion speed of the pulse in units per second.")]
        [Min(1f)]
        [SerializeField] private float _expansionSpeed = 12f;

        [Tooltip("Time in seconds for the emission to fade out after the pulse finishes.")]
        [Min(0.1f)]
        [SerializeField] private float _killTime = 0.8f;

        [Header("Audio")]
        [Tooltip("AudioSource on this GameObject for the scan start sound.")]
        [SerializeField] private AudioSource _audioSource;

        [Header("Input")]
        [Tooltip("SH_InputHandler on this GameObject or a parent.")]
        [SerializeField] private SH_InputHandler _inputHandler;

        #endregion

        #region Runtime State

        private bool _scanActive;
        private bool _killPhase;
        private float _scanTimer;
        private float _killTimer;
        private float _scanDuration;
        private float _currentRadius;
        private float _cachedEmission;
        private Vector3 _scanOrigin;

        public bool IsScanActive => _scanActive;
        public Vector3 ScanOrigin => _scanOrigin;
        public float ScanRadius => _currentRadius;
        public float ScanDuration => _scanDuration;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_inputHandler == null)
                _inputHandler = GetComponentInParent<SH_InputHandler>();

            if (_sensorMaterial != null)
            {
                _cachedEmission = _sensorMaterial.GetFloat("_OverlayEmission");
                _sensorMaterial.SetFloat("_Radius", 0f);
            }

            if (_cameraEffect != null)
            {
                _cameraEffect.material = _sensorMaterial;
                _cameraEffect.enabled = false;
            }
        }

        private void OnDisable()
        {
            if (_sensorMaterial == null) return;
            _sensorMaterial.SetFloat("_Radius", 0f);
            _sensorMaterial.SetFloat("_OverlayEmission", _cachedEmission);
        }

        private void Update()
        {
            ReadInput();
            TickScanPulse();
            TickKillPhase();
        }

        #endregion

        #region Input

        private void ReadInput()
        {
            if (_inputHandler == null) return;
            if (!_inputHandler.ScanPressed) return;

            _inputHandler.ConsumeScanPressed();
            TriggerScan();
        }

        #endregion

        #region Scan Logic

        private void TriggerScan()
        {
            if (_scanActive || _killPhase) return;
            if (_sensorMaterial == null) return;

            _scanDuration = _maxDistance / _expansionSpeed;
            _scanTimer = 0f;
            _currentRadius = 0f;
            _scanOrigin = transform.position;

            _cachedEmission = _sensorMaterial.GetFloat("_OverlayEmission");
            _sensorMaterial.SetVector("_RevealOrigin", _scanOrigin);

            if (_cameraEffect != null) _cameraEffect.enabled = true;
            if (_audioSource != null) _audioSource.Play();

            _scanActive = true;
        }

        private void TickScanPulse()
        {
            if (!_scanActive) return;

            _scanTimer += Time.deltaTime;
            _currentRadius = Mathf.Min(_expansionSpeed * _scanTimer, _maxDistance);
            _sensorMaterial.SetFloat("_Radius", _currentRadius);

            if (_scanTimer >= _scanDuration)
            {
                _scanTimer = 0f;
                _scanActive = false;
                _killPhase = true;
                _currentRadius = 0f;
            }
        }

        private void TickKillPhase()
        {
            if (!_killPhase) return;

            if (_killTimer >= _killTime)
            {
                _killTimer = 0f;
                _killPhase = false;
                _sensorMaterial.SetFloat("_Radius", 0f);
                _sensorMaterial.SetFloat("_OverlayEmission", _cachedEmission);
                if (_cameraEffect != null) _cameraEffect.enabled = false;
                return;
            }

            float t = _killTimer / _killTime;
            _sensorMaterial.SetFloat("_OverlayEmission",
                Mathf.Lerp(_cachedEmission, 0f, t));
            _killTimer += Time.deltaTime;
        }

        #endregion

        #region Debug

        [ContextMenu("Debug — Trigger Scan")]
        private void Debug_TriggerScan()
        {
            if (Application.isPlaying) TriggerScan();
        }

        #endregion
    }
}