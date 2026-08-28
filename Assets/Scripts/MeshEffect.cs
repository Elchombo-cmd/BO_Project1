using System.Collections;
using UnityEngine;

/// <summary>
/// A single, self-contained mesh effect. Attach to any GameObject that has a mesh
/// (MeshFilter + MeshRenderer), assign an <see cref="InteractionConfig"/> asset in the
/// Inspector, and the matching handler runs whenever the effect is played.
///
/// All tuneable values (colours, speeds, durations, etc.) live in the
/// <see cref="InteractionConfig"/> ScriptableObject — no magic numbers here.
///
/// Setup:
/// 1. Attach to the mesh object you want to affect.
/// 2. Drag an InteractionConfig asset into the Config slot.
/// 3. Trigger it in one of two ways:
///    - Click it in game. This implements <see cref="IInteractable"/>, so it needs a Collider.
///    - Call <see cref="Play"/> from another script or wire it to a UnityEvent.
///
/// The animated effects (scale, rotate, move) ignore new triggers while already running, so
/// spam-clicking will not stack them.
/// </summary>
[DisallowMultipleComponent]
public class MeshEffect : MonoBehaviour, IInteractable
{
    /// <summary>The effects this component can apply to its mesh.</summary>
    public enum EffectType
    {
        ChangeColour,
        ToggleVisibility,
        ScaleUpDown,
        RotateBriefly,
        MoveBetweenPoints
    }

    [Header("Configuration")]
    [Tooltip("ScriptableObject asset that holds every tuneable value for this effect.")]
    [SerializeField] private InteractionConfig config;

    [Header("Move Between Points — Scene Reference")]
    [Tooltip("Optional destination marker. Leave empty to use the starting position plus the config's Move Offset.")]
    [SerializeField] private Transform destination;

    private Renderer _renderer;
    private Color _originalColour;
    private Vector3 _originalScale;
    private Quaternion _originalRotation;
    private Vector3 _pointA;
    private bool _isAtPointA = true;
    private bool _isPlaying;

    /// <summary>True while an animated effect is mid-run.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>The config asset driving this effect. Exposed so editors / tests can read it.</summary>
    public InteractionConfig Config => config;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();

        if (_renderer != null)
            _originalColour = _renderer.material.color;

        _originalScale = transform.localScale;
        _originalRotation = transform.localRotation;
        _pointA = transform.position;

        WarnIfMisconfigured();
    }

    /// <summary>Called by the player's interaction system (left-click). Plays the chosen effect.</summary>
    public void Interact() => Play();

    /// <summary>
    /// Runs the effect selected in the config asset. Safe to call from a UnityEvent.
    /// </summary>
    public void Play()
    {
        if (config == null)
        {
            Debug.LogWarning($"[MeshEffect] {gameObject.name}: No InteractionConfig assigned.", this);
            return;
        }

        switch (config.effect)
        {
            case EffectType.ChangeColour:
                ChangeColour();
                break;

            case EffectType.ToggleVisibility:
                ToggleVisibility();
                break;

            case EffectType.ScaleUpDown:
                ScaleUpDown();
                break;

            case EffectType.RotateBriefly:
                RotateBriefly();
                break;

            case EffectType.MoveBetweenPoints:
                MoveBetweenPoints();
                break;
        }
    }

    /// <summary>
    /// Swaps the mesh colour between its original and the config's target colour.
    /// </summary>
    public void ChangeColour()
    {
        if (_renderer == null)
            return;

        _renderer.material.color = _renderer.material.color == _originalColour
            ? config.targetColour
            : _originalColour;
    }

    /// <summary>
    /// Shows or hides the mesh. The GameObject stays active so colliders and scripts keep working.
    /// </summary>
    public void ToggleVisibility()
    {
        if (_renderer == null)
            return;

        _renderer.enabled = !_renderer.enabled;
    }

    /// <summary>Punches the mesh up to the config's scale multiplier and back down.</summary>
    public void ScaleUpDown()
    {
        if (_isPlaying)
            return;

        StartCoroutine(ScaleRoutine());
    }

    /// <summary>Spins the mesh for the config's rotate duration, then stops.</summary>
    public void RotateBriefly()
    {
        if (_isPlaying)
            return;

        StartCoroutine(RotateRoutine());
    }

    /// <summary>Slides the mesh to the other of its two points. Each call reverses direction.</summary>
    public void MoveBetweenPoints()
    {
        if (_isPlaying)
            return;

        StartCoroutine(MoveRoutine());
    }

    /// <summary>
    /// Snaps the mesh back to how it started: original colour, visible, original scale,
    /// rotation and position. Stops any running effect.
    /// </summary>
    public void ResetToOriginal()
    {
        StopAllCoroutines();
        _isPlaying = false;

        if (_renderer != null)
        {
            _renderer.enabled = true;
            _renderer.material.color = _originalColour;
        }

        transform.localScale = _originalScale;
        transform.localRotation = _originalRotation;
        transform.position = _pointA;
        _isAtPointA = true;
    }

    private IEnumerator ScaleRoutine()
    {
        _isPlaying = true;

        Vector3 peakScale = _originalScale * config.scaleMultiplier;
        float elapsed = 0f;

        while (elapsed < config.scaleDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / config.scaleDuration);
            float blend = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(progress * 2f - 1f));
            transform.localScale = Vector3.LerpUnclamped(_originalScale, peakScale, blend);
            yield return null;
        }

        transform.localScale = _originalScale;
        _isPlaying = false;
    }

    private IEnumerator RotateRoutine()
    {
        _isPlaying = true;

        Vector3 axis = config.rotateAxis.sqrMagnitude < Mathf.Epsilon
            ? Vector3.up
            : config.rotateAxis.normalized;

        float elapsed = 0f;

        while (elapsed < config.rotateDuration)
        {
            elapsed += Time.deltaTime;
            transform.Rotate(axis, config.rotateSpeed * Time.deltaTime, Space.Self);
            yield return null;
        }

        if (config.returnToStartRotation)
        {
            Quaternion landedRotation = transform.localRotation;
            float settleDuration = 0.2f;
            float settleElapsed = 0f;

            while (settleElapsed < settleDuration)
            {
                settleElapsed += Time.deltaTime;
                float blend = Mathf.SmoothStep(0f, 1f, settleElapsed / settleDuration);
                transform.localRotation = Quaternion.Slerp(landedRotation, _originalRotation, blend);
                yield return null;
            }

            transform.localRotation = _originalRotation;
        }

        _isPlaying = false;
    }

    private IEnumerator MoveRoutine()
    {
        _isPlaying = true;

        Vector3 pointB = destination != null ? destination.position : _pointA + config.moveOffset;
        Vector3 from = _isAtPointA ? _pointA : pointB;
        Vector3 to = _isAtPointA ? pointB : _pointA;
        float elapsed = 0f;

        while (elapsed < config.moveDuration)
        {
            elapsed += Time.deltaTime;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / config.moveDuration));
            transform.position = Vector3.Lerp(from, to, blend);
            yield return null;
        }

        transform.position = to;
        _isAtPointA = !_isAtPointA;
        _isPlaying = false;
    }

    private void WarnIfMisconfigured()
    {
        if (config == null)
        {
            Debug.LogWarning(
                $"[MeshEffect] {gameObject.name}: No InteractionConfig assigned. " +
                "Drag one into the Config slot in the Inspector.", this);
            return;
        }

        bool needsRenderer = config.effect == EffectType.ChangeColour ||
                             config.effect == EffectType.ToggleVisibility;

        if (needsRenderer && _renderer == null)
        {
            Debug.LogWarning(
                $"[MeshEffect] {gameObject.name}: '{config.effect}' needs a Renderer but none was found. " +
                "Attach this to an object with a MeshRenderer.", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (config == null || config.effect != EffectType.MoveBetweenPoints)
            return;

        Vector3 pointA = Application.isPlaying ? _pointA : transform.position;
        Vector3 pointB = destination != null ? destination.position : pointA + config.moveOffset;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pointA, pointB);
        Gizmos.DrawWireSphere(pointB, 0.15f);
    }
}
