using Game.Enemy;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Terminal state entered when SH_HealthComponent.OnDefeated fires.
    /// Owns the defeat cinematic: triggers Bear's death animation, moves the
    /// camera to a configured cinematic target, waits for the sequence duration,
    /// then resets the player and all enemies back to their initial state.
    ///
    /// Input is fully locked during this state.
    /// Camera lerp uses the main camera directly — no Cinemachine dependency.
    /// </summary>
    public class SH_DeathSequenceState : SH_BaseState
    {
        public override int Priority => 10;

        private readonly string _animationTrigger;
        private readonly float _sequenceDuration;
        private readonly Transform _cameraTarget;
        private readonly Transform _spawnPoint;

        private float _timer;
        private Transform _mainCameraTransform;
        private Vector3 _cameraOriginPosition;
        private Quaternion _cameraOriginRotation;
        private bool _cameraRestored;

        private const string PrototypeSceneName = "SCN_1_Prototype";

        public SH_DeathSequenceState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            string animationTrigger,
            float sequenceDuration,
            Transform cameraTarget,
            Transform spawnPoint)
            : base(context, stateMachine)
        {
            _animationTrigger = animationTrigger;
            _sequenceDuration = sequenceDuration;
            _cameraTarget = cameraTarget;
            _spawnPoint = spawnPoint;
        }

        public override void Enter()
        {
            _timer = 0f;
            _cameraRestored = false;

            // Lock all input by stopping locomotion and locking movement.
            _context.Locomotion.SetMovementLock(true);
            _context.Physics.CancelHorizontalVelocity();

            // Trigger defeat animation on Bear's Animator.
            if (_context.Animator != null && !string.IsNullOrEmpty(_animationTrigger))
                _context.Animator.SetTrigger(_animationTrigger);

            // Store the main camera transform for restoration after the sequence.
            if (UnityEngine.Camera.main != null)
            {
                _mainCameraTransform = UnityEngine.Camera.main.transform;
                _cameraOriginPosition = _mainCameraTransform.position;
                _cameraOriginRotation = _mainCameraTransform.rotation;
            }
        }

        public override void Update()
        {
            _timer += Time.deltaTime;

            // Lerp camera to cinematic target during the first half of the sequence.
            if (_cameraTarget != null && _mainCameraTransform != null)
            {
                float lerpT = Mathf.Clamp01(_timer / (_sequenceDuration * 0.5f));
                _mainCameraTransform.position = Vector3.Lerp(
                    _cameraOriginPosition, _cameraTarget.position, lerpT);
                _mainCameraTransform.rotation = Quaternion.Lerp(
                    _cameraOriginRotation, _cameraTarget.rotation, lerpT);
            }

            if (_timer >= _sequenceDuration)
                ResetGame();
        }

        public override void PhysicsUpdate(float dt)
        {
            // Physics still ticks to maintain gravity grounding, but no locomotion.
            _context.Physics.Tick(_context.Settings, dt);
        }

        public override void Exit()
        {
            // Restore camera to origin if ResetGame did not already do so.
            if (!_cameraRestored && _mainCameraTransform != null)
            {
                _mainCameraTransform.position = _cameraOriginPosition;
                _mainCameraTransform.rotation = _cameraOriginRotation;
            }

            _context.Locomotion.SetMovementLock(false);
        }

        private void ResetGame()
        {
            
            SceneManager.LoadScene(PrototypeSceneName);
            //// Restore camera before transition.
            //if (_mainCameraTransform != null)
            //{
            //    _mainCameraTransform.position = _cameraOriginPosition;
            //    _mainCameraTransform.rotation = _cameraOriginRotation;
            //}
            //_cameraRestored = true;

            //// Reposition player.
            //Vector3 resetPosition = _spawnPoint != null
            //    ? _spawnPoint.position
            //    : Vector3.zero;

            //_context.Transform.position = resetPosition;
            //_context.Physics.CancelHorizontalVelocity();

            //// Restore health and apply defeat resource penalty (already called by
            //// SH_ResourceSystem subscription — only reset health here).
            //_context.Health.ResetToFull();

            //// Reset all enemies in scene.
            //var enemies = Object.FindObjectsByType<SH_EnemyController>(
            //    FindObjectsSortMode.None);
            //foreach (var enemy in enemies)
            //    enemy.ResetEnemy(_context);

            //SH_EnemyController.ResetSharedAlert();

            //_stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
        }
    }
}