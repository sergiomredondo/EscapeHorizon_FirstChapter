using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    /// <summary>
    /// Manages the title screen buttons for SCN_0_Title.
    /// Wire each button's OnClick event in the Inspector to the corresponding method.
    /// </summary>
    public class SH_TitleScreenController : MonoBehaviour
    {
        private const string PrototypeSceneName = "SCN_1_Prototype";

        [Header("Transition")]
        [Tooltip("Optional CanvasGroup for a full-screen fade overlay before scene load.")]
        [SerializeField] private CanvasGroup _fadeOverlay;

        [Tooltip("Duration of the fade out before loading the game scene.")]
        [Min(0.1f)]
        [SerializeField] private float _fadeDuration = 0.6f;

        [Header("Gamepad Navigation")]
        [Tooltip("First button to be selected when the title screen opens.")]
        [SerializeField] private UnityEngine.UI.Button _firstSelectedButton;

        private void Awake()
        {
            if (_fadeOverlay != null)
                _fadeOverlay.alpha = 0f;

            // Ensure normal timescale in case we returned from gameplay.
            Time.timeScale = 1f;
        }

        private void Start()
        {
            if (_firstSelectedButton != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_firstSelectedButton.gameObject);
            }
        }

        // ── Button Handlers ──────────────────────────────────────────────

        /// <summary> Start Link button — loads the prototype level. </summary>
        public void OnStartLink()
        {
            if (_fadeOverlay != null)
                StartCoroutine(FadeAndLoad(PrototypeSceneName));
            else
                SceneManager.LoadScene(PrototypeSceneName);
        }

        /// <summary> Reset Connection button — reserved for future new-game logic. </summary>
        public void OnResetConnection()
        {
            // Reserved — no action in prototype.
#if UNITY_EDITOR
            Debug.Log("[SH_TitleScreenController] Reset Connection: not yet implemented.");
#endif
        }

        /// <summary> Parameters button — reserved for future settings screen. </summary>
        public void OnParameters()
        {
            // Reserved — no action in prototype.
#if UNITY_EDITOR
            Debug.Log("[SH_TitleScreenController] Parameters: not yet implemented.");
#endif
            if ( SH_PauseMenuController.Instance != null)
            {
                SH_PauseMenuController.Instance.OpenStandalone();
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_TitleScreenController] SH_PauseMenuController instance not found in the scene.");
#endif
            }
        }

        /// <summary> Disconnection button — exits the application. </summary>
        public void OnDisconnection()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ── Internal ─────────────────────────────────────────────────────

        private System.Collections.IEnumerator FadeAndLoad(string sceneName)
        {
            float timer = 0f;
            while (timer < _fadeDuration)
            {
                timer += Time.deltaTime;
                _fadeOverlay.alpha = Mathf.Clamp01(timer / _fadeDuration);
                yield return null;
            }
            _fadeOverlay.alpha = 1f;
            SceneManager.LoadScene(sceneName);
        }
    }
}