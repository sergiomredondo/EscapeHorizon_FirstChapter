using Core.Input;
using Core.Physics;
using UnityEngine;

[DisallowMultipleComponent]
public class SH_MovementTelemetryLite : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _transform;
    [SerializeField] private Rigidbody _rigidbody;

    // Opcionales (ajusta según tu arquitectura real)
    [SerializeField] private SH_PhysicsMotor _physicsMotor;
    [SerializeField] private SH_InputHandler _inputHandler;

    [Header("Sampling")]
    [SerializeField] private float _sampleInterval = 0.1f; // 10 Hz
    private float _sampleTimer;

    [Header("Debug Flags")]
    [SerializeField] private bool _logToConsole = false;
    [SerializeField] private bool _drawVelocity = true;

    // Estado interno (sin allocs)
    private Vector3 _lastPosition;
    private Vector3 _velocity;
    private Vector3 _acceleration;

    private void Awake()
    {
        if (_transform == null)
            _transform = transform;

        _lastPosition = _transform.position;
    }

    private void Update()
    {
        if (!enabled) return;

        _sampleTimer += Time.deltaTime;

        if (_sampleTimer >= _sampleInterval)
        {
            Sample();
            _sampleTimer = 0f;
        }

        if (_drawVelocity)
            DrawDebug();
    }

    private void Sample()
    {
        Vector3 currentPosition = _transform.position;

        // Velocidad
        Vector3 newVelocity;

        if (_physicsMotor != null)
            newVelocity = _physicsMotor.CurrentVelocity;
        else if (_rigidbody != null)
            newVelocity = _rigidbody.linearVelocity;
        else
            newVelocity = (currentPosition - _lastPosition) / _sampleInterval;

        // Aceleración
        _acceleration = (newVelocity - _velocity) / _sampleInterval;

        _velocity = newVelocity;
        _lastPosition = currentPosition;

        if (_logToConsole)
            LogState();
    }

    private void DrawDebug()
    {
        Debug.DrawLine(
            _transform.position,
            _transform.position + _velocity,
            Color.cyan,
            0f,
            false
        );
    }

    private void LogState()
    {
        Vector2 input = _inputHandler != null ? _inputHandler.MoveInput : Vector2.zero;
        bool grounded = _physicsMotor != null;

        Debug.Log(
            $"[Telemetry] " +
            $"v:{_velocity.magnitude:F2} " +
            $"a:{_acceleration.magnitude:F2} " +
            $"input:{input.magnitude:F2} " +
            $"grounded:{grounded}"
        );
    }
}