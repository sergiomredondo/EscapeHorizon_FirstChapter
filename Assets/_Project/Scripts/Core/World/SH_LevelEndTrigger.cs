using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Volume trigger that marks the end of the level.
    /// When the player enters, activates SH_LevelCompleteOverlay.
    ///
    /// Setup: add a BoxCollider (Is Trigger: true) to this GameObject.
    /// Assign the SH_LevelCompleteOverlay reference in the Inspector.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SH_LevelEndTrigger : MonoBehaviour
    {
        [Tooltip("The level complete overlay to activate when the player reaches this trigger.")]
        [SerializeField] private Game.UI.SH_LevelCompleteOverlay _overlay;

        [Tooltip("Player tag used to identify the player on trigger enter.")]
        [SerializeField] private string _playerTag = "Player";

        private bool _triggered;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered) return;
            if (!other.CompareTag(_playerTag)) return;

            _triggered = true;

            if (_overlay != null)
                _overlay.Show();
            else
#if UNITY_EDITOR
                Debug.LogWarning("[SH_LevelEndTrigger] No SH_LevelCompleteOverlay assigned.");
#endif
        }

        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (col is BoxCollider box)
                Gizmos.DrawCube(box.center, box.size);

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            if (col is BoxCollider boxWire)
                Gizmos.DrawWireCube(boxWire.center, boxWire.size);
        }
    }
}