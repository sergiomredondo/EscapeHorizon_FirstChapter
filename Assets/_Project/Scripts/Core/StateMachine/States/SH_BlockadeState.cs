using UnityEngine;
using TMPro;

namespace Core.StateMachine.States
{
    public class SH_BlockadeState : SH_BaseState
    {
        public override int Priority => 8;

        #region Phase

        private enum BlockadePhase { Reading, Turning, WalkingAway }
        private BlockadePhase _phase;
        private float _phaseTimer;

        #endregion

        #region Constructor Parameters

        private readonly string _message;
        private readonly float _readDuration;
        private readonly float _fadeDuration;
        private readonly float _walkAwayDuration;
        private readonly GameObject _bubblePrefab;
        private readonly Vector3 _rejectDirection;

        #endregion

        #region Runtime State

        private GameObject _bubbleInstance;
        private CanvasGroup _bubbleCanvasGroup;
        private Quaternion _targetRotation;

        private static readonly Vector3 BubbleOffset = new Vector3(0f, 2.2f, 0f);

        #endregion

        #region Constructor

        public SH_BlockadeState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            string message,
            float readDuration,
            float fadeDuration,
            float walkAwayDuration,
            GameObject bubblePrefab,
            Vector3 rejectDirection)
            : base(context, stateMachine)
        {
            _message = message;
            _readDuration = Mathf.Max(0.5f, readDuration);
            _fadeDuration = Mathf.Max(0.2f, fadeDuration);
            _walkAwayDuration = Mathf.Max(0.1f, walkAwayDuration);
            _bubblePrefab = bubblePrefab;
            _rejectDirection = new Vector3(rejectDirection.x, 0f, rejectDirection.z).normalized;
        }

        #endregion

        #region Lifecycle

        public override void Enter()
        {
            _phase = BlockadePhase.Reading;
            _phaseTimer = 0f;

            // Stop the player immediately.
            _context.Locomotion.SetMovementLock(true);
            _context.Physics.CancelHorizontalVelocity();

            // Reset animator to idle immediately so the walk cycle stops.
            _context.AnimatorBridge?.UpdateMovement(0f);

            _targetRotation = _rejectDirection.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(_rejectDirection)
                : _context.Transform.rotation;

            SpawnBubble();
        }

        public override void Update()
        {
            _phaseTimer += Time.deltaTime;

            switch (_phase)
            {
                case BlockadePhase.Reading: TickReading(); break;
                case BlockadePhase.Turning: TickTurning(); break;
                case BlockadePhase.WalkingAway: TickWalkingAway(); break;
            }

            if (_phase == BlockadePhase.Reading || _phase == BlockadePhase.Turning)
                UpdateBubbleTransform();
        }

        public override void PhysicsUpdate(float dt)
        {
            if (_phase == BlockadePhase.WalkingAway)
            {
                // Apply movement in the rejection direction using the physics motor.
                float speed = _context.Settings.runSpeed * 0.6f;
                Vector3 velocity = _rejectDirection * speed;
                _context.Physics.SetHorizontalVelocity(velocity);
            }

            _context.Physics.Tick(_context.Settings, dt);
        }

        public override void Exit()
        {
            _context.Locomotion.SetMovementLock(false);
            _context.AnimatorBridge?.UpdateMovement(0f);
            DestroyBubble();
        }

        #endregion

        #region Phase Ticks

        private void TickReading()
        {
            // Ensure velocity stays at zero while reading.
            _context.Physics.CancelHorizontalVelocity();
            _context.AnimatorBridge?.UpdateMovement(0f);

            if (_phaseTimer >= _readDuration)
                TransitionToPhase(BlockadePhase.Turning);
        }

        private void TickTurning()
        {
            float t = Mathf.Clamp01(_phaseTimer / _fadeDuration);

            // Auto-rotate toward the rejection direction.
            float degreesPerSecond = Quaternion.Angle(_context.Transform.rotation, _targetRotation)
                                   / Mathf.Max(0.01f, _fadeDuration);
            _context.Transform.rotation = Quaternion.RotateTowards(
                _context.Transform.rotation,
                _targetRotation,
                degreesPerSecond * Time.deltaTime);

            // Fade bubble.
            if (_bubbleCanvasGroup != null)
                _bubbleCanvasGroup.alpha = 1f - t;

            // Keep player stopped during turn.
            _context.Physics.CancelHorizontalVelocity();
            _context.AnimatorBridge?.UpdateMovement(0f);

            if (_phaseTimer >= _fadeDuration)
            {
                DestroyBubble();
                TransitionToPhase(BlockadePhase.WalkingAway);

                // Unlock locomotion only for the walk-away phase so physics applies.
                _context.Locomotion.SetMovementLock(false);
                _context.Transform.rotation = _targetRotation;
            }
        }

        private void TickWalkingAway()
        {
            // Drive animator to show a walk cycle during walk-away.
            float normalizedSpeed = _context.Settings.runSpeed > 0f
                ? (0.6f * _context.Settings.runSpeed) / _context.Settings.runSpeed * 0.5f
                : 0.4f;
            _context.AnimatorBridge?.UpdateMovement(normalizedSpeed);

            if (_phaseTimer >= _walkAwayDuration)
            {
                _context.Physics.CancelHorizontalVelocity();
                _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
            }
        }

        #endregion

        #region Phase Transition

        private void TransitionToPhase(BlockadePhase next)
        {
            _phase = next;
            _phaseTimer = 0f;
        }

        #endregion

        #region Bubble Management

        private void SpawnBubble()
        {
            if (_bubblePrefab == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_BlockadeState] No speech bubble prefab assigned.");
#endif
                return;
            }

            Vector3 spawnPos = GetCameraAlignedPosition();

            _bubbleInstance = UnityEngine.Object.Instantiate(_bubblePrefab, spawnPos, Quaternion.identity);
            _bubbleCanvasGroup = _bubbleInstance.GetComponentInChildren<CanvasGroup>();
            if (_bubbleCanvasGroup != null) _bubbleCanvasGroup.alpha = 1f;

            // Align rotation to camera immediately on spawn.
            if (UnityEngine.Camera.main != null)
                _bubbleInstance.transform.rotation = UnityEngine.Camera.main.transform.rotation;

            // Push sorting order so the bubble renders over most scene geometry.
            var canvas = _bubbleInstance.GetComponentInChildren<Canvas>();
            if (canvas != null) canvas.sortingOrder = 100;

            var tmp = _bubbleInstance.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = _message;
        }

        /// <summary>
        /// Returns a world position along the ray from the camera toward the player's head,
        /// at a comfortable fixed reading distance from the camera. This guarantees the
        /// bubble is always fully visible and correctly oriented regardless of camera angle.
        /// </summary>
        private Vector3 GetCameraAlignedPosition()
        {
            Vector3 headPos = _context.Transform.position + BubbleOffset;

            if (UnityEngine.Camera.main == null) return headPos;

            Vector3 camPos = UnityEngine.Camera.main.transform.position;
            Vector3 toHead = headPos - camPos;
            float fullDist = toHead.magnitude;

            // Place the bubble at 45% of the camera-to-head distance,
            // clamped to a readable range regardless of zoom or camera height.
            float readableDist = Mathf.Clamp(fullDist * 0.55f, 2f, 5f);

            return camPos + toHead.normalized * readableDist;
        }

        private void UpdateBubbleTransform()
        {
            if (_bubbleInstance == null) return;

            _bubbleInstance.transform.position = GetCameraAlignedPosition();

            // Billboard: keep canvas parallel to camera plane so it is always readable.
            if (UnityEngine.Camera.main != null)
                _bubbleInstance.transform.rotation = UnityEngine.Camera.main.transform.rotation;
        }

        private void DestroyBubble()
        {
            if (_bubbleInstance == null) return;
            UnityEngine.Object.Destroy(_bubbleInstance);
            _bubbleInstance = null;
            _bubbleCanvasGroup = null;
        }

        #endregion
    }
}