using Core;
using Core.StateMachine;
using Game.Combat.Core;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction;
using Game.World;
using System;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;

namespace UI
{
    /// <summary>
    /// The only coupling point between gameplay systems and the UI layer.
    /// Subscribes to events from SH_HealthComponent, SH_ResourceSystem, and
    /// SH_PlayerCombatController, then translates those events into setter calls
    /// on the SH_UIStateModel so HUD controllers can update reactively.
    ///
    /// Initialization order:
    ///   1. SH_UIBridge.Awake()     — creates the SH_UIStateModel instance and
    ///                                assigns it to the HUD controller.
    ///   2. SH_PlayerStateMachine.Awake() — builds SH_PlayerContext and wires
    ///                                      all gameplay subsystems.
    ///   3. SH_UIBridge.Start()     — calls Initialize() which subscribes to the
    ///                                now-ready context. Start() always runs after
    ///                                all Awake() calls in the same frame, so the
    ///                                context is guaranteed to exist by then.
    ///
    /// Responsibility boundaries:
    ///   OWNS: Subscription lifecycle (subscribe in Start, unsubscribe in OnDestroy).
    ///   OWNS: Initial state push to the model after subscribing.
    ///   OWNS: Per-frame Surge state polling (no event available from combat controller).
    ///   DOES NOT OWN: How the model values are rendered (SH_HUDController).
    ///   DOES NOT OWN: Any gameplay logic — read-only access to all systems.
    ///
    /// Scene setup:
    ///   Add this component to a dedicated [UI] GameObject alongside SH_HUDController.
    ///   Assign the SH_HUDController reference in the Inspector so Awake() can
    ///   inject the model before OnEnable() fires on that controller.
    ///   Leave _playerStateMachine unassigned; it is resolved automatically via
    ///   FindFirstObjectByType in Start().
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_UIBridge : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        #region Inspector References

        [Header("UI Layer")]

        [Tooltip("The HUD controller that will consume the UIStateModel produced by this bridge. " +
                 "Assign in the Inspector so the model is injected before the controller enables.")]
        [SerializeField] private SH_HUDController _hudController;

        [Header("Optional Override")]

        [Tooltip("Leave unassigned — resolved automatically via FindFirstObjectByType in Start(). " +
                 "Assign manually only for prototype scenes where auto-resolution would be ambiguous.")]
        [SerializeField] private SH_PlayerStateMachine _playerStateMachine;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime State

        /// <summary>
        /// The model owned and populated by this bridge.
        /// Created in Awake() and injected into SH_HUDController before
        /// that controller's OnEnable() fires.
        /// </summary>
        private SH_UIStateModel _model;

        /// <summary>
        /// Cached reference to the player context resolved in Start().
        /// All gameplay event subscriptions are made through this reference.
        /// </summary>
        private SH_PlayerContext _context;

        /// <summary>
        /// Guards Initialize() and Update() against executing before the
        /// context has been successfully resolved and subscriptions set up.
        /// </summary>
        private bool _isInitialized;

        // ─── Stored delegate references ───────────────────────────────────────
        // Storing delegates explicitly allows clean -= unsubscription in OnDestroy.
        // Without stored references, lambda closures registered with += cannot be
        // removed, causing the bridge to retain the context and prevent GC.

        private Action<float, float, float> _onDamageReceivedHandler;
        private Action<float, float, float> _onRepairedHandler;
        private Action<ResourceType, float> _onResourceChangedHandler;

        // ─── Surge polling cache ──────────────────────────────────────────────
        // SH_PlayerCombatController does not expose events for Surge state changes.
        // We poll the two bool properties each frame and push to the model only
        // when a transition is detected, keeping the model's change guard effective.

        private bool _lastSurgeActive;
        private bool _lastSurgeInCooldown;


        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle

        private void Awake()
        {
            // Create the model before any controller calls OnEnable().
            // This guarantees that when SH_HUDController subscribes in its own
            // OnEnable(), the model instance already exists.
            _model = new SH_UIStateModel();

            if (_hudController == null)
            {
                Debug.LogWarning("[SH_UIBridge] SH_HUDController reference is not assigned. " +
                                 "Assign it in the Inspector so the model can be injected " +
                                 "before the controller subscribes in OnEnable().");
                return;
            }
            _hudController.InjectModel(_model);
        }

        private void Start()
        {
            // Resolve the StateMachine if not manually assigned.
            if (_playerStateMachine == null)
                _playerStateMachine = FindFirstObjectByType<SH_PlayerStateMachine>();

            if (_playerStateMachine == null)
            {
                Debug.LogError("[SH_UIBridge] SH_PlayerStateMachine not found in scene. " +
                               "Add the component to the Bear GameObject or assign it manually.");
                return;
            }

            // The context is built inside SH_PlayerStateMachine.Awake(), which has
            // already completed by the time Start() runs. Accessing it here is safe.
            Initialize(_playerStateMachine.GetContext());
        }

        private void Update()
        {
            if (!_isInitialized) return;

            PollSurgeState();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Initialization

        /// <summary>
        /// Wires all gameplay event subscriptions and performs the initial state push.
        /// Called from Start() once the context is confirmed to exist.
        /// </summary>
        /// <param name="context">
        /// The player context built by SH_PlayerStateMachine. Must not be null.
        /// </param>
        private void Initialize(SH_PlayerContext context)
        {
            if (context == null)
            {
                Debug.LogError("[SH_UIBridge] Initialize: context is null. " +
                               "Ensure SH_PlayerStateMachine.Awake() has run and GetContext() " +
                               "returns a valid SH_PlayerContext before Start() fires.");
                return;
            }

            _context = context;

            Subscribe();
            PushInitialState();

            _isInitialized = true;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Subscription Management

        private void Subscribe()
        {
            // ─── Health ───────────────────────────────────────────────────────
            // OnDamageReceived: (newDurability, maxDurability, damageTaken)
            _onDamageReceivedHandler = (newDurability, maxDurability, _) =>
                _model.SetHP(newDurability, maxDurability);

            _context.Health.OnDamageReceived += _onDamageReceivedHandler;

            // OnRepaired: (newDurability, maxDurability, amountRepaired)
            _onRepairedHandler = (newDurability, maxDurability, _) =>
                _model.SetHP(newDurability, maxDurability);

            _context.Health.OnRepaired += _onRepairedHandler;

            // ─── Resources ────────────────────────────────────────────────────
            // OnResourceChanged: (ResourceType type, float newValue)
            // A single handler routes all three resource types to their
            // dedicated model setters with a type-switch, avoiding three
            // separate lambda allocations.
            _onResourceChangedHandler = OnResourceChanged;
            _context.Resources.OnResourceChanged += _onResourceChangedHandler;
            _context.Interaction.OnFocusChanged += OnFocusChanged;
            _context.Interaction.OnHoldProgress += (progress) =>
            {
                var target = _context.Interaction.FocusedTarget;
                var scannable = (target as MonoBehaviour)?.GetComponent<SH_ScannableObject>();

                if (scannable != null && scannable.IsRevealed)
                {
                    _model.SetInteractionProgress(progress);
                }
                else
                {
                    _model.SetInteractionProgress(0f);
                }
            };
            _context.Interaction.OnHoldInterrupted += OnInteractionReset;
            _context.Interaction.OnInteractionCompleted += OnInteractionCompleted;
        }

        private void Unsubscribe()
        {
            if (_context == null) return;

            if (_onDamageReceivedHandler != null)
            {
                _context.Health.OnDamageReceived -= _onDamageReceivedHandler;
                _context.Health.OnRepaired -= _onRepairedHandler;
                _onDamageReceivedHandler = null;
                _onRepairedHandler = null;
            }

            if (_onResourceChangedHandler != null)
            {
                _context.Resources.OnResourceChanged -= _onResourceChangedHandler;
                _context.Interaction.OnFocusChanged -= OnFocusChanged;
                _context.Interaction.OnHoldProgress -= _model.SetInteractionProgress;
                _context.Interaction.OnHoldInterrupted -= OnInteractionReset;
                _context.Interaction.OnInteractionCompleted -= OnInteractionCompleted;
                _onResourceChangedHandler = null;
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Event Handlers

        /// <summary>
        /// Routes incoming resource change notifications to the correct model setter.
        /// Called by SH_ResourceSystem.OnResourceChanged for all three resource types.
        /// The switch avoids per-type delegate allocations and keeps the routing logic
        /// in one readable place.
        /// </summary>
        private void OnResourceChanged(ResourceType type, float newValue)
        {
            switch (type)
            {
                case ResourceType.EnergyCore:
                    // Energy max comes from the settings asset, which never changes at runtime.
                    // Reading it here on every regen tick (multiple times per second) is safe
                    // because ScriptableObject field access has negligible cost.
                    float maxEnergy = _context.EconomySettings != null
                        ? _context.EconomySettings.maxEnergy
                        : 0f;
                    _model.SetEnergy(newValue, maxEnergy);
                    break;

                case ResourceType.Scrap:
                    _model.SetScrap(newValue);
                    break;

                case ResourceType.IdentityCore:
                    // SH_ResourceSystem fires newValue as float for IC despite the
                    // internal counter being an int (cast for interface uniformity).
                    // We cast back to int here for the model, which stores it correctly.
                    _model.SetIdentityCores((int)newValue);
                    break;
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Per-Frame Polling

        /// <summary>
        /// Polls SH_PlayerCombatController for Surge state changes each frame.
        /// Pushes to the model only when a transition is detected so the model's
        /// internal equality guard prevents redundant event firing.
        ///
        /// This polling approach is used because SH_PlayerCombatController exposes
        /// IsSurgeActive and IsInSurgeCooldown as read-only properties without
        /// dedicated change events. Adding events to that system is deferred to
        /// a later stage when the Surge bar accumulation system is implemented.
        /// </summary>
        private void PollSurgeState()
        {
            UpdateFocusUIOnStateChange();

            if (_context?.CombatController == null) return;

            bool currentSurgeActive = _context.CombatController.IsSurgeActive;
            bool currentSurgeInCooldown = _context.CombatController.IsInSurgeCooldown;

            if (currentSurgeActive != _lastSurgeActive ||
                currentSurgeInCooldown != _lastSurgeInCooldown)
            {
                _lastSurgeActive = currentSurgeActive;
                _lastSurgeInCooldown = currentSurgeInCooldown;
                _model.SetSurgeState(currentSurgeActive, currentSurgeInCooldown);
            }

            var target = _context.Interaction.FocusedTarget;
            if (target != null)
            {
                var scannable = (target as MonoBehaviour)?.GetComponent<SH_ScannableObject>();
                bool isRevealed = scannable != null && scannable.IsRevealed;

                if (isRevealed)
                {
                    _model.SetInteractionFocus(true, target.ToString());
                    _model.UpdateTargetPosition(target.WorldPosition);
                }
                else
                {
                    _model.SetInteractionFocus(false, string.Empty);
                }
            }
            else
            {
                _model.SetInteractionFocus(false, string.Empty);
                _model.SetInteractionProgress(0f);
            }
        }

        private void UpdateFocusUIOnStateChange()
        {
            var target = _context?.Interaction.FocusedTarget;
            if (target == null) return;

            var scannable = (target as MonoBehaviour)?.GetComponent<SH_ScannableObject>();
            if (scannable == null) return;

            OnFocusChanged(target);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Initial State Push

        /// <summary>
        /// Pushes the current gameplay state to the model immediately after subscribing.
        /// Without this, the HUD would show zeroed values until the first event fires,
        /// which could be several seconds into gameplay if the player does not take
        /// damage or collect resources immediately.
        /// </summary>
        private void PushInitialState()
        {
            // HP
            _model.SetHP(
                _context.Health.CurrentDurability,
                _context.Health.MaxDurability);

            // Energy
            float maxEnergy = _context.EconomySettings != null
                ? _context.EconomySettings.maxEnergy
                : 0f;
            _model.SetEnergy(_context.Resources.CurrentEnergy, maxEnergy);

            // Scrap
            _model.SetScrap(_context.Resources.CurrentScrap);

            // Identity Cores
            _model.SetIdentityCores(_context.Resources.CurrentIdentityCores);

            // Surge (cache initial values to avoid a false-positive on first PollSurgeState)
            _lastSurgeActive = _context.CombatController.IsSurgeActive;
            _lastSurgeInCooldown = _context.CombatController.IsInSurgeCooldown;
            _model.SetSurgeState(_lastSurgeActive, _lastSurgeInCooldown);
        }

        #endregion

        #region Interaction Event Handlers

        private void OnFocusChanged(IInteractable target)
        {
            bool hasTarget = target != null && target.IsAvailable;
            bool shouldShowUI = false;
            string targetName = string.Empty;

            if (hasTarget)
            {
                var scannable = (target as MonoBehaviour)?.GetComponent<SH_ScannableObject>();
                if (scannable != null && scannable.IsRevealed)
                {
                    shouldShowUI = true;
                    targetName = target.ToString();
                }
            }

            _model.SetInteractionFocus(shouldShowUI, shouldShowUI ? targetName : string.Empty);
            if (!shouldShowUI) _model.SetInteractionProgress(0f);
        }

        private void OnInteractionReset()
        {
            _model.SetInteractionProgress(0f);
        }

        private void OnInteractionCompleted(IInteractable target)
        {
            _model.SetInteractionProgress(0f);
            _model.SetInteractionFocus(false, string.Empty);
        }

        #endregion
    }
}