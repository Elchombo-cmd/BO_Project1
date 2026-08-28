using UnityEngine;

/// <summary>
/// Simulates a flame-based lamp by randomly flickering a point light's intensity,
/// colour temperature, and position. Attach to any GameObject that has a Light component.
///
/// All values are driven by layered Perlin noise so the flicker feels organic
/// rather than random.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlameLight : MonoBehaviour
{
    [Header("Intensity")]
    [Tooltip("Base intensity the flame hovers around.")]
    [SerializeField] private float baseIntensity = 2.5f;

    [Tooltip("How far above and below the base the flame can flicker.")]
    [SerializeField] private float intensityVariation = 0.6f;

    [Tooltip("How fast the intensity flickers. Higher = more frantic.")]
    [SerializeField] private float intensitySpeed = 3f;

    [Header("Colour")]
    [Tooltip("Warm colour at low flicker (deep orange / ember).")]
    [SerializeField] private Color colourLow = new Color(1f, 0.45f, 0.1f);

    [Tooltip("Bright colour at high flicker (yellow-white flame tip).")]
    [SerializeField] private Color colourHigh = new Color(1f, 0.85f, 0.5f);

    [Header("Range")]
    [Tooltip("Base range of the light.")]
    [SerializeField] private float baseRange = 10f;

    [Tooltip("How much the range wobbles with the flicker.")]
    [SerializeField] private float rangeVariation = 1.5f;

    [Header("Position Wobble")]
    [Tooltip("How far the light drifts from its starting position (simulates flame sway).")]
    [SerializeField] private float positionWobble = 0.04f;

    [Tooltip("How fast the position wobbles.")]
    [SerializeField] private float wobbleSpeed = 2f;

    [Header("Occasional Gutter")]
    [Tooltip("Chance per second of a brief dip (0 = never, 1 = constant).")]
    [Range(0f, 1f)]
    [SerializeField] private float gutterChance = 0.08f;

    [Tooltip("How much the intensity drops during a gutter.")]
    [SerializeField] private float gutterDepth = 0.7f;

    [Tooltip("How long a gutter lasts in seconds.")]
    [SerializeField] private float gutterDuration = 0.15f;

    private Light _light;
    private Vector3 _originLocalPos;
    private float _noiseOffsetA;
    private float _noiseOffsetB;
    private float _noiseOffsetC;
    private float _gutterTimer;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _originLocalPos = transform.localPosition;

        // Give each flame its own noise lane so multiple lamps don't flicker in sync.
        _noiseOffsetA = Random.Range(0f, 100f);
        _noiseOffsetB = Random.Range(0f, 100f);
        _noiseOffsetC = Random.Range(0f, 100f);
    }

    private void Update()
    {
        float t = Time.time;

        // --- Intensity ---
        // Layer two noise frequencies for a natural feel.
        float noiseSlow = Mathf.PerlinNoise(_noiseOffsetA + t * intensitySpeed, 0f);
        float noiseFast = Mathf.PerlinNoise(_noiseOffsetB + t * intensitySpeed * 3.7f, 0f);
        float combined = Mathf.Lerp(noiseSlow, noiseFast, 0.35f);             // mostly slow, hint of fast
        float flickerNorm = (combined - 0.5f) * 2f;                           // remap 0..1 → -1..1

        float intensity = baseIntensity + flickerNorm * intensityVariation;

        // --- Gutter (random brief dip) ---
        if (_gutterTimer > 0f)
        {
            _gutterTimer -= Time.deltaTime;
            float gutterBlend = Mathf.Clamp01(_gutterTimer / gutterDuration);
            intensity *= Mathf.Lerp(1f, 1f - gutterDepth, gutterBlend);
        }
        else if (Random.value < gutterChance * Time.deltaTime)
        {
            _gutterTimer = gutterDuration;
        }

        _light.intensity = Mathf.Max(intensity, 0f);

        // --- Colour ---
        float colourT = Mathf.InverseLerp(baseIntensity - intensityVariation,
                                           baseIntensity + intensityVariation,
                                           _light.intensity);
        _light.color = Color.Lerp(colourLow, colourHigh, colourT);

        // --- Range ---
        _light.range = baseRange + flickerNorm * rangeVariation;

        // --- Position wobble (flame sway) ---
        if (positionWobble > 0f)
        {
            float wx = (Mathf.PerlinNoise(_noiseOffsetC + t * wobbleSpeed, 0f) - 0.5f) * 2f;
            float wz = (Mathf.PerlinNoise(0f, _noiseOffsetC + t * wobbleSpeed) - 0.5f) * 2f;
            transform.localPosition = _originLocalPos + new Vector3(wx, 0f, wz) * positionWobble;
        }
    }
}
