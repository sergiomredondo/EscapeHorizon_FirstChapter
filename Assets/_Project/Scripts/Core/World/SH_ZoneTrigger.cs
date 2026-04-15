using Core;
using Core.StateMachine;
using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Volume trigger that advances the difficulty zone when the player enters.
    /// Requires a BoxCollider with Is Trigger enabled on this GameObject.
    /// Optionally fades the screen via a CanvasGroup overlay during the transition.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SH_ZoneTrigger : MonoBehaviour
    {
        [Header("Zone Configuration")]
        [Tooltip("Zone index to report to SH_DifficultyManager on player entry. " +
                 "GDD §5.3.6: higher zones scale enemy HP and attack values.")]
        [Min(1)]
        [SerializeField] private int _zoneIndex = 2;

        [Tooltip("Player tag used to identify the player on trigger enter.")]
        [SerializeField] private string _playerTag = "Player";

        [Header("Screen Transition (Optional)")]
        [Tooltip("CanvasGroup of a full-screen black overlay for fade in/out. " +
                 "Leave empty to skip the screen transition.")]
        [SerializeField] private CanvasGroup _fadeOverlay;

        [Tooltip("Duration in seconds of each fade direction (in and out).")]
        [Min(0.1f)]
        [SerializeField] private float _fadeDuration = 0.5f;

        private bool _triggered = false;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            if (_fadeOverlay != null)
                _fadeOverlay.alpha = 0f;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered) return;
            if (!other.CompareTag(_playerTag)) return;

            _triggered = true;

            SH_PlayerStateMachine playerFSM =
                other.GetComponentInParent<Core.StateMachine.SH_PlayerStateMachine>();

            if (playerFSM == null)
            {
                Debug.LogWarning($"[SH_ZoneTrigger] No SH_PlayerStateMachine found on " +
                                 $"'{other.gameObject.name}'. Zone change not applied.");
                return;
            }

            // Access DifficultyManager through the player context via the FSM.
            // SH_PlayerStateMachine exposes GetCurrentStateName but not the context directly.
            // We use FindObjectsByType to locate the DifficultyManager — acceptable for
            // a zone trigger that fires infrequently.
            var difficultyManager = UnityEngine.Object.FindFirstObjectByType
                <Game.Combat.Core.SH_DifficultyManager>();

            if (difficultyManager != null)
                difficultyManager.NotifyZoneEntered(_zoneIndex);

            if (_fadeOverlay != null)
                StartCoroutine(FadeSequence());
        }

        private System.Collections.IEnumerator FadeSequence()
        {
            // Fade to black.
            float t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.deltaTime;
                _fadeOverlay.alpha = Mathf.Clamp01(t / _fadeDuration);
                yield return null;
            }

            _fadeOverlay.alpha = 1f;
            yield return new WaitForSeconds(0.1f);

            // Fade back in.
            t = 0f;
            while (t < _fadeDuration)
            {
                t += Time.deltaTime;
                _fadeOverlay.alpha = 1f - Mathf.Clamp01(t / _fadeDuration);
                yield return null;
            }

            _fadeOverlay.alpha = 0f;
        }

        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (col is BoxCollider box)
                Gizmos.DrawCube(box.center, box.size);

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
            if (col is BoxCollider boxWire)
                Gizmos.DrawWireCube(boxWire.center, boxWire.size);
        }
    }
}