using Core.Input;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Drives the scanner post-process effect for URP.
    /// No dependency on TerrainScanner.CameraEffect — visibility is controlled
    /// by setting _ScanRadius to -1 on the shared material when inactive.
    ///
    /// Place on Bear. Assign the shared scanner material (Hidden/ScannerWorld)
    /// and the SH_InputHandler in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_ScannerController : MonoBehaviour
    {
        #region Inspector

        [Header("Scanner Material")]
        [Tooltip("Shared material using the Hidden/ScannerWorld shader. " +
                 "Must also be assigned to the ScannerRenderFeature on the URP Renderer.")]
        [SerializeField] private Material _scannerMaterial;
        [Tooltip("Optional: Texture used for the scanner grid effect. " +
                 "Assign a texture to create a grid pattern on the scanner ring.")]
        [SerializeField] private Texture2D _gridTexture;
        [Tooltip("Optional: Texture used for noise effect on the scanner ring. " +
                 "Assign a texture to create a noise pattern on the scanner ring.")]
        [SerializeField] private Texture2D _noiseTexture;

        [Header("Scanner Visual Parameters")]
        [Tooltip("Width of the scanner ring in world units (Shader parameter _ScanWidth). " +
                 "Controls the thickness of the visual effect.")]
        [Min(0.05f)][SerializeField] private float _scanWidth = 0.5f;
        [Tooltip("Density of the scanner ring effect (Shader parameter _Intensity). " +
                 "Controls how solid or transparent the effect appears.")]
        [Range(0.05f, 1f)][SerializeField] private float _waveDensity = 0.7f;
        [Tooltip("Scale of the scanner grid effect (Shader parameter _GridScale). " +
                 "Controls the size of the grid pattern on the scanner ring.")]
        [Range(0.01f, 1f)][SerializeField] private float _gridScale = 0.1f;
        [Tooltip("Color of the scanner ring (Shader parameter _ScanColor). " +
                 "Controls the visual color of the effect.")]
        [SerializeField] private Color _scanColor = Color.cyan;

        [Header("Scanner Expansion Parameters")]
        [Tooltip("Maximum distance the scanner ring can reach in world units (Shader parameter _maxDistance). " +
                 "Controls how far the effect expands before fading out.")]
        [Min(1f)][SerializeField] private float _maxDistance = 1f;
        [Tooltip("Speed at which the scanner ring expands in world units per second. " +
                 "Controls how quickly the effect grows to its maximum distance.")]
        [Min(1f)][SerializeField] private float _expansionSpeed = 1f;
        [Tooltip("Duration of the kill phase in seconds. " +
                 "Controls how long the effect takes to fade out after reaching max distance.")]
        [Min(0.1f)][SerializeField] private float _killTime = 0.8f;
        [Tooltip("Rate at which the scanner ring fades out during the kill phase (Shader parameter _ScanRadius). " +
                 "Controls how quickly the effect disappears after reaching max distance.")]
        [Range(0f, 1f)][SerializeField] private float _fadeDecrement = 0.5f;
        

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;

        [Header("Input")]
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

        private static readonly int PropScanRadius = Shader.PropertyToID("_ScanRadius");
        private static readonly int PropScanWidth = Shader.PropertyToID("_ScanWidth");
        private static readonly int PropScanCenter = Shader.PropertyToID("_ScannerCenter");
        private static readonly int PropScanColor = Shader.PropertyToID("_ScanColor");
        private static readonly int PropMaxDistance = Shader.PropertyToID("_maxDistance");
        private static readonly int PropIntensity = Shader.PropertyToID("_Intensity");
        private static readonly int PropGridScale = Shader.PropertyToID("_GridScale");
        private static readonly int PropFadeDec = Shader.PropertyToID("_FadeDecrement");

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_inputHandler == null)
                _inputHandler = GetComponentInParent<SH_InputHandler>();
            ResetMaterial();
        }

        private void OnDisable()
        {
            ResetMaterial();
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
            if (_inputHandler == null || !_inputHandler.ScanPressed) return;
            _inputHandler.ConsumeScanPressed();
            TriggerScan();
        }

        #endregion

        #region Scan Logic

        private void ResetMaterial()
        {
            if (_scannerMaterial == null) return;
            _scannerMaterial.SetFloat(PropScanRadius, -1f);
            _scannerMaterial.SetFloat(PropFadeDec, 1f);
            _scannerMaterial.SetFloat(PropScanWidth, _scanWidth);
            _scannerMaterial.SetColor(PropScanColor, _scanColor);
            _scannerMaterial.SetFloat(PropMaxDistance, _maxDistance);
        }

        private void TriggerScan()
        {
            if (_scanActive || _killPhase || _scannerMaterial == null) return;

            _scanDuration = _maxDistance / _expansionSpeed;
            _scanTimer = 0f;
            _currentRadius = 0f;
            _scanOrigin = transform.position;

            _scannerMaterial.SetVector(PropScanCenter,
                new Vector4(_scanOrigin.x, _scanOrigin.y, _scanOrigin.z, 0f));
            _scannerMaterial.SetTexture("_GridTex", _gridTexture);
            _scannerMaterial.SetFloat("_GridScale", _gridScale);
            _scannerMaterial.SetTexture("_NoiseTex", _noiseTexture);
            _scannerMaterial.SetFloat(PropMaxDistance, _maxDistance);

            if (_audioSource != null) _audioSource.Play();

            _scanActive = true;
        }

        private void TickScanPulse()
        {
            if (!_scanActive) return;

            _scanTimer += Time.deltaTime;
            _currentRadius = Mathf.Min(_expansionSpeed * _scanTimer, _maxDistance);

            _scannerMaterial.SetFloat(PropScanRadius, _currentRadius);
            _scannerMaterial.SetFloat(PropScanWidth, _waveDensity);
            _scannerMaterial.SetFloat(PropScanWidth, _scanWidth);
            _scannerMaterial.SetColor(PropScanColor, _scanColor);
            _scannerMaterial.SetFloat(PropGridScale, _gridScale);
            _scannerMaterial.SetFloat(PropFadeDec, 1.0f);

            float intensity = Mathf.Lerp(1.5f, 0.8f, _scanTimer / _scanDuration);
            _scannerMaterial.SetFloat(PropIntensity, intensity);

            if (_scanTimer >= _scanDuration)
            {
                _cachedEmission = _currentRadius;
                _scanTimer = 0f;
                _scanActive = false;
                _killPhase = true;
            }
        }

        private void TickKillPhase()
        {
            if (!_killPhase) return;

            if (_killTimer >= _killTime)
            {
                _killTimer = 0f;
                _killPhase = false;
                _currentRadius = 0f;
                _scannerMaterial.SetFloat(PropScanRadius, -1f);
                return;
            }

            // Fade the ring out by shrinking it toward the edge.
            _killTimer += Time.deltaTime;
            float t = _killTimer / _killTime;
            float killIntensity = Mathf.Lerp(0.8f, 0f, t);
            _scannerMaterial.SetFloat(PropFadeDec, Mathf.Lerp(1.0f, _fadeDecrement, t));
            _scannerMaterial.SetFloat(PropIntensity, Mathf.Lerp(0.8f, 0f, t));
            _scannerMaterial.SetFloat(PropScanRadius, Mathf.Lerp(_cachedEmission, _maxDistance + 0.5f, t));


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