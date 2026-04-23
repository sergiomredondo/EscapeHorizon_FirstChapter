using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Full-screen overlay shown when the player completes the level.
    /// Activated by SH_LevelEndTrigger. Pauses the game via Time.timeScale = 0.
    ///
    /// Hierarchy expected:
    ///   LevelCompleteOverlay (this script + Canvas)
    ///     └── Background (Image — assign your custom background sprite here)
    ///         └── ButtonGroup
    ///               ├── Button_Retry    → OnRetry()
    ///               └── Button_Title    → OnReturnToTitle()
    /// </summary>
    public class SH_LevelCompleteOverlay : MonoBehaviour
    {
        private const string TitleSceneName = "SCN_0_Title";
        private const string PrototypeSceneName = "SCN_1_Prototype";

        [Header("UI References")]
        [Tooltip("Root GameObject of the UI Canvas. This is disabled when the overlay is shown.")]
        [SerializeField] private GameObject _uiObject;

        [Header("Overlay Visuals")]
        [Tooltip("Root GameObject of the overlay Canvas. " +
                 "This is enabled/disabled to show or hide the overlay.")]
        [SerializeField] private GameObject _overlayRoot;

        [Tooltip("Background Image component. Assign your custom background sprite here.")]
        [SerializeField] private Image _backgroundImage;

        [Header("Auto-dismiss (optional)")]
        [Tooltip("If > 0, the overlay closes automatically after this many seconds " +
                 "and loads the title screen. Set to 0 to require manual button press.")]
        [Min(0f)]
        [SerializeField] private float _autoDismissDelay = 0f;

        [Header("Transition")]
        [Tooltip("CanvasGroup for a full-screen fade overlay on scene transition.")]
        [SerializeField] private CanvasGroup _fadeOverlay;

        [Tooltip("Duration of the fade before loading a new scene.")]
        [Min(0.1f)]
        [SerializeField] private float _fadeDuration = 0.5f;

        private bool _isVisible;
        private float _autoDismissTimer;

        private void Awake()
        {
            if (_uiObject == null)
                _uiObject = GameObject.Find("UI");

            if (_overlayRoot != null)
                _overlayRoot.SetActive(false);

            if (_fadeOverlay != null)
                _fadeOverlay.alpha = 0f;
        }

        private void Update()
        {
            if (!_isVisible || _autoDismissDelay <= 0f) return;

            _autoDismissTimer += Time.unscaledDeltaTime;
            if (_autoDismissTimer >= _autoDismissDelay)
                OnReturnToTitle();
        }

        // ── Public API (called by SH_LevelEndTrigger) ────────────────────

        public void Show()
        {
            if (_uiObject != null)
                _uiObject.SetActive(false);

            if (_isVisible) return;
            _isVisible = true;
            _autoDismissTimer = 0f;

            Time.timeScale = 0f;

            if (_overlayRoot != null)
                _overlayRoot.SetActive(true);
        }

        // ── Button Handlers ──────────────────────────────────────────────

        /// <summary> Retry button — reloads the current prototype scene. </summary>
        public void OnRetry()
        {
            Time.timeScale = 1f;
            StartCoroutine(FadeAndLoad(PrototypeSceneName));
        }

        /// <summary> Return to Title button — loads SCN_0_Title. </summary>
        public void OnReturnToTitle()
        {
            Time.timeScale = 1f;
            StartCoroutine(FadeAndLoad(TitleSceneName));
        }

        // ── Internal ─────────────────────────────────────────────────────

        private System.Collections.IEnumerator FadeAndLoad(string sceneName)
        {
            if (_fadeOverlay != null)
            {
                float timer = 0f;
                while (timer < _fadeDuration)
                {
                    timer += Time.unscaledDeltaTime;
                    _fadeOverlay.alpha = Mathf.Clamp01(timer / _fadeDuration);
                    yield return null;
                }
                _fadeOverlay.alpha = 1f;
            }

            SceneManager.LoadScene(sceneName);
        }
    }
}