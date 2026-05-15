using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

        private void Awake()
        {
            if (_fadeOverlay != null)
                _fadeOverlay.alpha = 0f;

            // Ensure normal timescale in case we returned from gameplay.
            Time.timeScale = 1f;
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