using System.Collections;
using UnityEngine;

/// <summary>
/// A single, self-contained mesh effect. Attach to any GameObject that has a mesh
/// (MeshFilter + MeshRenderer), pick an <see cref="EffectType"/> in the Inspector, and the
/// matching handler runs whenever the effect is played.
///
/// Setup:
/// 1. Attach to the mesh object you want to affect.
/// 2. Choose Effect in the Inspector. Only the settings group for that effect is used;
///    the others are ignored.
/// 3. Trigger it in one of two ways:
///    - Click it in game. This implements <see cref="IInteractable"/>, so it needs a Collider.
///    - Call <see cref="Play"/> from another script or wire it to a UnityEvent, for example a
///      <see cref="PlacementTrigger"/> On Activated event.
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

    [Header("Effect")]
    [Tooltip("Which effect this component applies. Only the matching settings group below is used.")]
    [SerializeField] private EffectType effect = EffectType.ChangeColour;

    [Header("Change Colour")]
    [Tooltip("Colour to switch to. Playing the effect again switches back to the original colour.")]
    [SerializeField] private Color targetColour = Color.red;

    [Header("Scale Up Down")]
    [Tooltip("Peak scale as a multiple of the starting scale. 1.5 = 50% bigger at the peak.")]
    [Min(0.01f)]
    [SerializeField] private float scaleMultiplier = 1.5f;

    [Tooltip("Seconds for the full up-and-back-down cycle.")]
    [Min(0.01f)]
    [SerializeField] private float scaleDuration = 0.5f;

    [Header("Rotate Briefly")]
    [Tooltip("Rotation axis in the object's LOCAL space. (0,1,0) spins like a turntable.")]
    [SerializeField] private Vector3 rotateAxis = Vector3.up;

    [Tooltip("Spin speed in degrees per second.")]
    [SerializeField] private float rotateSpeed = 360f;

    [Tooltip("How many seconds to spin for.")]
    [Min(0.01f)]
    [SerializeField] private float rotateDuration = 1f;

    [Tooltip("Ease back to the original rotation when the spin finishes, instead of stopping wherever it lands.")]
    [SerializeField] private bool returnToStartRotation = true;

    [Header("Move Between Points")]
    [Tooltip("Optional destination marker. Leave empty to use the starting position plus Move Offset.")]
    [SerializeField] private Transform destination;

    [Tooltip("Used only when Destination is empty. Offset from the starting position, in WORLD space.")]
    [SerializeField] private Vector3 moveOffset = new Vector3(0f, 0f, 3f);

    [Tooltip("Seconds to travel between the two points.")]
    [Min(0.01f)]
    [SerializeField] private float moveDuration = 1f;

    private Renderer _renderer;
    private Color _originalColour;
    private Vector3 _originalScale;
    private Quaternion _originalRotation;
    private Vector3 _pointA;
    private bool _isAtPointA = true;
    private bool _isPlaying;

    /// <summary>True while an animated effect is mid-run.</summary>
    public bool IsPlaying => _isPlaying;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();

        if (_renderer != null)
            _originalColour = _renderer.material.color;

        _originalScale = transform.localScale;
        _originalRotation = transform.localRotation;
        _pointA = transform.position;

        WarnIfRendererMissing();
    }

    /// <summary>Called by the player's interaction system (left-click). Plays the chosen effect.</summary>
    public void Interact() => Play();

    /// <summary>
    /// Runs the effect selected in the Inspector. Safe to call from a UnityEvent.
    /// </summary>
    public void Play()
    {
        switch (effect)
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
    /// Swaps the mesh colour between its original and <c>Target Colour</c>. Calling this again
    /// swaps it back.
    /// </summary>
    public void ChangeColour()
    {
        if (_renderer == null)
            return;

        // Reading .material gives this object its own material instance, so sibling objects
        // sharing the same material are left alone.
        _renderer.material.color = _renderer.material.color == _originalColour
            ? targetColour
            : _originalColour;
    }

    /// <summary>
    /// Shows or hides the mesh. The GameObject stays active, so colliders and scripts keep
    /// working; only the rendering is switched off.
    /// </summary>
    public void ToggleVisibility()
    {
        if (_renderer == null)
            return;

        _renderer.enabled = !_renderer.enabled;
    }

    /// <summary>Punches the mesh up to <c>Scale Multiplier</c> and back down to its original scale.</summary>
    public void ScaleUpDown()
    {
        if (_isPlaying)
            return;

        StartCoroutine(ScaleRoutine());
    }

    /// <summary>Spins the mesh for <c>Rotate Duration</c> seconds, then stops.</summary>
    public void RotateBriefly()
    {
        if (_isPlaying)
            return;

        StartCoroutine(RotateRoutine());
    }

    /// <summary>
    /// Slides the mesh to the other of its two points. Each call reverses the direction.
    /// </summary>
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

        Vector3 peakScale = _originalScale * scaleMultiplier;
        float elapsed = 0f;

        while (elapsed < scaleDuration)
        {
            elapsed += Time.deltaTime;

            // PingPong the 0..1 progress so it grows for the first half and shrinks for the second.
            float progress = Mathf.Clamp01(elapsed / scaleDuration);
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

        Vector3 axis = rotateAxis.sqrMagnitude < Mathf.Epsilon ? Vector3.up : rotateAxis.normalized;
        float elapsed = 0f;

        while (elapsed < rotateDuration)
        {
            elapsed += Time.deltaTime;
            transform.Rotate(axis, rotateSpeed * Time.deltaTime, Space.Self);
            yield return null;
        }

        if (returnToStartRotation)
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

        Vector3 pointB = destination != null ? destination.position : _pointA + moveOffset;
        Vector3 from = _isAtPointA ? _pointA : pointB;
        Vector3 to = _isAtPointA ? pointB : _pointA;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
            transform.position = Vector3.Lerp(from, to, blend);
            yield return null;
        }

        transform.position = to;
        _isAtPointA = !_isAtPointA;
        _isPlaying = false;
    }

    private void WarnIfRendererMissing()
    {
        bool needsRenderer = effect == EffectType.ChangeColour || effect == EffectType.ToggleVisibility;

        if (needsRenderer && _renderer == null)
        {
            Debug.LogWarning(
                $"[MeshEffect] {gameObject.name}: '{effect}' needs a Renderer but none was found. " +
                "Attach this to an object with a MeshRenderer.", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (effect != EffectType.MoveBetweenPoints)
            return;

        // In edit mode _pointA is not set yet, so fall back to the live position.
        Vector3 pointA = Application.isPlaying ? _pointA : transform.position;
        Vector3 pointB = destination != null ? destination.position : pointA + moveOffset;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pointA, pointB);
        Gizmos.DrawWireSphere(pointB, 0.15f);
    }
}
