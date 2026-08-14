using UnityEngine;

/// <summary>
/// A door that stays frozen until something unlocks it, then swings freely on a hinge.
///
/// This script knows nothing about keys. Anything can unlock it by calling <see cref="Unlock"/> —
/// typically a <see cref="PlacementTrigger"/> On Activated event.
///
/// Setup:
/// 1. Attach to the door leaf (the part that swings). It needs a Collider — a thin BoxCollider
///    matching the leaf is ideal. A non-convex MeshCollider cannot swing; this script will
///    convert it and warn.
/// 2. Set Hinge Anchor Point to an empty child sitting on the hinge edge.
/// 3. On the lock/socket object, add a PlacementTrigger and wire On Activated -> DoorLock.Unlock.
/// </summary>
public class DoorLock : MonoBehaviour
{
    [Header("Hinge")]
    [Tooltip("Empty child placed on the hinge edge. The door rotates around this point. " +
             "If null, the door rotates around its own origin, which is rarely what you want.")]
    [SerializeField] private Transform hingeAnchorPoint;

    [Tooltip("Hinge axis in WORLD space. (0,1,0) is a normal vertical door hinge. " +
             "This is converted to the door's local space automatically, so the door's own " +
             "rotation and its parents' rotations no longer matter.")]
    [SerializeField] private Vector3 hingeAxisWorld = Vector3.up;

    [Header("Swing Limits")]
    [Tooltip("Use the min/max angles below. Turn off for a door that spins all the way round.")]
    [SerializeField] private bool useSwingLimits = true;

    [Tooltip("Minimum swing angle (degrees from the closed position).")]
    [SerializeField] private float minSwingAngle = -90f;

    [Tooltip("Maximum swing angle (degrees from the closed position).")]
    [SerializeField] private float maxSwingAngle = 90f;

    [Header("Feel")]
    [Tooltip("Mass of the door once it can swing. Keep it close to the mass of the things " +
             "that push it (pickable props are usually 1) or it will barely budge.")]
    [Min(0.01f)]
    [SerializeField] private float doorMass = 1f;

    [Tooltip("Resistance while swinging. 0 = swings forever, 1+ = heavy and slow.")]
    [Min(0f)]
    [SerializeField] private float swingDamping = 0.5f;

    [Tooltip("Let gravity act on the door. Only useful for trapdoors or deliberately sagging doors.")]
    [SerializeField] private bool useGravityWhenUnlocked = false;

    [Header("Optional")]
    [Tooltip("If true, the door starts unlocked (no trigger required). Handy for testing the swing.")]
    [SerializeField] private bool startsUnlocked = false;

    private bool _isUnlocked;
    private Rigidbody _rb;
    private HingeJoint _hingeJoint;

    /// <summary>Whether this door has been unlocked.</summary>
    public bool IsUnlocked => _isUnlocked;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        ValidateSetup();
    }

    private void Start()
    {
        if (startsUnlocked)
            Unlock();
        else
            Relock();
    }

    /// <summary>
    /// Releases the door so it can swing. Safe to call more than once.
    /// Hook this up to PlacementTrigger's On Activated event.
    /// </summary>
    public void Unlock()
    {
        if (_isUnlocked)
            return;

        _isUnlocked = true;

        // The hinge is what constrains the door. Rigidbody constraints must NOT be used here:
        // FreezePosition stops the door orbiting the hinge anchor and it ends up spinning
        // around its own centre of mass instead (or not moving at all).
        _rb.constraints = RigidbodyConstraints.None;

        _rb.isKinematic = false;
        _rb.useGravity = useGravityWhenUnlocked;
        _rb.mass = doorMass;
        _rb.angularDamping = swingDamping;
        _rb.linearDamping = 0f;

        // Door leaves are thin, so be generous about catching fast-moving props.
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Helps the hinge limits hold up when something slams into the door.
        _rb.solverIterations = 20;
        _rb.solverVelocityIterations = 8;

        if (_hingeJoint == null)
            _hingeJoint = gameObject.AddComponent<HingeJoint>();

        _hingeJoint.autoConfigureConnectedAnchor = true;

        _hingeJoint.anchor = hingeAnchorPoint != null
            ? transform.InverseTransformPoint(hingeAnchorPoint.position)
            : Vector3.zero;

        // HingeJoint.axis is local, so convert the desired world axis into local space.
        Vector3 axis = hingeAxisWorld.sqrMagnitude > 0.0001f ? hingeAxisWorld.normalized : Vector3.up;
        _hingeJoint.axis = transform.InverseTransformDirection(axis);

        _hingeJoint.useMotor = false;
        _hingeJoint.useSpring = false;
        _hingeJoint.useLimits = useSwingLimits;

        if (useSwingLimits)
        {
            _hingeJoint.limits = new JointLimits
            {
                min = Mathf.Min(minSwingAngle, maxSwingAngle),
                max = Mathf.Max(minSwingAngle, maxSwingAngle),
                bounciness = 0f,
                bounceMinVelocity = 0f,
                contactDistance = 0f
            };
        }

        Debug.Log($"[DoorLock] Unlocked: {gameObject.name}");
    }

    /// <summary>
    /// Freezes the door again. Hook this up to PlacementTrigger's On Deactivated event if
    /// removing the key should re-lock the door.
    /// </summary>
    public void Relock()
    {
        _isUnlocked = false;

        if (_hingeJoint != null)
        {
            Destroy(_hingeJoint);
            _hingeJoint = null;
        }

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        _rb.useGravity = false;
    }

    /// <summary>
    /// Warns about the setup mistakes that silently stop a door from swinging.
    /// </summary>
    private void ValidateSetup()
    {
        if (hingeAnchorPoint == null)
        {
            Debug.LogWarning($"[DoorLock] '{gameObject.name}' has no Hinge Anchor Point, so it will " +
                             "rotate around its own origin instead of the hinge edge.", this);
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        bool hasSolidCollider = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col.isTrigger)
                continue;

            hasSolidCollider = true;

            // A non-convex MeshCollider on a non-kinematic Rigidbody is not supported by PhysX:
            // the door would pass straight through everything and never get pushed.
            if (col is MeshCollider meshCollider && !meshCollider.convex)
            {
                meshCollider.convex = true;
                Debug.LogWarning($"[DoorLock] '{col.name}' had a non-convex MeshCollider, which cannot " +
                                 "be moved by physics. Set it to Convex at runtime — consider replacing " +
                                 "it with a BoxCollider for a door leaf.", this);
            }
        }

        if (!hasSolidCollider)
        {
            Debug.LogWarning($"[DoorLock] '{gameObject.name}' has no non-trigger Collider, so nothing " +
                             "can push it. Add a BoxCollider matching the door leaf.", this);
        }

        Vector3 scale = transform.lossyScale;
        if (Mathf.Abs(scale.x - scale.y) > 0.01f || Mathf.Abs(scale.y - scale.z) > 0.01f)
        {
            Debug.LogWarning($"[DoorLock] '{gameObject.name}' has a non-uniform scale {scale}. " +
                             "Joint anchors get skewed by this; if the pivot looks slightly off, " +
                             "bake the scale into the mesh or move the scaling to a parent.", this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 anchor = hingeAnchorPoint != null ? hingeAnchorPoint.position : transform.position;
        Vector3 axis = hingeAxisWorld.sqrMagnitude > 0.0001f ? hingeAxisWorld.normalized : Vector3.up;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(anchor - axis * 0.75f, anchor + axis * 0.75f);
        Gizmos.DrawWireSphere(anchor, 0.05f);
    }
}
