using Core.StateMachine;
using Core.StateMachine.States;
using UnityEngine;

namespace Game.World
{
    [RequireComponent(typeof(Collider))]
    public class SH_BlockadeTrigger : MonoBehaviour
    {
        #region Inspector

        [Header("Message")]
        [TextArea(2, 4)]
        [SerializeField] private string _message = "We can't go that way.";

        [Header("Timing")]
        [Tooltip("Seconds the speech bubble remains fully visible.")]
        [Min(0.5f)]
        [SerializeField] private float _readDuration = 2.5f;

        [Tooltip("Seconds for the auto-rotation and bubble fade.")]
        [Min(0.2f)]
        [SerializeField] private float _fadeDuration = 0.8f;

        [Tooltip("Seconds Bear walks away in the rejection direction after turning.")]
        [Min(0.1f)]
        [SerializeField] private float _walkAwayDuration = 1.2f;

        [Tooltip("Seconds before this trigger can fire again.")]
        [Min(0f)]
        [SerializeField] private float _triggerCooldown = 6f;

        [Header("Presentation")]
        [Tooltip("World-space speech bubble prefab with CanvasGroup and TextMeshProUGUI.")]
        [SerializeField] private GameObject _speechBubblePrefab;

        [Header("Detection")]
        [SerializeField] private string _playerTag = "Player";

        #endregion

        #region Runtime State

        private float _cooldownTimer;
        private bool _onCooldown;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void Update()
        {
            if (!_onCooldown) return;
            _cooldownTimer -= Time.deltaTime;
            if (_cooldownTimer <= 0f) _onCooldown = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_onCooldown) return;
            if (!other.CompareTag(_playerTag)) return;

            SH_PlayerStateMachine fsm =
                other.GetComponentInParent<SH_PlayerStateMachine>();
            if (fsm == null) return;

            Vector3 rejectDir = other.transform.position - transform.position;
            rejectDir.y = 0f;

            bool accepted = fsm.RequestBlockade(
                _message,
                _readDuration,
                _fadeDuration,
                _walkAwayDuration,
                _speechBubblePrefab,
                rejectDir);

            if (accepted)
            {
                _onCooldown = true;
                _cooldownTimer = _triggerCooldown;
            }
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.8f, 0.2f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (col is BoxCollider box) Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(1f, 0.3f, 0.8f, 0.7f);
            if (col is BoxCollider boxWire) Gizmos.DrawWireCube(boxWire.center, boxWire.size);
        }

        #endregion
    }
}