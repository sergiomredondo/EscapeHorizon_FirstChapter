using UnityEngine;

public class SH_AmbientController : MonoBehaviour
{
    [Header("DayCycle Settings")]
    [SerializeField, Tooltip("Reference to the directional light representing the sun in the scene.")]
    private Light sunLight;

    [Range(0f, 1f)]
    [SerializeField, Tooltip("Speed of time progression during daytime.")]
    private float daySpeed = 0.1f;

    [Range(0f, 1f)]
    [SerializeField, Tooltip("Speed of time progression during nighttime.")]
    private float nightSpeed = 0.1f;

    [Range(0f, 0.5f)]
    [SerializeField, Tooltip("Base speed of the cloud texture movement.")]
    private float speed = 0.1f;

    [Range(0f, 1f)]
    [SerializeField, Tooltip("Amplitude of the oscillatory movement to simulate atmospheric turbulence.")]
    private float rotationAmplitude = 0.5f;

    private float _timeOfDay;
    private float _cloudOffset;

    private void Start()
    {
        // 0.5f represents noon: (0.5 * 360) - 90 = 90 degrees (pointing straight down)
        _timeOfDay = 0.5f;
        if (sunLight == null)
        {
            sunLight = GetComponent<Light>();
        }

        if (sunLight != null)
        {
            UpdateAmbientLogic();
        }
    }

    private void Update()
    {
        if (sunLight == null) return;

        UpdateAmbientLogic();
    }

    /// <summary>
    /// Processes time progression, solar rotation, and cloud projection movement.
    /// </summary>
    private void UpdateAmbientLogic()
    {
        float dt = Time.deltaTime;

        // 1. Day/Night Cycle

        // Determine if it is day or night based on sun elevation
        float sunDot = Vector3.Dot(sunLight.transform.forward, Vector3.down);
        bool isDay = sunDot > 0f;
        float cycleSpeed = isDay ? daySpeed : nightSpeed;

        // Progress normalized time
        _timeOfDay += dt * cycleSpeed * 0.01f;
        _timeOfDay %= 1f;

        float sunAngleRad = ((_timeOfDay * 360f) - 90f);
        Quaternion dayNightRotation = new Quaternion(Mathf.Sin(sunAngleRad), 0, 0, Mathf.Cos(sunAngleRad));

        // 2. Cloud Projection Movement

        // Update offset based on speed
        _cloudOffset += speed * dt * 10f;

        //// 3. Visual Micro-Variations

        float varOffset = _cloudOffset + (Mathf.Sin(Time.time * 2) * rotationAmplitude);

        //// Apply final direct transformation
        sunLight.transform.rotation = Quaternion.Euler(sunAngleRad, 0, varOffset);

        // Adjust light intensity based on sun position
        sunLight.intensity = Mathf.Lerp(0.0f, 1.5f, Mathf.Max(0, sunDot + 0.1f));
    }
}