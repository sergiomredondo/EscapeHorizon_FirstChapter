using Actions.Data;
using Animation;
using Game.Enemy;
using UnityEngine;

namespace Core.StateMachine.States
{
    /// <summary>
    /// Tactical retreat state triggered when Bear's durability crosses the
    /// retreat threshold defined in SH_EconomySettings (GDD §C3).
    ///
    /// Five sequential phases:
    ///   SlowMotion  — timeScale reduced, retreat animation, camera closes in.
    ///   FadeOut     — screen fades to black (unscaled time).
    ///   Reset       — while black: reposition, reset enemies, restore health,
    ///                 defeat penalty already applied by SH_ResourceSystem subscription.
    ///   FadeIn      — screen fades back in at the safe zone (unscaled time).
    ///   Arrive      — arrival animation plays, timeScale restored, transition to Idle.
    ///
    /// Input is locked for the full duration.
    /// Camera lerp uses the main camera — no Cinemachine dependency.
    /// All fade timing uses Time.unscaledDeltaTime so it is immune to timeScale changes.
    /// </summary>
    public class SH_TacticalRetreatState : SH_BaseState
    {
        public override int Priority => 10;

        #region Phase Enum

        private enum RetreatPhase
        {
            SlowMotion,
            FadeOut,
            Reset,
            FadeIn,
            Arrive
        }

        #endregion

        #region Constructor Parameters

        private readonly string _retreatAnimTrigger;
        private readonly string _arrivalAnimTrigger;
        private readonly float _slowMotionScale;
        private readonly float _slowMotionDuration;
        private readonly float _fadeDuration;
        private readonly float _arrivalDuration;
        private readonly Transform _closeUpCameraTarget;
        private readonly Transform _spawnPoint;
        private readonly CanvasGroup _fadeOverlay;
        private readonly SH_ActionData _retreatActionData;
        private readonly SH_ActionData _arrivalActionData;
        private readonly SH_ActionAnimationMap _retreatAnimationMap;

        #endregion

        #region Runtime State

        private RetreatPhase _phase;
        private float _phaseTimer;

        private Transform _mainCameraTransform;
        private Vector3 _cameraOriginPosition;
        private Quaternion _cameraOriginRotation;
        private Vector3 _cameraToPlayerOffset;
        private bool _callbacksSubscribed;
        private bool _animationPhaseComplete;

        #endregion

        #region Constructor

        /// <param name="context"> Player context. </param>
        /// <param name="stateMachine"> Owning FSM. </param>
        /// <param name="retreatActionData"> Action data for the retreat animation. Optional but recommended for visual polish. </param>
        /// <param name="arrivalActionData"> Action data for the arrival animation. Optional but recommended for visual polish. </param>
        /// <param name="animationMap"> Animation map to resolve clips for the retreatActionData. Optional but required if retreatActionData is provided. </param>
        /// <param name="slowMotionScale"> timeScale during the slow-motion phase. Recommended: 0.25–0.35. </param>
        /// <param name="slowMotionDuration"> Duration of the slow-motion phase in real seconds. </param>
        /// <param name="fadeDuration"> Duration of each fade direction in real seconds. </param>
        /// <param name="arrivalDuration"> Duration of the arrival animation phase in real seconds. </param>
        /// <param name="closeUpCameraTarget"> Transform the camera lerps to during slow-motion. </param>
        /// <param name="spawnPoint"> Safe zone spawn point where Bear reappears after the fade. </param>
        /// <param name="fadeOverlay"> Full-screen CanvasGroup used for the black fade. </param>
        public SH_TacticalRetreatState(
            SH_PlayerContext context,
            SH_PlayerStateMachine stateMachine,
            SH_ActionData retreatActionData,
            SH_ActionData arrivalActionData,
            SH_ActionAnimationMap animationMap,
            float slowMotionScale,
            float slowMotionDuration,
            float fadeDuration,
            float arrivalDuration,
            Transform closeUpCameraTarget,
            Transform spawnPoint,
            CanvasGroup fadeOverlay)
            : base(context, stateMachine)
        {
            _retreatActionData = retreatActionData;
            _arrivalActionData = arrivalActionData;
            _retreatAnimationMap = animationMap;
            _slowMotionScale = Mathf.Clamp(slowMotionScale, 0.1f, 0.9f);
            _slowMotionDuration = Mathf.Max(0.5f, slowMotionDuration);
            _fadeDuration = Mathf.Max(0.2f, fadeDuration);
            _arrivalDuration = Mathf.Max(0.5f, arrivalDuration);
            _closeUpCameraTarget = closeUpCameraTarget;
            _spawnPoint = spawnPoint;
            _fadeOverlay = fadeOverlay;
        }

        #endregion

        #region Lifecycle

        public override void Enter()
        {
            _phase = RetreatPhase.SlowMotion;
            _phaseTimer = 0f;
            _callbacksSubscribed = false;
            _animationPhaseComplete = false;

            _context.Locomotion.SetMovementLock(true);
            _context.Physics.CancelHorizontalVelocity();

            if (UnityEngine.Camera.main != null)
            {
                _mainCameraTransform = UnityEngine.Camera.main.transform;
                _cameraOriginPosition = _mainCameraTransform.position;
                _cameraOriginRotation = _mainCameraTransform.rotation;
                _cameraToPlayerOffset = _mainCameraTransform.position - _context.Transform.position;
            }

            if (_fadeOverlay != null)
            {
                _fadeOverlay.alpha = 0f;
                _fadeOverlay.gameObject.SetActive(true);
                _fadeOverlay.blocksRaycasts = true;
            }

            Time.timeScale = _slowMotionScale;

            // Dispatch retreat animation through the bridge exactly like attacks and surge.
            // OnActiveBegin fires after startupTime — no action needed there.
            // OnActionComplete fires when the clip ends — triggers FadeOut.
            if (_context.AnimatorBridge != null && _retreatActionData != null)
            {
                AnimationClip[] clips = _retreatAnimationMap?.GetClips(_retreatActionData);

                SubscribeToBridgeCallbacks();

                _context.AnimatorBridge.PlayActionClip(
                    clips,
                    _retreatActionData.TotalDuration,
                    _retreatActionData.startupTime,
                    _retreatActionData.activeTime,0);


            }
            else
            {
                // No animation data — go straight to fade after the slowmotion window.
                _animationPhaseComplete = true;
            }
        }

        public override void Update()
        {
            // All phase timers use unscaled time so slow-motion and paused
            // timeScale do not affect the sequence pacing.
            _phaseTimer += Time.unscaledDeltaTime;

            switch (_phase)
            {
                case RetreatPhase.SlowMotion: TickSlowMotion(); break;
                case RetreatPhase.FadeOut: TickFadeOut(); break;
                case RetreatPhase.Reset: TickReset(); break;
                case RetreatPhase.FadeIn: TickFadeIn(); break;
                case RetreatPhase.Arrive: TickArrive(); break;
            }
        }

        public override void PhysicsUpdate(float dt)
        {
            // Gravity still ticks to keep Bear grounded, but no locomotion.
            if (_phase == RetreatPhase.Arrive)
                _context.Physics.Tick(_context.Settings, dt);
        }

        public override void Exit()
        {
            UnsubscribeBridgeCallbacks();
            _context.AnimatorBridge?.StopActionClip();
            Time.timeScale = 1f;

            _context.Locomotion.SetMovementLock(false);
            if (_fadeOverlay != null)
            {
                _fadeOverlay.alpha = 0f;
                _fadeOverlay.gameObject.SetActive(false);
                _fadeOverlay.blocksRaycasts = false;
            }
        }

        #endregion

        #region Animation Bridge Callbacks

        private void SubscribeToBridgeCallbacks()
        {
            if (_callbacksSubscribed || _context.AnimatorBridge == null) return;
            _context.AnimatorBridge.OnActionComplete += HandleRetreatAnimationComplete;
            _callbacksSubscribed = true;
        }

        private void UnsubscribeBridgeCallbacks()
        {
            if (!_callbacksSubscribed || _context.AnimatorBridge == null) return;
            _context.AnimatorBridge.OnActionComplete -= HandleRetreatAnimationComplete;
            _callbacksSubscribed = false;
        }

        private void HandleRetreatAnimationComplete()
        {
            _animationPhaseComplete = true;
            UnsubscribeBridgeCallbacks();
        }

        #endregion
        #region Phase Ticks

        private void TickSlowMotion()
        {
            if (_closeUpCameraTarget != null && _mainCameraTransform != null)
            {
                float t = Mathf.Clamp01(_phaseTimer / _slowMotionDuration);
                _mainCameraTransform.position = Vector3.Lerp(
                    _cameraOriginPosition, _closeUpCameraTarget.position, t);
                _mainCameraTransform.rotation = Quaternion.Lerp(
                    _cameraOriginRotation, _closeUpCameraTarget.rotation, t);
            }

            // Wait for both the slowmotion window to complete
            // before transitioning to the fade. Whichever takes longer wins.
            bool timerDone = _phaseTimer >= _slowMotionDuration;
            if (timerDone) TransitionToPhase(RetreatPhase.FadeOut);
        }

        private void TickFadeOut()
        {
            float t = Mathf.Clamp01(_phaseTimer / _fadeDuration);
            if (_fadeOverlay != null)
                _fadeOverlay.alpha = t;

            if (_phaseTimer >= _fadeDuration)
                TransitionToPhase(RetreatPhase.Reset);
        }

        private void TickReset()
        {
            // Execute all resets while screen is fully black — one frame only.
            ExecuteReset();
            TransitionToPhase(RetreatPhase.FadeIn);
        }

        private void TickFadeIn()
        {
            float t = Mathf.Clamp01(_phaseTimer * 0.5f/ _fadeDuration);
            if (_fadeOverlay != null)
                _fadeOverlay.alpha = 1f - t;

            if (_phaseTimer * 0.5f >= _fadeDuration)
                TransitionToPhase(RetreatPhase.Arrive);
        }

        private void TickArrive()
        {
            if (_phaseTimer >= _arrivalDuration)
                CompleteSequence();
        }

        #endregion

        #region Reset Execution

        private void ExecuteReset()
        {
            // Restore timeScale before repositioning so physics settle correctly.
            Time.timeScale = 1f;

            // Reposition Bear at the safe zone spawn point.
            Vector3 resetPosition = _spawnPoint != null
                ? _spawnPoint.position
                : Vector3.zero;

            _context.Transform.position = resetPosition;
            _context.Physics.CancelHorizontalVelocity();

            // Restore health — defeat penalty was already applied by the
            // SH_ResourceSystem subscription wired in SH_PlayerContext.
            _context.Health.ResetToFull();

            // Deactivate build — PD return to pool, base stats restored.
            _context.BuildSystem?.DeactivateBuild();

            // Play arrival animation if data is provided, using the same bridge mechanism as attacks and surge.
            if (_context.AnimatorBridge != null && _arrivalActionData != null)
            {
                AnimationClip[] arrivalClips = _retreatAnimationMap?.GetClips(_arrivalActionData);
                _context.AnimatorBridge.PlayActionClip(
                    arrivalClips,
                    _arrivalActionData.TotalDuration,
                    _arrivalActionData.startupTime,
                    _arrivalActionData.activeTime);
            }

            // Snap camera to the safe zone using the same isometric offset
            // captured at the start of the retreat, so it is correctly positioned
            // when the screen fades back in.
            if (_mainCameraTransform != null && _spawnPoint != null)
            {
                _mainCameraTransform.position = _spawnPoint.position + _cameraToPlayerOffset;
                _mainCameraTransform.rotation = _cameraOriginRotation;
            }
            else if (_mainCameraTransform != null)
            {
                _mainCameraTransform.position = _cameraOriginPosition;
                _mainCameraTransform.rotation = _cameraOriginRotation;
            }

            // Reset all enemies in scene.
            SH_EnemyController[] enemies =
                UnityEngine.Object.FindObjectsByType<SH_EnemyController>(
                    UnityEngine.FindObjectsSortMode.None);

            foreach (SH_EnemyController enemy in enemies)
                enemy.ResetEnemy(_context);

            SH_EnemyController.ResetSharedAlert();
        }

        #endregion

        #region Phase Transition

        private void TransitionToPhase(RetreatPhase next)
        {
            _phase = next;
            _phaseTimer = 0f;
        }

        #endregion

        #region Sequence Complete

        private void CompleteSequence()
        {
            _stateMachine.ChangeState(new SH_IdleState(_context, _stateMachine));
        }

        #endregion
    }
}