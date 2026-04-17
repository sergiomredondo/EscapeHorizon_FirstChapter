using Core;
using Core.Input;
using Game.Interaction.Data;
using Game.World;
using System;
using UnityEditor.ShaderKeywordFilter;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// Central controller for all world interactions executed by the Mecha Bear.
    /// Detects IInteractable objects in range, manages the hold timer lifecycle,
    /// and dispatches interaction resolution to the focused target.
    ///
    /// Input model (fix vs. original):
    ///   The original design relied on NotifyInteractReleased() arriving every
    ///   frame to cancel a hold. This was fragile because the Unity Input System
    ///   fires Canceled callbacks asynchronously between Update() calls, so a
    ///   release could be missed for one frame and the hold would continue.
    ///
    ///   The corrected model polls SH_InputHandler.InteractHeld directly each
    ///   Tick(). The hold is active only while the button is physically held.
    ///   NotifyInteractPressed() still starts the hold and NotifyInteractReleased()
    ///   still interrupts it, but TickHold() verifies the live flag on every frame
    ///   as the authoritative truth. This makes the hold cancellation frame-perfect
    ///   regardless of callback timing.
    ///
    /// Responsibility boundaries:
    ///   - OWNS: Detection scan, focus selection, hold timer.
    ///   - OWNS: Interruption logic (range break, damage, button release).
    ///   - DOES NOT OWN: Input handling (SH_InputHandler).
    ///   - DOES NOT OWN: Reward delivery (IInteractable.Interact).
    ///   - DOES NOT OWN: Radial bar UI (future HUD consumes OnHoldProgress).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_InteractionController : MonoBehaviour
    {
        #region Dependencies

        private SH_InteractionSettings _settings;
        private SH_PlayerContext _context;
        private SH_InputHandler _inputHandler;
        private bool _isInitialized;

        #endregion

        #region Runtime State

        /// <summary> Currently focused interactable (closest in range). Null if none. </summary>
        private IInteractable _focusedTarget;

        /// <summary> Elapsed hold time for the current hold interaction. </summary>
        private float _holdTimer;

        /// <summary> Whether a hold interaction is currently in progress. </summary>
        private bool _isHolding;

        protected SH_ScannableObject _scannable;

        /// <summary>
        /// Required hold duration for the current target.
        /// Cached on focus change to avoid per-frame settings lookup.
        /// </summary>
        private float _requiredHoldDuration;

        /// <summary>
        /// Buffer list for overlap results. Reused per frame to avoid GC allocation.
        /// </summary>
        private readonly Collider[] _overlapBuffer = new Collider[16];

        #endregion

        #region Events

        /// <summary>
        /// Fired when a new interactable target receives focus (player enters range).
        /// Consumed by: HUD (show interaction prompt).
        /// </summary>
        public event Action<IInteractable> OnTargetFocused;

        /// <summary>
        /// Fired when the focused target is lost (player leaves range or object consumed).
        /// Consumed by: HUD (hide interaction prompt).
        /// </summary>
        public event Action OnTargetLost;

        /// <summary>
        /// Fired every frame during an active hold. Parameter: normalized progress [0,1].
        /// Consumed by: HUD radial progress bar.
        /// </summary>
        public event Action<float> OnHoldProgress;

        /// <summary>
        /// Fired when an active hold is interrupted before completion.
        /// Consumed by: HUD (reset radial bar), audio.
        /// </summary>
        public event Action OnHoldInterrupted;

        /// <summary>
        /// Fired when an interaction resolves successfully (press or hold complete).
        /// Consumed by: audio, analytics.
        /// </summary>
        public event Action<IInteractable> OnInteractionCompleted;

        /// <summary>
        /// Fired when the focused target changes (focus lost or new target gained).
        /// </summary>
        public event Action<IInteractable> OnFocusChanged;
        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerContext during orchestration.
        /// Caches a direct reference to SH_InputHandler so TickHold() can poll
        /// InteractHeld without going through the context property chain each frame.
        /// </summary>
        public void Initialize(SH_InteractionSettings settings, SH_PlayerContext context)
        {
            if (settings == null)
            {
                Debug.LogError($"[SH_InteractionController] Init failed on {gameObject.name}: " +
                               $"settings is null.");
                return;
            }
            if (context == null)
            {
                Debug.LogError($"[SH_InteractionController] Init failed on {gameObject.name}: " +
                               $"context is null.");
                return;
            }
            if (context.Input == null)
            {
                Debug.LogError($"[SH_InteractionController] Init failed on {gameObject.name}: " +
                               $"context.Input is null.");
                return;
            }

            _settings = settings;
            _context = context;
            _inputHandler = context.Input;
            _isInitialized = true;
        }

        #endregion

        #region Public Input API

        /// <summary>
        /// Called by SH_IdleState / SH_MoveState when the interact button is pressed
        /// (InputActionPhase.Started).
        ///
        /// For Press-type targets: resolves immediately.
        /// For Hold-type targets: starts the hold timer.
        ///
        /// This method only starts the hold. Continuation and cancellation are
        /// driven by polling InteractHeld in TickHold() — not by waiting for
        /// NotifyInteractReleased to arrive.
        /// </summary>
        public void NotifyInteractPressed()
        {
            if (!_isInitialized || _focusedTarget == null || !_focusedTarget.IsAvailable)
                return;

            if (_focusedTarget.InteractionType == InteractionType.Press)
            {
                ResolveInteraction(_focusedTarget);
            }
            else // Hold
            {
                _isHolding = true;
                _holdTimer = 0f;
            }
        }

        /// <summary>
        /// Called by SH_IdleState / SH_MoveState when the interact button is released
        /// (InputActionPhase.Canceled).
        ///
        /// Interrupts an in-progress hold immediately. Even though TickHold() also
        /// polls InteractHeld and would catch the release on the next frame, calling
        /// InterruptHold() here gives a same-frame response which is preferable for
        /// feedback timing.
        /// </summary>
        public void NotifyInteractReleased()
        {
            if (_isHolding)
                InterruptHold();
        }

        /// <summary>
        /// Called via SH_PlayerContext when SH_HealthComponent fires OnDamageReceived.
        /// Being hit cancels an in-progress hold (GDD §5.2.1).
        /// </summary>
        public void NotifyDamageReceived()
        {
            if (_isHolding)
                InterruptHold();
        }

        #endregion

        #region Per-Frame Tick

        /// <summary>
        /// Per-frame tick. Called by SH_IdleState and SH_MoveState in their Update().
        /// Runs detection scan, updates focus, and advances the hold timer.
        /// </summary>
        public void Tick()
        {
            if (!_isInitialized) return;

            UpdateFocus();
            TickHold();
        }

        #endregion

        #region Detection

        private void UpdateFocus()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                _settings.detectionRadius,
                _overlapBuffer,
                _settings.interactableLayer);

            IInteractable closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var interactable = _overlapBuffer[i].GetComponent<IInteractable>();
                if (interactable == null || !interactable.IsAvailable) continue;
                
                _scannable = _overlapBuffer[i].GetComponent<SH_ScannableObject>();
                if (_scannable != null)
                {
                    _scannable.AlternateDetection();
                }

                float dist = Vector3.Distance(transform.position, interactable.WorldPosition);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = interactable;
                }
            }

            if (closest != _focusedTarget)
                ChangeFocus(closest);
        }

        private void ChangeFocus(IInteractable newTarget)
        {
            if (_isHolding)
                InterruptHold();

            if (_focusedTarget != null)
            {
                _focusedTarget.OnFocusExit();
                OnTargetLost?.Invoke();
            }

            _focusedTarget = newTarget;
            _requiredHoldDuration = GetRequiredHoldDuration(newTarget);

            if (_focusedTarget != null)
            {
                _focusedTarget.OnFocusEnter();
                OnTargetFocused?.Invoke(_focusedTarget);
                OnFocusChanged?.Invoke(_focusedTarget);
            }
        }

        private float GetRequiredHoldDuration(IInteractable target)
        {
            if (target == null || _settings == null) return 0f;

            if (target is SH_CaptiveCore) return _settings.captiveCoreHoldDuration;
            if (target is SH_ScrapPile) return _settings.scrapPileHoldDuration;

            return _settings.defaultHoldDuration;
        }

        #endregion

        #region Hold Timer

        /// <summary>
        /// Advances the hold timer each frame.
        ///
        /// KEY FIX: At the top of every tick, this method polls _inputHandler.InteractHeld
        /// directly. If the button is no longer held — regardless of whether
        /// NotifyInteractReleased() arrived in time — the hold is immediately interrupted.
        /// This makes release detection frame-perfect and independent of callback timing.
        ///
        /// Secondary checks: range break (if enabled) and damage (via NotifyDamageReceived).
        /// </summary>
        private void TickHold()
        {
            if (!_isHolding || _focusedTarget == null) return;

            // --- Primary guard: poll live button state ---
            // If the physical button is not held this frame, cancel immediately.
            // This is the fix for the bug where releasing early left _isHolding
            // true because the Canceled callback arrived between Update() calls.
            if (!_inputHandler.InteractHeld)
            {
                InterruptHold();
                return;
            }

            // --- Range break ---
            if (_settings.breakOnRangeExit)
            {
                float dist = Vector3.Distance(transform.position, _focusedTarget.WorldPosition);
                float breakThreshold = _settings.detectionRadius + _settings.rangeBreakBuffer;

                if (dist > breakThreshold)
                {
                    InterruptHold();
                    return;
                }
            }

            // --- Advance timer ---
            _holdTimer += Time.deltaTime;

            float progress = Mathf.Clamp01(_holdTimer / _requiredHoldDuration);
            OnHoldProgress?.Invoke(progress);

            if (_holdTimer >= _requiredHoldDuration)
                ResolveInteraction(_focusedTarget);
        }

        private void InterruptHold()
        {
            if (!_isHolding) return;

            _isHolding = false;
            _holdTimer = 0f;

            _focusedTarget?.OnInteractionInterrupted();
            OnHoldInterrupted?.Invoke();
        }

        #endregion

        #region Interaction Resolution

        private void ResolveInteraction(IInteractable target)
        {
            _isHolding = false;
            _holdTimer = 0f;

            target.Interact(_context);
            OnInteractionCompleted?.Invoke(target);

            if (!target.IsAvailable)
                ChangeFocus(null);
        }

        #endregion

        #region Public State Queries

        /// <summary> True if a hold interaction is in progress. </summary>
        public bool IsHolding => _isHolding;

        /// <summary> Normalized hold progress [0,1]. Zero when no hold is active. </summary>
        public float NormalizedHoldProgress =>
            _isHolding && _requiredHoldDuration > 0f
                ? Mathf.Clamp01(_holdTimer / _requiredHoldDuration)
                : 0f;

        /// <summary> The currently focused interactable, or null. </summary>
        public IInteractable FocusedTarget => _focusedTarget;

        #endregion
    }
}