using Core.Physics;      
using Core.StateMachine; 
using Data;              
using UnityEngine;
using Core;              

/// <summary>
/// Debug component that visualizes real-time data telemetry for the Mecha unit.
/// Displays current velocity, kinetic energy, and applied forces on-screen, and draws vector gizmos in the scene view.
/// </summary>
namespace DebugTools
{
    [DisallowMultipleComponent]
    public class SH_Debugger : MonoBehaviour
    {
        #region Dependencies

        private SH_PhysicsMotor physicsMotor;
        private SH_PlayerStateMachine stateMachine;
        private SH_MovementSettings settings;
        private SH_PlayerContext _context;

        [Header("Visualization Settings")]
        [SerializeField] private bool showOnScreenStats = true;
        [SerializeField] private bool showVectors = true;

        [Tooltip("Multiplier for the max speed threshold to trigger stability warnings. Values above 1 will allow for some overshooting before warning the player.")]
        [Range(1f, 2f)]
        [SerializeField] private float stabilityThreshold = 1.2f;

        #endregion

        #region Private UI Fields

        private GUIStyle _style;
        private GUIStyle _headerStyle;

        #endregion

        private void Awake()
        {
            if (GetComponent<SH_PlayerStateMachine>() == null) { Debug.LogWarning($"{nameof(SH_Debugger)}: PlayerStateMachine reference is missing. Attempting to find PlayerStateMachine on the same GameObject."); return; }
            stateMachine = GetComponent<SH_PlayerStateMachine>();

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
        }

        #region Initialization

        public void Initialize(SH_PlayerContext context)
        {
            _context = context;

            if (_context != null)
            {
                // Assign dependencies from the context
                physicsMotor = _context.Physics;
                settings = _context.Settings;
            }
        }

        #endregion

        private void OnGUI()
        {
            if (!showOnScreenStats || physicsMotor == null || settings == null)
                return;

            // Velocity vector calculation and horizontal speed extraction to provide insight into the Mecha's current movement state,
            // which is crucial for debugging locomotion behavior and ensuring that the physics motor is correctly integrating forces and velocities.
            Vector3 currentVel = physicsMotor.CurrentVelocity;
            float horizontalSpeed = new Vector3(currentVel.x, 0f, currentVel.z).magnitude;

            // Calculation of kinetic energy using the formula Ek = 0.5 * m * v^2, where m is the mass of the Mecha and v is the magnitude of the velocity vector.
            // This provides a quantitative measure of the Mecha's current energy state, which can be useful for debugging issues related to acceleration, deceleration, and force application.
            float kineticEnergy = 0.5f * settings.mass * currentVel.sqrMagnitude;
            float frictionForce = settings.muK * settings.mass * Mathf.Abs(settings.gravity);

            // Determination of whether the current horizontal speed exceeds a defined threshold based on the maximum speed setting.
            // This is used to trigger visual warnings in the UI when the Mecha is moving too fast, which can indicate issues with force application, friction, or state transitions that need to be addressed during debugging.
            bool isOvershooting = horizontalSpeed > settings.maxSpeed * stabilityThreshold;

            GUILayout.BeginArea(new Rect(25, 25, 420, 500), GUI.skin.box);

            GUILayout.Label("NEWTONIAN TELEMETRY", _headerStyle);
            GUILayout.Space(8);

            DrawStat("State", stateMachine.GetCurrentStateName(), Color.white);
            DrawStat("Gounded", GetComponent<CharacterController>().isGrounded.ToString(), true ? Color.green: Color.yellow);
            DrawStat("Current Velocity", currentVel.ToString("F2"), Color.white);
            DrawStat("Horizontal Speed", $"{horizontalSpeed:F2} m/s", isOvershooting ? Color.red : Color.green);
            
            GUILayout.Space(8);
            GUILayout.Label("--- Dynamics & Forces ---", _style);

            DrawStat("Mass (m)", $"{settings.mass} kg", Color.white);
            DrawStat("Kinetic Energy (Ek)", $"{kineticEnergy:F2} J", Color.cyan);
            DrawStat("Ground Friction (Ff)", $"{frictionForce:F2} N", Color.red);

            GUILayout.Label("ANIMATION TELEMETRY", _headerStyle);
            GUILayout.Space(8);
            
            float internalSpeed = _context.Animator.GetFloat("Movement_Blend");
            DrawStat("Internal Float (Speed)", internalSpeed.ToString("F4"), Color.yellow);
            float internaDashSpeed = _context.Animator.GetFloat("DashForce");
            DrawStat("Internal Dash Float (Speed)", internaDashSpeed.ToString("F4"), Color.yellow);

            bool dashTrigger = _context.Animator.GetBool("Dash");
            DrawStat("Trigger 'Dash' Active", dashTrigger.ToString(), dashTrigger ? Color.red : Color.gray);
            
            var stateInfo = _context.Animator.GetCurrentAnimatorStateInfo(0);
            DrawStat("Current Clip Hash", stateInfo.shortNameHash.ToString(), Color.cyan);
            DrawStat("Normalized Time", (stateInfo.normalizedTime % 1).ToString("F2"), Color.white);

            bool inTransition = _context.Animator.IsInTransition(0);
            DrawStat("In Transition", inTransition.ToString(), inTransition ? Color.magenta : Color.gray);

            GUILayout.Space(8);
            if (physicsMotor.HasActiveForce)
            {
                DrawStat("Active Force", physicsMotor.LastAppliedForce.ToString("F2"), Color.yellow);
            }

            if (isOvershooting)
            {
                GUILayout.Space(5);
                GUILayout.Label("STABILITY WARNING: SPEED LIMIT EXCEEDED",
                    new GUIStyle(_style) { fontStyle = FontStyle.Bold, normal = { textColor = Color.red } });
            }

            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!showVectors || physicsMotor == null || !Application.isPlaying)
                return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;

            // Visualización de la velocidad actual
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(origin, physicsMotor.CurrentVelocity);

            // Visualización de fuerzas externas si existen
            if (physicsMotor.HasActiveForce)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(origin, physicsMotor.LastAppliedForce / settings.mass);
            }
        }

        private void DrawStat(string label, string value, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: ", _style, GUILayout.Width(180));
            GUILayout.Label(value, new GUIStyle(_style) { normal = { textColor = color } });
            GUILayout.EndHorizontal();
        }
    }
}