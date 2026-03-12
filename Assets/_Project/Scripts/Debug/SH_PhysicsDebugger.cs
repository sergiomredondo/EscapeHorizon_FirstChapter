using Core.Physics;
using Core.StateMachine;
using Data;
using UnityEngine;
using Core;
using Game.Economy;
using Game.Economy.Data;
using Game.Economy.Progression;

/// <summary>
/// Debug component that visualizes real-time data telemetry for the Mecha unit.
/// Displays physics, animation, and economic state on-screen, and draws
/// vector gizmos in the scene view.
/// Extended to include economic telemetry: resource levels, active economic
/// events, progression curve preview, and defeat penalty simulation.
/// </summary>
namespace DebugTools
{
    [DisallowMultipleComponent]
    public class SH_Debugger : MonoBehaviour
    {
        #region Dependencies

        private SH_PhysicsMotor _physicsMotor;
        private SH_PlayerStateMachine _stateMachine;
        private SH_MovementSettings _settings;
        private SH_PlayerContext _context;

        // Economic system references extracted from context on Initialize().
        private SH_ResourceSystem _resources;
        private SH_HealthComponent _health;
        private SH_EconomicEventManager _economicEvents;
        private SH_EconomySettings _economySettings;

        #endregion

        #region Serialized Visualization Settings

        [Header("Panel Visibility")]
        [Tooltip("Toggle the Newtonian physics telemetry panel.")]
        [SerializeField] private bool showPhysicsPanel = true;

        [Tooltip("Toggle the animation state telemetry panel.")]
        [SerializeField] private bool showAnimationPanel = true;

        [Tooltip("Toggle the economic resource telemetry panel.")]
        [SerializeField] private bool showEconomyPanel = true;

        [Tooltip("Toggle vector gizmos in the Scene view.")]
        [SerializeField] private bool showVectors = true;

        [Header("Physics Settings")]
        [Tooltip("Multiplier for the max speed threshold to trigger stability warnings. " +
                 "Values above 1 allow some overshooting before warning.")]
        [Range(1f, 2f)]
        [SerializeField] private float stabilityThreshold = 1.2f;

        #endregion

        #region Private UI Fields

        private GUIStyle _style;
        private GUIStyle _headerStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _positiveStyle;

        // Layout constants for panel positioning.
        private const float PanelWidth = 420f;
        private const float PanelMargin = 10f;
        private const float PanelStartX = 25f;
        private const float PanelStartY = 25f;
        private const float LabelWidth = 210f;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _stateMachine = GetComponent<SH_PlayerStateMachine>();

            if (_stateMachine == null)
            {
                Debug.LogWarning($"[SH_Debugger] SH_PlayerStateMachine not found on " +
                                 $"{gameObject.name}. Telemetry will be unavailable.");
            }

            InitializeStyles();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Context-driven initialization. Called by SH_PlayerStateMachine during Awake()
        /// after the context has been constructed.
        /// Extracts all required sub-system references from the context for efficient
        /// per-frame access without repeated property lookups.
        /// </summary>
        /// <param name="context">
        /// The fully constructed player context. Must not be null.
        /// </param>
        public void Initialize(SH_PlayerContext context)
        {
            if (context == null)
            {
                Debug.LogWarning($"[SH_Debugger] Initialize called with null context " +
                                 $"on {gameObject.name}. Telemetry will be unavailable.");
                return;
            }

            _context = context;
            _physicsMotor = context.Physics;
            _settings = context.Settings;
            _resources = context.Resources;
            _health = context.Health;
            _economicEvents = context.EconomicEvents;
            _economySettings = context.EconomySettings;
        }

        /// <summary>
        /// Initializes GUIStyle instances used across all telemetry panels.
        /// Called once in Awake() to avoid per-frame allocations in OnGUI().
        /// </summary>
        private void InitializeStyles()
        {
            _style = new GUIStyle
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };

            _headerStyle = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan }
            };

            _warningStyle = new GUIStyle
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.red }
            };

            _positiveStyle = new GUIStyle
            {
                fontSize = 13,
                normal = { textColor = Color.green }
            };
        }

        #endregion

        #region OnGUI — Panel Layout

        private void OnGUI()
        {
            if (_context == null)
                return;

            float currentY = PanelStartY;

            if (showPhysicsPanel)
            {
                float panelHeight = DrawPhysicsPanel(PanelStartX, currentY);
                currentY += panelHeight + PanelMargin;
            }

            if (showAnimationPanel)
            {
                float panelHeight = DrawAnimationPanel(PanelStartX, currentY);
                currentY += panelHeight + PanelMargin;
            }

            if (showEconomyPanel && _resources != null && _health != null)
            {
                DrawEconomyPanel(PanelStartX, currentY);
            }
        }

        #endregion

        #region Physics Panel

        /// <summary>
        /// Draws the Newtonian physics telemetry panel.
        /// Returns the height of the drawn panel for vertical stacking.
        /// </summary>
        private float DrawPhysicsPanel(float x, float y)
        {
            if (_physicsMotor == null || _settings == null)
                return 0f;

            Vector3 currentVel = _physicsMotor.CurrentVelocity;
            float horizontalSpeed = new Vector3(currentVel.x, 0f, currentVel.z).magnitude;
            float kineticEnergy = 0.5f * _settings.mass * currentVel.sqrMagnitude;
            float frictionForce = _settings.muK * _settings.mass * Mathf.Abs(_settings.gravity);
            bool isOvershooting = horizontalSpeed > _settings.maxSpeed * stabilityThreshold;

            GUILayout.BeginArea(new Rect(x, y, PanelWidth, 220f), GUI.skin.box);

            GUILayout.Label("NEWTONIAN TELEMETRY", _headerStyle);
            GUILayout.Space(6);

            DrawStat("State",
                _stateMachine != null ? _stateMachine.GetCurrentStateName() : "N/A",
                Color.white);
            DrawStat("Grounded",
                GetComponent<CharacterController>().isGrounded.ToString(),
                GetComponent<CharacterController>().isGrounded ? Color.green : Color.yellow);
            DrawStat("Current Velocity", currentVel.ToString("F2"), Color.white);
            DrawStat("Horizontal Speed", $"{horizontalSpeed:F2} m/s",
                isOvershooting ? Color.red : Color.green);

            GUILayout.Space(6);
            GUILayout.Label("--- Dynamics & Forces ---", _style);

            DrawStat("Mass (m)", $"{_settings.mass} kg", Color.white);
            DrawStat("Kinetic Energy (Ek)", $"{kineticEnergy:F2} J", Color.cyan);
            DrawStat("Ground Friction (Ff)", $"{frictionForce:F2} N", Color.red);

            if (_physicsMotor.HasActiveForce)
            {
                DrawStat("Active Force",
                    _physicsMotor.LastAppliedForce.ToString("F2"), Color.yellow);
            }

            if (isOvershooting)
            {
                GUILayout.Space(4);
                GUILayout.Label("STABILITY WARNING: SPEED LIMIT EXCEEDED", _warningStyle);
            }

            GUILayout.EndArea();

            return 220f;
        }

        #endregion

        #region Animation Panel

        /// <summary>
        /// Draws the Animator state telemetry panel.
        /// Returns the height of the drawn panel for vertical stacking.
        /// </summary>
        private float DrawAnimationPanel(float x, float y)
        {
            if (_context.Animator == null)
                return 0f;

            GUILayout.BeginArea(new Rect(x, y, PanelWidth, 185f), GUI.skin.box);

            GUILayout.Label("ANIMATION TELEMETRY", _headerStyle);
            GUILayout.Space(6);

            float movementBlend = _context.Animator.GetFloat("Movement_Blend");
            float dashForce = _context.Animator.GetFloat("DashForce");
            bool dashTrigger = _context.Animator.GetBool("Dash");

            DrawStat("Movement Blend", movementBlend.ToString("F4"), Color.yellow);
            DrawStat("Dash Force", dashForce.ToString("F4"), Color.yellow);
            DrawStat("Dash Trigger", dashTrigger.ToString(),
                dashTrigger ? Color.red : Color.gray);

            var stateInfo = _context.Animator.GetCurrentAnimatorStateInfo(0);
            DrawStat("Clip Hash", stateInfo.shortNameHash.ToString(), Color.cyan);
            DrawStat("Normalized Time", (stateInfo.normalizedTime % 1).ToString("F2"), Color.white);

            bool inTransition = _context.Animator.IsInTransition(0);
            DrawStat("In Transition", inTransition.ToString(),
                inTransition ? Color.magenta : Color.gray);

            GUILayout.EndArea();

            return 185f;
        }

        #endregion

        #region Economy Panel

        /// <summary>
        /// Draws the economic resource and event telemetry panel.
        /// Displays current resource levels, active economic events,
        /// progression curve preview for the next DP, and defeat penalty simulation.
        /// </summary>
        private float DrawEconomyPanel(float x, float y)
        {
            GUILayout.BeginArea(new Rect(x, y, PanelWidth, 450f), GUI.skin.box);

            GUILayout.Label("ECONOMIC TELEMETRY", _headerStyle);
            GUILayout.Space(6);

            // --- Durability ---
            GUILayout.Label("--- Durability ---", _style);

            float normalizedDurability = _health.NormalizedDurability;
            Color durabilityColor = normalizedDurability > 0.5f ? Color.green
                                  : normalizedDurability > 0.25f ? Color.yellow
                                  : Color.red;

            DrawStat("Durability",
                $"{_health.CurrentDurability:F1} / {_health.MaxDurability:F1}",
                durabilityColor);
            DrawStat("Normalized", $"{normalizedDurability:P1}", durabilityColor);
            DrawStat("Is Defeated", _health.IsDefeated.ToString(),
                _health.IsDefeated ? Color.red : Color.gray);

            GUILayout.Space(6);

            // --- Resources ---
            GUILayout.Label("--- Resources ---", _style);

            float normalizedEnergy = _resources.NormalizedEnergy;
            Color energyColor = normalizedEnergy > 0.5f ? Color.green
                              : normalizedEnergy > 0.25f ? Color.yellow
                              : Color.red;

            DrawStat("Energy (EC)",
                $"{_resources.CurrentEnergy:F1} / " +
                $"{(_economySettings != null ? _economySettings.maxEnergy.ToString("F0") : "?")}",
                energyColor);
            DrawStat("Energy Normalized", $"{normalizedEnergy:P1}", energyColor);
            DrawStat("Identity Cores", $"{_resources.CurrentIdentityCores} IC", Color.cyan);
            DrawStat("Scrap", $"{_resources.CurrentScrap:F1} SC", Color.yellow);
            DrawStat("Total DP Spent", $"{_resources.TotalDPSpent} DP", Color.white);

            GUILayout.Space(6);

            // --- Progression Curve Preview ---
            GUILayout.Label("--- Progression ---", _style);

            if (_economySettings != null)
            {
                float costNextDP = SH_ProgressionCalculator.GetICCostForNextDP(
                    _resources.TotalDPSpent, _economySettings);

                float progressToNext = SH_ProgressionCalculator.GetProgressToNextDP(
                    _resources.CurrentIdentityCores,
                    _resources.TotalDPSpent,
                    _economySettings);

                int simulatedDP = SH_ProgressionCalculator.SimulatePurge(
                    _resources.CurrentIdentityCores,
                    _resources.TotalDPSpent,
                    _economySettings);

                float reconfigCost = SH_ProgressionCalculator.GetReconfigCostWithModifier(
                    _resources.TotalDPSpent,
                    _economySettings,
                    _resources.ReconfigCostModifier);

                DrawStat("Next DP Cost",
                    $"{costNextDP:F1} IC",
                    SH_ProgressionCalculator.IsEligibleForNextDP(
                        _resources.CurrentIdentityCores,
                        _resources.TotalDPSpent,
                        _economySettings) ? Color.green : Color.white);

                DrawStat("Progress to Next DP", $"{progressToNext:P1}", Color.cyan);
                DrawStat("Purge Preview", $"+{simulatedDP} DP",
                    simulatedDP > 0 ? Color.green : Color.gray);
                DrawStat("Reconfig Cost", $"{reconfigCost:F1} SC",
                    _resources.CurrentScrap >= reconfigCost ? Color.white : Color.red);
            }

            GUILayout.Space(6);

            // --- Active Economic Events ---
            GUILayout.Label("--- Active Events ---", _style);

            if (_economicEvents != null)
            {
                DrawEventStatus(EconomicEventType.IdentityCoreScarcity, "IC Scarcity");
                DrawEventStatus(EconomicEventType.ReconfigurationOverload, "Reconfig Overload");
                DrawEventStatus(EconomicEventType.EnergyFlux, "Energy Flux");
            }

            // --- Active Modifiers ---
            GUILayout.Space(4);
            GUILayout.Label("--- Active Modifiers ---", _style);

            DrawStat("EC Regen Modifier",
                $"x{_resources.EnergyRegenModifier:F2}",
                _resources.EnergyRegenModifier != 1f ? Color.yellow : Color.gray);
            DrawStat("IC Drop Modifier",
                $"x{_resources.ICDropRateModifier:F2}",
                _resources.ICDropRateModifier != 1f ? Color.yellow : Color.gray);
            DrawStat("Reconfig Modifier",
                $"x{_resources.ReconfigCostModifier:F2}",
                _resources.ReconfigCostModifier != 1f ? Color.yellow : Color.gray);

            GUILayout.EndArea();

            return 340f;
        }

        /// <summary>
        /// Draws a single event status row showing whether the event is active
        /// and its remaining duration if so.
        /// </summary>
        /// <param name="eventType"> The event type to display. </param>
        /// <param name="label"> Human-readable label for the event. </param>
        private void DrawEventStatus(EconomicEventType eventType, string label)
        {
            bool isActive = _economicEvents.IsEventActive(eventType);
            float remainingTime = _economicEvents.GetEventRemainingDuration(eventType);

            string valueText = isActive
                ? $"ACTIVE — {remainingTime:F0}s remaining"
                : "Inactive";

            DrawStat(label, valueText, isActive ? Color.red : Color.gray);
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!showVectors || _physicsMotor == null || !Application.isPlaying)
                return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;

            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(origin, _physicsMotor.CurrentVelocity);

            if (_physicsMotor.HasActiveForce)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(origin, _physicsMotor.LastAppliedForce / _settings.mass);
            }
        }

        #endregion

        #region Shared Drawing Utility

        /// <summary>
        /// Renders a single labeled stat row with a colored value field.
        /// Used across all telemetry panels for visual consistency.
        /// </summary>
        private void DrawStat(string label, string value, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: ", _style, GUILayout.Width(LabelWidth));
            GUILayout.Label(value, new GUIStyle(_style) { normal = { textColor = color } });
            GUILayout.EndHorizontal();
        }

        #endregion
    }
}