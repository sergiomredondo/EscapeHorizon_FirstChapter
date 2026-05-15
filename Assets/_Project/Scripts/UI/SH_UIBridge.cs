using Core;
using Core.StateMachine;
using Game.Combat.Core;
using Game.Economy;
using Game.Economy.Data;
using Game.Interaction;
using Game.Progression;
using Game.Progression.Data;
using Game.World;
using System;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// The only coupling point between gameplay systems and the UI layer.
    ///
    /// Extended for the Analysis Tree build menu (GDD §5.4.2):
    ///   Polls MenuPressed each frame alongside Surge state.
    ///   Owns Time.timeScale management (0 when menu open, 1 when closed).
    ///   Subscribes to SH_HUDController button events and translates them
    ///   into SH_BuildSystem transactions, then pushes the resulting tree
    ///   snapshot back to SH_UIStateModel.
    ///   SH_BuildMenuController is removed — this bridge absorbs its role.
    ///
    /// Responsibility boundaries:
    ///   OWNS: Subscription lifecycle, initial state push, Surge polling,
    ///         Menu input polling, build menu open/close, build transactions.
    ///   DOES NOT OWN: How values are rendered (SH_HUDController).
    ///   DOES NOT OWN: State values (SH_UIStateModel).
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_UIBridge : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        #region Inspector References

        [Header("UI Layer")]
        [Tooltip("HUD controller on the [UI] GameObject. " +
                 "Assign in Inspector so the model is injected before OnEnable().")]
        [SerializeField] private SH_HUDController _hudController;

        [Header("Optional Override")]
        [Tooltip("Leave unassigned — resolved via FindFirstObjectByType in Start().")]
        [SerializeField] private SH_PlayerStateMachine _playerStateMachine;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime State

        private SH_UIStateModel _model;
        private SH_PlayerContext _context;
        private bool _isInitialized;
        private bool _buildMenuOpen;

        // ── Stored delegate references ─────────────────────────────────────
        private Action<float, float, float> _onDamageReceivedHandler;
        private Action<float, float, float> _onRepairedHandler;
        private Action<ResourceType, float> _onResourceChangedHandler;

        // ── Surge polling cache ────────────────────────────────────────────
        private bool _lastSurgeActive;
        private bool _lastSurgeInCooldown;

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Unity Lifecycle

        private void Awake()
        {
            _model = new SH_UIStateModel();

            if (_hudController == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[SH_UIBridge] SH_HUDController not assigned.");
#endif
                return;
            }

            _hudController.InjectModel(_model);
        }

        private void Start()
        {
            if (_playerStateMachine == null)
                _playerStateMachine = FindFirstObjectByType<SH_PlayerStateMachine>();

            if (_playerStateMachine == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_UIBridge] SH_PlayerStateMachine not found in scene.");
#endif
                return;
            }

            Initialize(_playerStateMachine.GetContext());
        }

        private void Update()
        {
            if (!_isInitialized) return;

            PollSurgeState();
            PollMenuInput();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            // Restore timescale in case the bridge is destroyed while menu is open.
            if (_buildMenuOpen)
                Time.timeScale = 1f;
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Initialization

        private void Initialize(SH_PlayerContext context)
        {
            if (context == null)
            {
#if UNITY_EDITOR
                Debug.LogError("[SH_UIBridge] Initialize: context is null.");
#endif
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
            // ── Health ────────────────────────────────────────────────────────
            _onDamageReceivedHandler = (newDurability, maxDurability, _) =>
                _model.SetHP(newDurability, maxDurability);

            _onRepairedHandler = (newDurability, maxDurability, _) =>
                _model.SetHP(newDurability, maxDurability);

            _context.Health.OnDamageReceived += _onDamageReceivedHandler;
            _context.Health.OnRepaired += _onRepairedHandler;

            // ── Resources ─────────────────────────────────────────────────────
            _onResourceChangedHandler = OnResourceChanged;
            _context.Resources.OnResourceChanged += _onResourceChangedHandler;

            // ── Interaction ───────────────────────────────────────────────────
            _context.Interaction.OnFocusChanged += OnFocusChanged;
            _context.Interaction.OnHoldProgress += _model.SetInteractionProgress;
            _context.Interaction.OnHoldInterrupted += OnInteractionReset;
            _context.Interaction.OnInteractionCompleted += OnInteractionCompleted;

            // ── Build system ──────────────────────────────────────────────────
            if (_context.BuildSystem != null)
            {
                _context.BuildSystem.OnNodeActivated += OnBuildNodeActivated;
                _context.BuildSystem.OnBuildDeactivated += OnBuildDeactivatedHandler;
                _context.BuildSystem.OnActivationFailed += OnBuildActivationFailed;
            }

            // ── HUD controller button events ──────────────────────────────────
            if (_hudController != null)
            {
                _hudController.OnBuildNodePressed += OnHUDBuildNodePressed;
                _hudController.OnBuildReanalysisPressed += OnHUDBuildReanalysisPressed;
                _hudController.OnBuildMenuClosePressed += CloseBuildMenu;
            }
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
                _onResourceChangedHandler = null;
            }

            _context.Interaction.OnFocusChanged -= OnFocusChanged;
            _context.Interaction.OnHoldProgress -= _model.SetInteractionProgress;
            _context.Interaction.OnHoldInterrupted -= OnInteractionReset;
            _context.Interaction.OnInteractionCompleted -= OnInteractionCompleted;

            if (_context.BuildSystem != null)
            {
                _context.BuildSystem.OnNodeActivated -= OnBuildNodeActivated;
                _context.BuildSystem.OnBuildDeactivated -= OnBuildDeactivatedHandler;
                _context.BuildSystem.OnActivationFailed -= OnBuildActivationFailed;
            }

            if (_hudController != null)
            {
                _hudController.OnBuildNodePressed -= OnHUDBuildNodePressed;
                _hudController.OnBuildReanalysisPressed -= OnHUDBuildReanalysisPressed;
                _hudController.OnBuildMenuClosePressed -= CloseBuildMenu;
            }
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Event Handlers — Gameplay

        private void OnResourceChanged(ResourceType type, float newValue)
        {
            switch (type)
            {
                case ResourceType.EnergyCore:
                    float maxEnergy = _context.EconomySettings != null
                        ? _context.EconomySettings.maxEnergy : 0f;
                    _model.SetEnergy(newValue, maxEnergy);
                    break;

                case ResourceType.Scrap:
                    _model.SetScrap(newValue);
                    break;

                case ResourceType.IdentityCore:
                    _model.SetIdentityCores((int)newValue);
                    break;
            }

            // If the menu is open, refresh resource chips on any resource change.
            if (_buildMenuOpen)
                PushBuildTreeState();
        }

        private void OnFocusChanged(IInteractable target)
        {
            bool hasTarget = target != null && target.IsAvailable;
            bool shouldShowUI = false;
            string targetName = string.Empty;

            if (hasTarget)
            {
                var scannable = (target as UnityEngine.MonoBehaviour)
                    ?.GetComponent<SH_ScannableObject>();
                if (scannable != null && scannable.IsRevealed)
                {
                    shouldShowUI = true;
                    targetName = target.ToString();
                }
            }

            _model.SetInteractionFocus(shouldShowUI,
                shouldShowUI ? targetName : string.Empty);
            if (!shouldShowUI) _model.SetInteractionProgress(0f);
        }

        private void OnInteractionReset() => _model.SetInteractionProgress(0f);

        private void OnInteractionCompleted(IInteractable _)
        {
            _model.SetInteractionProgress(0f);
            _model.SetInteractionFocus(false, string.Empty);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Event Handlers — Build System

        private void OnBuildNodeActivated(BuildBranch branch, int count)
        {
            PushBuildTreeState();
        }

        private void OnBuildDeactivatedHandler()
        {
            PushBuildTreeState();
        }

        private void OnBuildActivationFailed(BuildBranch branch, int index)
        {
            // Refresh so the UI reflects the unmet cost without any change.
            PushBuildTreeState();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Event Handlers — HUD Controller Buttons

        private void OnHUDBuildNodePressed(BuildBranch branch, int zeroBasedIndex)
        {
            if (_context?.BuildSystem == null) return;

            bool activated = _context.BuildSystem.TryActivateNextNode(branch);

            if (activated)
            {
                // Show captive memory narrative for the node just activated.
                SH_BuildNodeData node =
                    _context.BuildSystem.GetNode(branch, zeroBasedIndex);

                if (node != null && !string.IsNullOrEmpty(node.captiveMemoryText))
                    _model.SetBuildNarrative(true, node.captiveMemoryText);
                else
                    _model.SetBuildNarrative(false, string.Empty);
            }

            PushBuildTreeState();
        }

        private void OnHUDBuildReanalysisPressed(BuildBranch branch)
        {
            if (_context?.BuildSystem == null) return;
            _context.BuildSystem.TryReanalyze(branch);
            _model.SetBuildNarrative(false, string.Empty);
            PushBuildTreeState();
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Build Menu Open / Close

        public void OpenBuildMenu()
        {
            if (_buildMenuOpen) return;
            _buildMenuOpen = true;

            Time.timeScale = 0f;

            _model.SetBuildNarrative(false, string.Empty);
            PushBuildTreeState();
            _model.SetBuildMenuOpen(true);
        }

        public void CloseBuildMenu()
        {
            if (!_buildMenuOpen) return;
            _buildMenuOpen = false;

            Time.timeScale = 1f;

            _model.SetBuildNarrative(false, string.Empty);
            _model.SetBuildMenuOpen(false);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Build Tree State Push

        private void PushBuildTreeState()
        {
            if (_context?.BuildSystem == null || _model == null) return;

            SH_BuildSystem build = _context.BuildSystem;

            SH_UIStateModel.BuildNodeDisplayData[,] nodeData =
                new SH_UIStateModel.BuildNodeDisplayData[3, 5];

            for (int b = 0; b < 3; b++)
            {
                BuildBranch branch = (BuildBranch)b;

                for (int n = 0; n < 5; n++)
                {
                    SH_BuildNodeData node = build.GetNode(branch, n);

                    SH_UIStateModel.BuildNodeDisplayState state;

                    if (build.HasActiveBuild && branch != build.ActiveBranch)
                    {
                        state = SH_UIStateModel.BuildNodeDisplayState.Unavailable;
                    }
                    else if (n < build.ActiveNodeCount)
                    {
                        state = SH_UIStateModel.BuildNodeDisplayState.Active;
                    }
                    else if (n == build.ActiveNodeCount)
                    {
                        state = SH_UIStateModel.BuildNodeDisplayState.Next;
                    }
                    else
                    {
                        state = SH_UIStateModel.BuildNodeDisplayState.Locked;
                    }

                    string costLabel = string.Empty;
                    if (node != null && state == SH_UIStateModel.BuildNodeDisplayState.Next)
                        costLabel = $"{node.pdCost} PD  /  {node.scrapCost:F0} SC";

                    nodeData[b, n] = new SH_UIStateModel.BuildNodeDisplayData
                    {
                        NodeName = node != null ? node.nodeName : $"Node {n + 1}",
                        CostLabel = costLabel,
                        State = state
                    };
                }
            }

            _model.SetBuildTreeState(
                build.ActiveBranch,
                build.ActiveNodeCount,
                build.HasActiveBuild,
                _context.Resources.AvailableDevelopmentPoints,
                build.GetReanalysisCost(),
                nodeData);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Per-Frame Polling

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
                var scannable = (target as UnityEngine.MonoBehaviour)
                    ?.GetComponent<SH_ScannableObject>();
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

        private void PollMenuInput()
        {
            if (_context?.Input == null) return;

            if (!_context.Input.MenuPressed) return;
            _context.Input.ConsumeMenuPressed();

            if (_buildMenuOpen)
                CloseBuildMenu();
            else
                OpenBuildMenu();
        }

        private void UpdateFocusUIOnStateChange()
        {
            var target = _context?.Interaction.FocusedTarget;
            if (target == null) return;

            var scannable = (target as UnityEngine.MonoBehaviour)
                ?.GetComponent<SH_ScannableObject>();
            if (scannable == null) return;

            OnFocusChanged(target);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Initial State Push

        private void PushInitialState()
        {
            // HP.
            _model.SetHP(
                _context.Health.CurrentDurability,
                _context.Health.MaxDurability);

            // Energy.
            float maxEnergy = _context.EconomySettings != null
                ? _context.EconomySettings.maxEnergy : 0f;
            _model.SetEnergy(_context.Resources.CurrentEnergy, maxEnergy);

            // Scrap.
            _model.SetScrap(_context.Resources.CurrentScrap);

            // Identity Cores.
            _model.SetIdentityCores(_context.Resources.CurrentIdentityCores);

            // Surge.
            _lastSurgeActive = _context.CombatController.IsSurgeActive;
            _lastSurgeInCooldown = _context.CombatController.IsInSurgeCooldown;
            _model.SetSurgeState(_lastSurgeActive, _lastSurgeInCooldown);

            // Build menu — push initial tree state (menu stays closed).
            PushBuildTreeState();
        }

        #endregion
    }
}