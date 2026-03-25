using Core;
using Core.Physics;
using Core.StateMachine;
using Data;
using Game.Economy;
using Game.Economy.Data;
using Game.Economy.Progression;
using Game.Interaction;
using Game.Interaction.Data;
using System.Collections.Generic;
using UnityEngine;
using Game.Combat.Core;
using Game.Combat.Data;

namespace DebugTools
{
    /// <summary>
    /// Realtime telemetry overlay for Escape Horizon development.
    /// Renders panels in the Game View and vector gizmos in the Scene View.
    ///
    /// PANELS:
    ///   1. NEWTONIAN    — velocity, forces, friction ramp, kinetic energy.
    ///   2. ANIMATION    — blend tree, transitions, Animator parameters.
    ///   3. ECONOMY      — durability, resources, progression, modifiers, events.
    ///   4. INTERACTION  — controller state, focused target, hold timer, input flags.
    ///   5. PERCEPTION   — all IInteractable objects in range with distance and type.
    ///   6. COMBAT        — active target, combat stats, damage modifiers.
    ///
    /// GIZMOS (Scene View):
    ///   - Current velocity vector (cyan)
    ///   - Last applied force (orange)
    ///   - Effective gravity (magenta)
    ///   - Interaction detection radius (wire sphere, green/blue)
    ///   - Range-break buffer sphere (yellow, when breakOnRangeExit is true)
    ///   - Line to focused target (bright green)
    ///   - Lines to all in-range objects (dim blue)
    ///   - Hold progress arc around focused target (yellow dots)
    /// </summary>
    [DisallowMultipleComponent]
    public class SH_Debugger : MonoBehaviour
    {
        #region Dependencies

        private SH_PhysicsMotor _physics;
        private SH_PlayerStateMachine _stateMachine;
        private SH_MovementSettings _settings;
        private SH_PlayerContext _context;

        // Economic
        private SH_ResourceSystem _resources;
        private SH_HealthComponent _health;
        private SH_EconomicEventManager _economicEvents;
        private SH_EconomySettings _economySettings;

        // Interaction
        private SH_InteractionController _interaction;
        private SH_InteractionSettings _interactionSettings;

        // Combat
        private SH_PlayerCombatController _combatController;
        private SH_CombatStats _playerCombatStats;
        private SH_CombatSettings _combatSettings;
        private SH_EnergySurgeSystem _surgeSystem;
        private SH_DifficultyManager _difficultyManager;

        #endregion

        #region Serialized Visualization Settings

        [Header("Panel Visibility")]

        [Tooltip("Newtonian physics telemetry: velocity, forces, friction ramp, kinetic energy.")]
        [SerializeField] private bool showPhysicsPanel = true;

        [Tooltip("Animator telemetry: blend tree, transitions, clip state.")]
        [SerializeField] private bool showAnimationPanel = true;

        [Tooltip("Economy telemetry: resources, progression, modifiers, active events.")]
        [SerializeField] private bool showEconomyPanel = true;

        [Tooltip("Interaction system telemetry: hold state, focused target, input flags.")]
        [SerializeField] private bool showInteractionPanel = true;

        [Tooltip("Perception panel: all IInteractable objects detected within detection radius.")]
        [SerializeField] private bool showPerceptionPanel = true;

        [Tooltip("Combat telemetry: active target, combat stats, damage modifiers.")]
        [SerializeField] private bool showCombatPanel = true;

        [Tooltip("Show focused target and all in-range IInteractables in the Scene View.")]
        [SerializeField] private bool showEnemyPanel = true;

        [Tooltip("Show Energy Surge status in the Combat panel.")]
        [SerializeField] private bool showSurgePanel = true;

        [Tooltip("Draw velocity, force, and interaction radius gizmos in the Scene View.")]
        [SerializeField] private bool showGizmos = true;

        #endregion

        #region Configuration

        [Header("Physics Configuration")]

        [Tooltip("Max-speed multiplier threshold above which a stability warning fires. " +
                 "1.2 = allows 20% overshoot before warning.")]
        [Range(1f, 2f)]
        [SerializeField] private float stabilityThreshold = 1.2f;

        [Header("Layout Configuration")]

        [Tooltip("Column 1 (left) starting X position.")]
        [SerializeField] private float col1X = 25f;

        [Tooltip("Column 2 (right) starting X position.")]
        [SerializeField] private float col2X = 465f;

        [Tooltip("Column 2 (right) starting X position.")]
        [SerializeField] private float col3X = 905f;

        [Tooltip("Starting Y position for both columns.")]
        [SerializeField] private float startY = 25f;

        [Tooltip("Width of each telemetry panel.")]
        [SerializeField] private float panelWidth = 430f;

        [Tooltip("Vertical margin between stacked panels.")]
        [SerializeField] private float panelMargin = 8f;

        private const float LabelWidth = 220f;

        #endregion

        #region GUI Styles

        private GUIStyle _style;
        private GUIStyle _headerStyle;
        private GUIStyle _subheaderStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _positiveStyle;
        private GUIStyle _dimStyle;
        private GUIStyle _activeStyle;
        private bool _stylesInitialized;

        #endregion

        #region Perception Cache

        // Reusable buffer for the per-frame perception overlap scan.
        // Updated in Update() so OnGUI() and OnDrawGizmos() share consistent data
        // without issuing Physics calls from those callbacks.
        private readonly Collider[] _perceptionBuffer = new Collider[24];
        private readonly List<(IInteractable obj, float dist)> _perceivedObjects
            = new List<(IInteractable, float)>(16);
        private int _perceivedCount;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _stateMachine = GetComponent<SH_PlayerStateMachine>();
            if (_stateMachine == null)
                Debug.LogWarning($"[SH_Debugger] SH_PlayerStateMachine not found on {gameObject.name}.");
        }

        private void Update()
        {
            if (_interaction == null || _interactionSettings == null) return;
            UpdatePerceptionCache();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization.
        /// Called by SH_PlayerStateMachine in Awake() after the context is constructed.
        /// </summary>
        public void Initialize(SH_PlayerContext context)
        {
            if (context == null)
            {
                Debug.LogWarning($"[SH_Debugger] Initialize called with null context on " +
                                 $"{gameObject.name}. Telemetry unavailable.");
                return;
            }

            _context = context;
            _physics = context.Physics;
            _settings = context.Settings;
            _resources = context.Resources;
            _health = context.Health;
            _economicEvents = context.EconomicEvents;
            _economySettings = context.EconomySettings;
            _interaction = context.Interaction;
            _interactionSettings = context.InteractionSettings;
            _combatController = context.CombatController;
            _playerCombatStats = context.PlayerCombatStats;
            _combatSettings = context.CombatSettings;
            _surgeSystem = context.SurgeSystem;
            _difficultyManager = context.DifficultyManager;
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _style = new GUIStyle
            {
                fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
            _headerStyle = new GUIStyle
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.4f, 0.9f, 1f) }
            };
            _subheaderStyle = new GUIStyle
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };
            _warningStyle = new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.3f, 0.3f) }
            };
            _positiveStyle = new GUIStyle
            {
                fontSize = 12,
                normal = { textColor = new Color(0.3f, 1f, 0.5f) }
            };
            _dimStyle = new GUIStyle
            {
                fontSize = 12,
                normal = { textColor = new Color(0.45f, 0.45f, 0.45f) }
            };
            _activeStyle = new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.2f) }
            };

            _stylesInitialized = true;
        }

        #endregion

        #region OnGUI — Layout

        private void OnGUI()
        {
            if (_context == null) return;
            EnsureStyles();

            // 1st column: panels 1, 2, 3
            float col1Y = startY;

            if (showPhysicsPanel)
            {
                float h = DrawPhysicsPanel(col1X, col1Y);
                col1Y += h + panelMargin;
            }
            if (showAnimationPanel)
            {
                float h = DrawAnimationPanel(col1X, col1Y);
                col1Y += h + panelMargin;
            }
            if (showEconomyPanel && _resources != null && _health != null)
            {
                DrawEconomyPanel(col1X, col1Y);
            }

            // 2nd column: panels 4, 5
            float col2Y = startY;

            if (showInteractionPanel && _interaction != null)
            {
                float h = DrawInteractionPanel(col2X, col2Y);
                col2Y += h + panelMargin;
            }
            if (showPerceptionPanel && _interaction != null)
            {
                DrawPerceptionPanel(col2X, col2Y);
            }

            // 3rd column: panels 4, 5
            float col3Y = startY;

            if (showCombatPanel)
            {
                float h = DrawCombatPanel(col3X, col3Y);
                col3Y += h + panelMargin;
            }
            if (showEnemyPanel)
            {
                float h = DrawEnemyPanel(col3X, col3Y);
                col3Y += h + panelMargin;
            }
            if (showSurgePanel)
            {
                DrawSurgeAndDifficultyPanel(col3X, col3Y);
            }

        }

        #endregion

        #region Panel 1 — Newtonian Telemetry

        private float DrawPhysicsPanel(float x, float y)
        {

            if (_physics == null || _settings == null) return 0f;

            Vector3 vel = _physics.CurrentVelocity;
            Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);
            float horizSpeed = horizVel.magnitude;
            float vertSpeed = Mathf.Abs(vel.y);
            float kineticEnergy = 0.5f * _settings.mass * vel.sqrMagnitude;
            float frictionForce = _settings.muK * _settings.mass * Mathf.Abs(_settings.gravity);
            float gravForce = _settings.mass * Mathf.Abs(_settings.gravity);
            bool overshooting = horizSpeed > _settings.maxSpeed * stabilityThreshold;
            float speedRatio = _settings.maxSpeed > 0f ? horizSpeed / _settings.maxSpeed : 0f;

            // Net force estimate: acceleration ≈ F / m (approximated from HasActiveForce).
            bool hasForce = _physics.HasActiveForce;
            Vector3 lastForce = hasForce ? _physics.LastAppliedForce : Vector3.zero;
            float forceMag = lastForce.magnitude;

            // Friction ramp state — reads the live multiplier from SH_PhysicsMotor.
            // Values above 1 indicate SH_MoveState is still smoothing down from a
            // post-action high-friction state (e.g., multiplier = 5 after a dash).
            float frictionMul = _physics.frictionMultiplier;
            bool rampActive = frictionMul > 1.01f;
            float accelTime = _settings.accelerationTime;

            float panelH = 340f;
            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH), GUI.skin.box);

            GUILayout.Label("NEWTONIAN TELEMETRY", _headerStyle);
            GUILayout.Space(4);

            // --- State & grounding ---
            string stateName = _stateMachine != null
                ? _stateMachine.GetCurrentStateName() : "N/A";
            bool grounded = GetComponent<CharacterController>()?.isGrounded ?? false;

            DrawStat("FSM state", stateName,
                     Color.white);
            DrawStat("Ground", grounded ? "Grounded" : "Airborne",
                     grounded ? Color.green : Color.yellow);

            Separator();

            // --- Velocities ---
            DrawStat("Full velocity", vel.ToString("F2") + " m/s",
                     Color.white);
            DrawStat("Horizontal speed", $"{horizSpeed:F3} m/s",
                     overshooting ? Color.red
                     : horizSpeed > _settings.walkSpeed ? new Color(1f, 0.8f, 0.2f)
                     : Color.green);
            DrawStat("Vertical speed", $"{vertSpeed:F3} m/s",
                     vertSpeed > 0.1f ? Color.yellow : Color.gray);
            DrawStat("Speed / vMax", $"{speedRatio:P1}",
                     speedRatio > 1f ? Color.red : Color.white);
            DrawStat("Horiz. direction", horizVel.normalized.ToString("F2"),
                     Color.white);

            Separator();

            // --- Forces & energy ---
            DrawStat("Mass (m)", $"{_settings.mass:F1} kg",
                     Color.white);
            DrawStat("Gravity force", $"{gravForce:F1} N  (g={_settings.gravity:F1})",
                     Color.white);
            DrawStat("Kinetic friction", $"{frictionForce:F1} N  (μK={_settings.muK:F2})",
                     Color.white);
            DrawStat("Static friction", $"μS = {_settings.muS:F2}",
                     Color.white);
            DrawStat("Kinetic energy", $"{kineticEnergy:F2} J",
                     new Color(0.4f, 0.9f, 1f));

            DrawStat("Last force", lastForce.ToString("F2"), Color.yellow);
            DrawStat("Force magnitude", $"{forceMag:F2} N", Color.yellow);
            DrawStat("Est. accel.", $"{forceMag / _settings.mass:F2} m/s²", Color.yellow);
            DrawStat("Active force", "none", Color.gray, style: _dimStyle);

            Separator();

            // --- Friction ramp (SH_MoveState smooth acceleration) ---
            // Visible only when the multiplier is being interpolated back toward 1
            // after leaving a locked-movement action. Green once ramp is complete.
            DrawStat("Friction multiplier", $"{frictionMul:F3}",
                     rampActive ? Color.yellow : Color.green);
            DrawStat("Ramp active", rampActive ? $"YES  (accelTime={accelTime:F2}s)" : "no",
                     rampActive ? new Color(1f, 0.8f, 0.2f) : Color.gray,
                     style: rampActive ? _activeStyle : _dimStyle);

            if (overshooting)
            {
                GUILayout.Space(3);
                GUILayout.Label("⚠  STABILITY: SPEED LIMIT EXCEEDED", _warningStyle);
            }

            GUILayout.EndArea();
            return panelH;
        }

        #endregion

        #region Panel 2 — Animation Telemetry

        private float DrawAnimationPanel(float x, float y)
        {
            if (_context?.Animator == null) return 0f;

            Animator anim = _context.Animator;
            float panelH = 180f;

            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH), GUI.skin.box);

            GUILayout.Label("ANIMATION TELEMETRY", _headerStyle);
            GUILayout.Space(4);

            float movBlend = anim.GetFloat("Movement_Blend");
            float dashForce = anim.GetFloat("DashForce");
            //bool dashBool = anim.GetBool("Dash");
            bool inTrans = anim.IsInTransition(0);
            var stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            float normTime = stateInfo.normalizedTime % 1f;

            DrawStat("Movement Blend", $"{movBlend:F4}",
                     movBlend > 0.5f ? new Color(1f, 0.8f, 0.2f) : Color.white);
            DrawStat("DashForce param", $"{dashForce:F4}",
                     dashForce > 0f ? Color.yellow : Color.gray);
            //DrawStat("Dash trigger", dashBool.ToString(),
            //         dashBool ? Color.red : Color.gray);

            Separator();

            DrawStat("Clip hash", stateInfo.shortNameHash.ToString(), new Color(0.4f, 0.9f, 1f));
            DrawStat("Norm. time", $"{normTime:F3}", Color.white);
            DrawStat("Speed", $"{stateInfo.speed:F2}x", Color.white);
            DrawStat("In transition", inTrans.ToString(),
                     inTrans ? new Color(1f, 0.4f, 1f) : Color.gray);

            if (inTrans)
            {
                var transInfo = anim.GetAnimatorTransitionInfo(0);
                DrawStat("  Trans. norm.", $"{transInfo.normalizedTime:F3}", Color.magenta);
            }

            GUILayout.EndArea();
            return panelH;
        }

        #endregion

        #region Panel 3 — Economy Telemetry

        private float DrawEconomyPanel(float x, float y)
        {
            float panelH = 440f;
            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH), GUI.skin.box);

            GUILayout.Label("ECONOMIC TELEMETRY", _headerStyle);
            GUILayout.Space(4);

            // --- Durability ---
            GUILayout.Label("— Durability —", _subheaderStyle);

            float normDur = _health.NormalizedDurability;
            Color durCol = normDur > 0.5f ? Color.green
                          : normDur > 0.25f ? Color.yellow : Color.red;

            DrawStat("Durability", $"{_health.CurrentDurability:F1} / {_health.MaxDurability:F1}", durCol);
            DrawStat("Normalized", $"{normDur:P1}", durCol);
            DrawStat("Defeated", _health.IsDefeated.ToString(),
                     _health.IsDefeated ? Color.red : Color.gray);

            Separator();

            // --- Resources ---
            GUILayout.Label("— Resources —", _subheaderStyle);

            float normEC = _resources.NormalizedEnergy;
            Color ecCol = normEC > 0.5f ? Color.green
                          : normEC > 0.25f ? Color.yellow : Color.red;
            float maxEC = _economySettings != null ? _economySettings.maxEnergy : 0f;

            DrawStat("Energy (EC)", $"{_resources.CurrentEnergy:F1} / {maxEC:F0}", ecCol);
            DrawStat("EC regen/s",
                     _economySettings != null
                         ? $"{_economySettings.energyRegenPerSecond * _resources.EnergyRegenModifier:F2} EC/s"
                         : "N/A",
                     ecCol);
            DrawStat("Identity Cores", $"{_resources.CurrentIdentityCores} IC", new Color(0.4f, 0.9f, 1f));
            DrawStat("Scrap", $"{_resources.CurrentScrap:F1} SC", Color.yellow);
            DrawStat("Total DP spent", $"{_resources.TotalDPSpent} DP", Color.white);

            Separator();

            // --- Progression ---
            GUILayout.Label("— Progression —", _subheaderStyle);

            if (_economySettings != null)
            {
                float costNextDP = SH_ProgressionCalculator.GetICCostForNextDP(
                    _resources.TotalDPSpent, _economySettings);
                float progress = SH_ProgressionCalculator.GetProgressToNextDP(
                    _resources.CurrentIdentityCores, _resources.TotalDPSpent, _economySettings);
                int simDP = SH_ProgressionCalculator.SimulatePurge(
                    _resources.CurrentIdentityCores, _resources.TotalDPSpent, _economySettings);
                float reconfigCost = SH_ProgressionCalculator.GetReconfigCostWithModifier(
                    _resources.TotalDPSpent, _economySettings, _resources.ReconfigCostModifier);
                bool eligible = SH_ProgressionCalculator.IsEligibleForNextDP(
                    _resources.CurrentIdentityCores, _resources.TotalDPSpent, _economySettings);

                DrawStat("Next DP cost", $"{costNextDP:F1} IC",
                         eligible ? Color.green : Color.white);
                DrawStat("Progress to DP", $"{progress:P1}", new Color(0.4f, 0.9f, 1f));
                DrawStat("Purge preview", $"+{simDP} DP",
                         simDP > 0 ? Color.green : Color.gray);
                DrawStat("Reconfig cost", $"{reconfigCost:F1} SC",
                         _resources.CurrentScrap >= reconfigCost ? Color.white : Color.red);
            }

            Separator();

            // --- Active modifiers ---
            GUILayout.Label("— Active Modifiers —", _subheaderStyle);

            DrawModifier("EC Regen", _resources.EnergyRegenModifier);
            DrawModifier("IC Drop", _resources.ICDropRateModifier);
            DrawModifier("Reconfig", _resources.ReconfigCostModifier);

            Separator();

            // --- Active economic events ---
            GUILayout.Label("— Active Events —", _subheaderStyle);

            if (_economicEvents != null)
            {
                DrawEventStatus(EconomicEventType.IdentityCoreScarcity, "IC Scarcity");
                DrawEventStatus(EconomicEventType.ReconfigurationOverload, "Reconfig Overload");
                DrawEventStatus(EconomicEventType.EnergyFlux, "Energy Flux");
            }
            else
            {
                GUILayout.Label("EconomicEventManager unavailable", _dimStyle);
            }

            GUILayout.EndArea();
            return panelH;
        }

        private void DrawModifier(string label, float value)
        {
            bool altered = Mathf.Abs(value - 1f) > 0.001f;
            string dir = value > 1f ? "▲" : value < 1f ? "▼" : "=";
            DrawStat($"  {label}", $"x{value:F3}  {dir}",
                     altered ? Color.yellow : Color.gray,
                     style: altered ? _activeStyle : _dimStyle);
        }

        private void DrawEventStatus(EconomicEventType type, string label)
        {
            bool active = _economicEvents.IsEventActive(type);
            float remaining = _economicEvents.GetEventRemainingDuration(type);
            string val = active ? $"ACTIVE — {remaining:F0}s remaining" : "Inactive";
            DrawStat($"  {label}", val,
                     active ? Color.red : Color.gray,
                     style: active ? _warningStyle : _dimStyle);
        }

        #endregion

        #region Panel 4 — Interaction Telemetry

        private float DrawInteractionPanel(float x, float y)
        {
            float panelH = 320f;
            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH), GUI.skin.box);

            GUILayout.Label("INTERACTION TELEMETRY", _headerStyle);
            GUILayout.Space(4);

            // --- Controller state ---
            GUILayout.Label("— Controller —", _subheaderStyle);

            DrawStat("Holding",
                     _interaction.IsHolding.ToString(),
                     _interaction.IsHolding ? Color.yellow : Color.gray);
            DrawStat("Hold progress",
                     $"{_interaction.NormalizedHoldProgress:P1}",
                     _interaction.IsHolding ? Color.yellow : Color.gray);
            DrawStat("Focused target",
                     _interaction.FocusedTarget != null
                         ? _interaction.FocusedTarget.GetType().Name
                         : "none",
                     _interaction.FocusedTarget != null ? Color.green : Color.gray);

            if (_interaction.FocusedTarget != null)
            {
                IInteractable t = _interaction.FocusedTarget;
                float dist = Vector3.Distance(transform.position, t.WorldPosition);
                DrawStat("  Type", t.InteractionType.ToString(), Color.white);
                DrawStat("  Distance", $"{dist:F2} m",
                         dist < _interactionSettings.detectionRadius * 0.6f
                             ? Color.green : Color.yellow);
                DrawStat("  Available", t.IsAvailable.ToString(),
                         t.IsAvailable ? Color.green : Color.red);

                if (t is SH_InteractableObject sio)
                    DrawStat("  Persistent ID", sio.PersistentID, Color.white);
            }

            Separator();

            // --- Live input flags ---
            GUILayout.Label("— Input flags —", _subheaderStyle);

            bool pressed = _context.Input.InteractPressed;
            bool released = _context.Input.InteractReleased;
            bool held = _context.Input.InteractHeld;

            DrawStat("InteractPressed", pressed.ToString(),
                     pressed ? Color.yellow : Color.gray);
            DrawStat("InteractReleased", released.ToString(),
                     released ? Color.magenta : Color.gray);
            DrawStat("InteractHeld", held.ToString(),
                     held ? Color.green : Color.gray);

            Separator();

            // --- Active configuration ---
            GUILayout.Label("— Settings —", _subheaderStyle);

            if (_interactionSettings != null)
            {
                DrawStat("Detection radius", $"{_interactionSettings.detectionRadius:F2} m", Color.white);
                DrawStat("Hold CaptiveCore", $"{_interactionSettings.captiveCoreHoldDuration:F1} s", Color.white);
                DrawStat("Hold ScrapPile", $"{_interactionSettings.scrapPileHoldDuration:F1} s", Color.white);
                DrawStat("Break on range", _interactionSettings.breakOnRangeExit.ToString(),
                         _interactionSettings.breakOnRangeExit ? Color.yellow : Color.gray);
                DrawStat("Toggle mode CC", _interactionSettings.captiveCoreToggleMode.ToString(),
                         _interactionSettings.captiveCoreToggleMode ? Color.cyan : Color.gray);
            }

            GUILayout.EndArea();
            return panelH;
        }

        #endregion

        #region Panel 5 — Perception

        private float DrawPerceptionPanel(float x, float y)
        {
            int count = _perceivedCount;
            float panelH = Mathf.Max(100f, 60f + count * 46f);

            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH), GUI.skin.box);

            GUILayout.Label($"PERCEPTION  ({count} object{(count != 1 ? "s" : "")} in range)", _headerStyle);
            GUILayout.Space(4);

            if (count == 0)
            {
                GUILayout.Label($"No IInteractable within radius {_interactionSettings?.detectionRadius:F1}m",
                                _dimStyle);
            }
            else
            {
                bool isFocusedTarget = false;
                for (int i = 0; i < count; i++)
                {
                    var (obj, dist) = _perceivedObjects[i];
                    if (obj == null) continue;

                    isFocusedTarget = (_interaction.FocusedTarget == obj);

                    string typeStr = obj.GetType().Name.Replace("SH_", "");
                    string availStr = obj.IsAvailable ? "available" : "consumed";
                    string holdStr = obj.InteractionType == InteractionType.Hold ? "Hold" : "Press";
                    string focusStr = isFocusedTarget ? " ◀ FOCUSED" : "";
                    string idStr = obj is SH_InteractableObject sio ? sio.PersistentID : "—";

                    Color rowColor = !obj.IsAvailable ? Color.gray
                                  : isFocusedTarget ? Color.green
                                  : Color.white;

                    DrawStat($"  [{typeStr}]",
                             $"{dist:F2}m  {holdStr}  {availStr}{focusStr}",
                             rowColor);
                    DrawStat($"    ID", idStr, Color.gray, style: _dimStyle);
                }
            }

            GUILayout.EndArea();
            return panelH;
        }

        /// <summary>
        /// Updates the perception cache in Update() so OnGUI() and OnDrawGizmos()
        /// read consistent data without calling Physics from those callbacks.
        /// </summary>
        private void UpdatePerceptionCache()
        {
            _perceivedObjects.Clear();
            _perceivedCount = 0;

            if (_interactionSettings == null) return;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                _interactionSettings.detectionRadius,
                _perceptionBuffer,
                _interactionSettings.interactableLayer);

            for (int i = 0; i < count; i++)
            {
                var interactable = _perceptionBuffer[i].GetComponent<IInteractable>();
                if (interactable == null) continue;

                float dist = Vector3.Distance(transform.position, interactable.WorldPosition);
                _perceivedObjects.Add((interactable, dist));
            }

            // Sort by distance so the panel reads nearest-first.
            _perceivedObjects.Sort((a, b) => a.dist.CompareTo(b.dist));
            _perceivedCount = _perceivedObjects.Count;
        }

        #endregion

        #region Panel 6 — Combat Panel

        /// <summary>
        /// Draws the combat system telemetry panel.
        /// Displays player combat stats, computed damage values, surge state,
        /// and all integration points status from GDD §5.3 Stage A.
        /// </summary>
        private float DrawCombatPanel(float x, float y)
        {
            float panelH = 380f;
            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH),
                GUI.skin.box);

            GUILayout.Label("COMBAT TELEMETRY", _headerStyle);
            GUILayout.Space(6);

            if (_combatController == null || _playerCombatStats == null ||
                _combatSettings == null)
            {
                GUILayout.Label("Combat system not initialized.", _warningStyle);
                GUILayout.Space(6);
                if (_combatController == null) GUILayout.Label("Combat controller not initialized.", _warningStyle);
                GUILayout.Space(6);
                if (_playerCombatStats == null) GUILayout.Label("Player combat stats not initialized.", _warningStyle);
                GUILayout.Space(6);
                if (_combatSettings == null) GUILayout.Label("Combat settings not initialized.", _warningStyle);
                GUILayout.EndArea();
                return 420f;
            }

            // --- Player Base Stats ---
            GUILayout.Label("--- Player Stats (Base) ---", _style);
            DrawStat("Strength (F)", $"{_playerCombatStats.Strength:F1}", Color.white);
            DrawStat("Defense (D)", $"{_playerCombatStats.Defense:F1}", Color.white);
            DrawStat("Agility (A)", $"{_playerCombatStats.Agility:F1}", Color.white);
            DrawStat("Posture Max", $"{_playerCombatStats.PostureMax:F1}", Color.white);

            GUILayout.Space(6);
            GUILayout.Label("--- Damage Formula Preview ---", _style);

            bool surge = _combatController.IsSurgeActive;
            float avLight = SH_DamageCalculator.ComputeAttackValue(
                _playerCombatStats, _combatSettings, AttackType.Light, surge);
            float avHeavy = SH_DamageCalculator.ComputeAttackValue(
                _playerCombatStats, _combatSettings, AttackType.Heavy, surge);

            // Simulate against zero-defense target for the raw DE preview
            float deLight = Mathf.Max(0f,
                avLight - (_playerCombatStats.Defense * _combatSettings.defenseEffectiveness));
            float deHeavy = Mathf.Max(0f,
                avHeavy - (_playerCombatStats.Defense * _combatSettings.defenseEffectiveness));

            DrawStat("AV Light", $"{avLight:F1}", Color.yellow);
            DrawStat("AV Heavy", $"{avHeavy:F1}", new Color(1f, 0.6f, 0f));
            DrawStat("DE Light (vs 0 def)", $"{deLight:F1}", Color.yellow);
            DrawStat("DE Heavy (vs 0 def)", $"{deHeavy:F1}", new Color(1f, 0.6f, 0f));
            DrawStat("Posture (Light)", $"{avLight * _combatSettings.postureDamageRatio:F1}", Color.cyan);
            DrawStat("Posture (Heavy)", $"{avHeavy * _combatSettings.postureDamageRatio:F1}", Color.cyan);

            GUILayout.Space(6);
            GUILayout.Label("--- Integration Points ---", _style);
            DrawStat("OnHitImpact wired", "✓ CombatController", Color.green);
            DrawStat("CaptiveCore.ForceDestroy", "✓ HitboxController", Color.green);
            DrawStat("RollEnergyOnElite", "✓ HitboxController", Color.green);
            DrawStat("NotifyReconfig bridge", "PENDING skill tree", Color.yellow);

            GUILayout.EndArea();
            return 380f;
        }

        #endregion

        #region Panel 7 — Enemy Panel

        private float DrawEnemyPanel(float x, float y)
        {
            var enemies = UnityEngine.Object.FindObjectsByType<Game.Enemy.SH_EnemyController>(
                FindObjectsSortMode.None);

            int aliveCount = 0;
            foreach (var e in enemies)
                if (!e.IsDead) aliveCount++;

            float panelH = 60f + Mathf.Min(enemies.Length, 6) * 52f;
            GUILayout.BeginArea(new Rect(x, y, panelWidth, panelH), GUI.skin.box);
            GUILayout.Label("ENEMY TELEMETRY", _headerStyle);
            GUILayout.Space(4);
            DrawStat("Enemies in scene", $"{enemies.Length} total / {aliveCount} alive", Color.white);
            GUILayout.Space(4);

            int shown = 0;
            foreach (var enemy in enemies)
            {
                if (shown >= 6) break;
                DrawEnemyRow(enemy);
                shown++;
            }

            GUILayout.EndArea();
            return panelH;
        }

        private void DrawEnemyRow(Game.Enemy.SH_EnemyController enemy)
        {
            if (enemy == null) return;

            string name = enemy.name;
            string state = enemy.CurrentStateName;
            float hp = enemy.NormalizedHP;
            float pst = enemy.NormalizedPosture;
            bool dead = enemy.IsDead;
            bool stag = enemy.IsStaggered;
            bool block = enemy.IsBlocking;

            Color nameColor = dead ? Color.gray : stag ? Color.red : Color.white;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name} [{state}]",
                new GUIStyle(_style) { normal = { textColor = nameColor } },
                GUILayout.Width(LabelWidth));
            GUILayout.Label(
                $"HP {hp:P0}  PST {pst:P0}{(stag ? " STAGGER" : "")}{(block ? " BLOCK" : "")}",
                new GUIStyle(_style) { normal = { textColor = dead ? Color.gray : Color.white } });
            GUILayout.EndHorizontal();
        }

        #endregion

        #region Panel 7 — Surge & Difficulty Panel

        private float DrawSurgeAndDifficultyPanel(float x, float y)
        {
            GUILayout.BeginArea(new Rect(x, y, panelWidth, 240f), GUI.skin.box);
            GUILayout.Label("SURGE & DIFFICULTY", _headerStyle);
            GUILayout.Space(6);

            // --- Energy Surge ---
            GUILayout.Label("--- Energy Surge (Sobrecarga) ---", _style);
            if (_surgeSystem != null)
            {
                float bar = _surgeSystem.SurgeBar;
                bool active = _combatController?.IsSurgeActive ?? false;
                bool cooldown = _combatController?.IsInSurgeCooldown ?? false;

                Color barColor = active ? Color.red : cooldown ? Color.yellow : Color.cyan;
                DrawStat("Surge Bar", $"{bar:P1}", barColor);
                DrawStat("Surge Active", active.ToString(),
                    active ? Color.red : Color.gray);
                DrawStat("Surge Cooldown", cooldown.ToString(),
                    cooldown ? Color.yellow : Color.gray);
                DrawStat("DMG Mult",
                    active ? $"x{_combatSettings?.surgeDamageMultiplier:F2}" : "x1.00",
                    active ? Color.red : Color.gray);
            }
            else
            {
                GUILayout.Label("SurgeSystem not initialized.", _warningStyle);
            }

            GUILayout.Space(6);

            // --- Difficulty ---
            GUILayout.Label("--- Difficulty (GDD §5.3.6) ---", _style);
            if (_difficultyManager != null)
            {
                DrawStat("Difficulty", _difficultyManager.ActiveDifficulty.ToString(), Color.white);
                DrawStat("Zone", $"Z-{_difficultyManager.CurrentZone:D2}", Color.white);
                DrawStat("Zone Factor", $"x{_difficultyManager.CurrentZoneFactor:F2}", Color.cyan);
                DrawStat("AI Multiplier",
                    $"x{_difficultyManager.CurrentAIMultiplier:F2}",
                    _difficultyManager.CurrentAIMultiplier > 1.05f ? Color.red
                  : _difficultyManager.CurrentAIMultiplier < 0.95f ? Color.green
                  : Color.white);
                DrawStat("Tracked Enemies", $"{_difficultyManager.TrackedEnemyCount}", Color.white);
            }
            else
            {
                GUILayout.Label("DifficultyManager not initialized.", _warningStyle);
            }

            GUILayout.EndArea();
            return 240f;
        }

        #endregion

        #region Gizmos — Scene View

        private void OnDrawGizmos()
        {
            if (!showGizmos || !Application.isPlaying || _context == null) return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 bodyOrigin = transform.position + Vector3.up * 0.8f;

            // 1. Current velocity vector (cyan) + vertical component (yellow)
            if (_physics != null)
            {
                Vector3 vel = _physics.CurrentVelocity;
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(origin, vel);
                DrawArrowHead(origin + vel, vel.normalized, 0.18f, Color.cyan);

                // Vertical component drawn separately so it is always visible
                // even when horizontal speed dominates the vector length.
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(origin, new Vector3(0f, vel.y, 0f));
            }

            // 2. Last applied force / impulse (orange)
            if (_physics != null && _physics.HasActiveForce)
            {
                Vector3 force = _physics.LastAppliedForce / Mathf.Max(_settings.mass, 0.001f);
                Gizmos.color = new Color(1f, 0.6f, 0.1f);
                Gizmos.DrawRay(origin, force);
                DrawArrowHead(origin + force, force.normalized, 0.15f, new Color(1f, 0.6f, 0.1f));
            }

            // 3. Effective gravity indicator (magenta, always points down)
            if (_settings != null)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.8f);
                Gizmos.DrawRay(bodyOrigin, Vector3.down * 1.2f);
            }

            // 4. Interaction detection radius (green when target focused, blue otherwise)
            if (_interactionSettings != null)
            {
                bool hasTarget = _interaction?.FocusedTarget != null;
                Gizmos.color = hasTarget
                    ? new Color(0.2f, 1f, 0.4f, 0.25f)
                    : new Color(0.3f, 0.6f, 1f, 0.15f);
                Gizmos.DrawWireSphere(transform.position, _interactionSettings.detectionRadius);

                // Range-break buffer — visible only when breakOnRangeExit is enabled.
                if (_interactionSettings.breakOnRangeExit)
                {
                    Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.08f);
                    Gizmos.DrawWireSphere(
                        transform.position,
                        _interactionSettings.detectionRadius + _interactionSettings.rangeBreakBuffer);
                }
            }

            // 5. Lines to all perceived objects
            if (_perceivedCount > 0)
            {
                for (int i = 0; i < _perceivedCount; i++)
                {
                    var (obj, _) = _perceivedObjects[i];
                    if (obj == null) continue;

                    bool isFocused = (_interaction?.FocusedTarget == obj);

                    if (isFocused)
                    {
                        // Focused target: bright green line + solid sphere marker
                        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
                        Gizmos.DrawLine(transform.position, obj.WorldPosition);
                        Gizmos.DrawSphere(obj.WorldPosition, 0.22f);

                        // Hold progress arc: dots fill the circle as the timer advances
                        if (_interaction.IsHolding)
                        {
                            Gizmos.color = Color.yellow;
                            DrawHoldProgressArc(obj.WorldPosition,
                                                _interaction.NormalizedHoldProgress);
                        }
                    }
                    else
                    {
                        // In-range but not focused: dim blue (available) or gray (consumed)
                        Gizmos.color = obj.IsAvailable
                            ? new Color(0.3f, 0.6f, 1f, 0.5f)
                            : new Color(0.5f, 0.5f, 0.5f, 0.3f);
                        Gizmos.DrawLine(transform.position, obj.WorldPosition);
                        Gizmos.DrawWireSphere(obj.WorldPosition, 0.15f);
                    }
                }
            }
        }

        /// <summary>
        /// Draws a simple two-line arrowhead at <paramref name="tip"/> in the Scene View.
        /// </summary>
        private void DrawArrowHead(Vector3 tip, Vector3 dir, float size, Color color)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            Gizmos.color = color;
            Vector3 right = Vector3.Cross(dir, Vector3.up).normalized * size;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.Cross(dir, Vector3.right).normalized * size;
            Gizmos.DrawLine(tip, tip - dir * size + right);
            Gizmos.DrawLine(tip, tip - dir * size - right);
        }

        /// <summary>
        /// Draws a dot-arc around <paramref name="center"/> to visualise hold timer progress.
        /// Dots fill clockwise from 0° as <paramref name="progress"/> approaches 1.
        /// </summary>
        private void DrawHoldProgressArc(Vector3 center, float progress)
        {
            int steps = 16;
            float radius = 0.45f;
            float filled = progress * 360f;
            for (int i = 0; i < steps; i++)
            {
                float angle = i * (360f / steps);
                if (angle > filled) break;

                float rad = angle * Mathf.Deg2Rad;
                Vector3 pt = center + new Vector3(Mathf.Cos(rad), 0.5f, Mathf.Sin(rad)) * radius;
                Gizmos.DrawSphere(pt, 0.05f);
            }
        }

        #endregion

        #region Shared Drawing Utilities

        private void DrawStat(string label, string value, Color valueColor,
                               GUIStyle style = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}:", _style, GUILayout.Width(LabelWidth));
            GUILayout.Label(value,
                style ?? new GUIStyle(_style) { normal = { textColor = valueColor } });
            GUILayout.EndHorizontal();
        }

        private void Separator()
        {
            GUILayout.Space(4);
            GUILayout.Label("────────────────────────────────────────", _dimStyle);
            GUILayout.Space(2);
        }

        #endregion
    }
}