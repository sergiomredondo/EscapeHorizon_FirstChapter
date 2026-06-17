using Actions.Data;
using Core;
using Core.StateMachine;
using Core.StateMachine.States;
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

        [Header("Action Data — Cooldown Tracking")]
        [Tooltip("Resolved from SH_PlayerCombatController via reflection-free reference. " +
         "Assign the same LightAttack.asset used in SH_PlayerCombatController.")]
        [SerializeField] private SH_ActionData _lightAttackData;

        [Tooltip("Assign the same HeavyAttack.asset used in SH_PlayerCombatController.")]
        [SerializeField] private SH_ActionData _heavyAttackData;

        [Tooltip("Resolved from SH_MovementSettings.dashAction. " +
                 "Assign the same Dash.asset used in SH_MovementSettings.")]
        [SerializeField] private SH_ActionData _dashActionData;

        [Tooltip("Narrative sequencer in the scene. Handles both text log and image sequence channels.")]
        [SerializeField] private Game.World.SH_NarrativeSequencer _narrativeSequencer;
        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Runtime State

        private SH_UIStateModel _model;
        private SH_PlayerContext _context;
        private bool _isInitialized;
        private bool _buildMenuOpen;
        private bool _dataLogOpen;

        // ── Stored delegate references ─────────────────────────────────────
        private Action<float, float, float> _onDamageReceivedHandler;
        private Action<float, float, float> _onRepairedHandler;
        private Action<ResourceType, float> _onResourceChangedHandler;

        // ── Surge polling cache ────────────────────────────────────────────
        private bool _lastSurgeActive;
        private bool _lastSurgeInCooldown;

        // ── Action cooldown polling cache ──────────────────────────────────
        private float _lightAttackCooldownStart = -999f;
        private float _heavyAttackCooldownStart = -999f;
        private float _dashCooldownStart = -999f;

        // Total window = TotalDuration + coolDownTime for each action.
        private float _lightAttackWindow;
        private float _heavyAttackWindow;
        private float _dashWindow;

        // Last known state to detect rising edge (action just started).
        private bool _wasInLightAttack;
        private bool _wasInHeavyAttack;
        private bool _wasInDash;

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
            PollActionCooldowns();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            // Restore timescale in case the bridge is destroyed while menu is open.
            if (_buildMenuOpen)
                Time.timeScale = 1f;

            if (_dataLogOpen)
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
                _hudController.OnPurgePressed += OnHUDPurgePressed;
            }

            // ── Narrative sequencer events ────────────────────────────────────────
            Game.World.SH_NarrativeSequencer.OnTextLogRequested += OnTextLogRequested;
            Game.World.SH_NarrativeSequencer.OnTextLogCloseRequested += OnTextLogCloseRequested;

            if (_hudController != null)
                _hudController.OnDataLogClosePressed += OnTextLogCloseRequested;
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
                _hudController.OnPurgePressed -= OnHUDPurgePressed;
            }

            Game.World.SH_NarrativeSequencer.OnTextLogRequested -= OnTextLogRequested;
            Game.World.SH_NarrativeSequencer.OnTextLogCloseRequested -= OnTextLogCloseRequested;

            if (_hudController != null)
                _hudController.OnDataLogClosePressed -= OnTextLogCloseRequested;
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
                    if (_buildMenuOpen) PushPurgeData();
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

        private void OnTextLogRequested(string title, string source, string body)
        {
            if (_dataLogOpen) return;
            _dataLogOpen = true;
            Time.timeScale = 0f;
            _model.SetDataLog(true, title, source, body);
        }

        private void OnTextLogCloseRequested()
        {
            if (!_dataLogOpen) return;
            _dataLogOpen = false;
            Time.timeScale = 1f;
            _model.SetDataLog(false, string.Empty, string.Empty, string.Empty);
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

        private void OnHUDPurgePressed()
        {
            if (_context?.Resources == null) return;
            if (_context.Resources.CurrentIdentityCores <= 0) return;

            _context.Resources.PurgeCores();

            PushBuildTreeState();
            PushPurgeData();

            _model.SetBuildNarrative(true,
                "Core purified. Development potential unlocked.\n" +
                "Return to the Analysis Tree to apply improvements.");
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        #region Build Menu Open / Close

        public void OpenBuildMenu(bool interactionEnabled = false)
        {
            if (_buildMenuOpen) return;
            _buildMenuOpen = true;

            Time.timeScale = 0f;

            _model.SetBuildMenuInteractionEnabled(interactionEnabled);
            _model.SetBuildNarrative(false, string.Empty);
            PushBuildTreeState();

            bool requirementsNotMet = interactionEnabled && 
                (_context.Resources.CurrentIdentityCores <= 0 || _model.PurgeDPYield <= 0);
            _model.SetPurgeRequirementsNotMet(requirementsNotMet);

            var hudController = _hudController;
            if (hudController != null)
            {
                hudController.TriggerNoticesTimeout();
            }

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

            PushPurgeData();
        }

        private void PushPurgeData()
        {
            if (_context?.Resources == null || _model == null) return;

            int ic = _context.Resources.CurrentIdentityCores;
            bool interactionEnabled = _model.BuildMenuInteractionEnabled;

            // Calculate DP yield using the progression calculator.
            // The calculator is internal to SH_ResourceSystem, so we
            // read the available DP before and after a hypothetical purge
            // by querying the public formula via SH_ProgressionCalculator.
            // Since it is not directly accessible, we expose CalculateDPFromCores
            // through a new public query method on SH_ResourceSystem.
            int dpYield = _context.Resources.CalculatePurgeDPYield();

            _model.SetPurgeData(ic, dpYield, interactionEnabled && ic > 0 && dpYield > 0);
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
            float currentProgress = _context.CombatController.CurrentSurgeProgress;
            float maxProgress = _context.CombatController.MaxSurgeProgress;
            _model.SetSurgeProgress(currentProgress, maxProgress);

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

            bool escapePressed = _context.Input.PausePressed;

            if (_dataLogOpen)
            {
                bool closeLog = escapePressed || _context.Input.InteractPressed;
                if (_context.Input.InteractPressed) _context.Input.ConsumeInteractPressed();
                if (escapePressed) _context.Input.ConsumePausePressed();
                if (closeLog) OnTextLogCloseRequested();
                return;
            }

            if (_buildMenuOpen)
            {
                if (_context.Input.MenuPressed) _context.Input.ConsumeMenuPressed();
                if (escapePressed) _context.Input.ConsumePausePressed();
                if (escapePressed || _context.Input.MenuPressed) CloseBuildMenu();
                return;
            }

            if (escapePressed)
            {
                _context.Input.ConsumePausePressed();
                UI.SH_PauseMenuController.Instance?.TogglePause();
                return;
            }

            if (!_context.Input.MenuPressed) return;
            _context.Input.ConsumeMenuPressed();
            OpenBuildMenu();
        }

        private void PollActionCooldowns()
        {
            if (_context?.StateMachine == null) return;
            if (_context.Resources.CurrentEnergy < _lightAttackData.staminaCost) return;
            string stateName = _context.StateMachine.GetCurrentStateName();

            bool inLight = stateName == nameof(SH_ActionState)
                        && _lightAttackData != null
                        && _context.StateMachine.IsCurrentAction(_lightAttackData);

            bool inHeavy = stateName == nameof(SH_ActionState)
                        && _heavyAttackData != null
                        && _context.StateMachine.IsCurrentAction(_heavyAttackData);

            bool inDash = stateName == nameof(SH_ActionState)
                        && _dashActionData != null
                        && _context.StateMachine.IsCurrentAction(_dashActionData);

            // Rising edge: action just started — record timestamp.
            if (inLight && !_wasInLightAttack)
                _lightAttackCooldownStart = Time.time;
            if (inHeavy && !_wasInHeavyAttack)
                _heavyAttackCooldownStart = Time.time;
            if (inDash && !_wasInDash)
                _dashCooldownStart = Time.time;

            _wasInLightAttack = inLight;
            _wasInHeavyAttack = inHeavy;
            _wasInDash = inDash;

            float light01 = ComputeCooldown01(_lightAttackCooldownStart, _lightAttackWindow);
            float heavy01 = ComputeCooldown01(_heavyAttackCooldownStart, _heavyAttackWindow);
            float dash01 = ComputeCooldown01(_dashCooldownStart, _dashWindow);

            _model.SetActionCooldowns(light01, heavy01, dash01);
        }

        /// <summary>
        /// Returns [0, 1] where 1 = ready, 0 = just started.
        /// Reaches 1 when Time.time >= startTime + window.
        /// </summary>
        private static float ComputeCooldown01(float startTime, float window)
        {
            if (window <= 0f || startTime < 0f) return 1f;
            return Mathf.Clamp01((Time.time - startTime) / window);
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
            _model.SetSurgeProgress(_context.CombatController.CurrentSurgeProgress, _context.CombatController.MaxSurgeProgress);
            // Build menu — push initial tree state (menu stays closed).
            PushBuildTreeState();

            // Action cooldown windows — computed once from asset data.
            if (_lightAttackData != null)
                _lightAttackWindow = _lightAttackData.TotalDuration + _lightAttackData.coolDownTime;
            if (_heavyAttackData != null)
                _heavyAttackWindow = _heavyAttackData.TotalDuration + _heavyAttackData.coolDownTime;
            if (_dashActionData != null)
                _dashWindow = _dashActionData.TotalDuration + _dashActionData.coolDownTime;

            _model.SetActionCooldowns(1f, 1f, 1f);
        }

        #endregion
    }
}