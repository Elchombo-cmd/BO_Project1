using UnityEngine;

/// <summary>
/// Holds all configuration for a single <see cref="MeshEffect"/> behaviour.
/// Create one asset per effect type via Assets → Create → Interaction Config.
/// </summary>
[CreateAssetMenu(fileName = "NewInteractionConfig", menuName = "Interaction Config")]
public class InteractionConfig : ScriptableObject
{
    [Header("Effect")]
    [Tooltip("Which effect this config drives.")]
    public MeshEffect.EffectType effect = MeshEffect.EffectType.ChangeColour;

    [Header("Change Colour")]
    [Tooltip("Colour to switch to. Playing the effect again switches back to the original colour.")]
    public Color targetColour = Color.red;

    [Header("Scale Up Down")]
    [Tooltip("Peak scale as a multiple of the starting scale. 1.5 = 50 % bigger at the peak.")]
    [Min(0.01f)]
    public float scaleMultiplier = 1.5f;

    [Tooltip("Seconds for the full up-and-back-down cycle.")]
    [Min(0.01f)]
    public float scaleDuration = 0.5f;

    [Header("Rotate Briefly")]
    [Tooltip("Rotation axis in the object's LOCAL space. (0,1,0) spins like a turntable.")]
    public Vector3 rotateAxis = Vector3.up;

    [Tooltip("Spin speed in degrees per second.")]
    public float rotateSpeed = 360f;

    [Tooltip("How many seconds to spin for.")]
    [Min(0.01f)]
    public float rotateDuration = 1f;

    [Tooltip("Ease back to the original rotation when the spin finishes.")]
    public bool returnToStartRotation = true;

    [Header("Move Between Points")]
    [Tooltip("Offset from the starting position, in WORLD space (used when no destination Transform is set on the object).")]
    public Vector3 moveOffset = new Vector3(0f, 0f, 3f);

    [Tooltip("Seconds to travel between the two points.")]
    [Min(0.01f)]
    public float moveDuration = 1f;
}
