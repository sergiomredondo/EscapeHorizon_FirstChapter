using UnityEngine;
using Core.Physics;      // Namespace definido en SH_PhysicsMotor
using Core.StateMachine; // Namespace definido en SH_PlayerStateMachine
using Data;              // Namespace definido en SH_MovementSettings

/// <summary>
/// Debug component that visualizes real-time physics telemetry for the Mecha unit.
/// Displays current velocity, kinetic energy, and applied forces on-screen, and draws vector gizmos in the scene view.
/// </summary>
namespace DebugTools
{
    [DisallowMultipleComponent]
    public class SH_PhysicsDebugger : MonoBehaviour
    {
        #region Serialized References

        [Header("Telemetry Sources")]
        [SerializeField] private SH_PhysicsMotor physicsMotor;
        [SerializeField] private SH_PlayerStateMachine stateMachine;
        [SerializeField] private SH_MovementSettings settings;

        [Header("Visualization Settings")]
        [SerializeField] private bool showOnScreenStats = true;
        [SerializeField] private bool showVectors = true;

        [Tooltip("Multiplicador sobre maxSpeed para alertas de estabilidad.")]
        [Range(1f, 2f)]
        [SerializeField] private float stabilityThreshold = 1.2f;

        #endregion

        #region Private UI Fields
        private GUIStyle _style;
        private GUIStyle _headerStyle;
        #endregion

        private void Awake()
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
        }

        private void OnGUI()
        {
            if (!showOnScreenStats || physicsMotor == null || settings == null)
                return;

            // Propiedad correcta según SH_PhysicsMotor
            Vector3 currentVel = physicsMotor.CurrentVelocity;
            float horizontalSpeed = new Vector3(currentVel.x, 0f, currentVel.z).magnitude;

            // Cálculos basados en SH_MovementSettings
            float kineticEnergy = 0.5f * settings.mass * currentVel.sqrMagnitude;
            float frictionForce = settings.muK * settings.mass * Mathf.Abs(settings.gravity);

            // Validación contra maxSpeed
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