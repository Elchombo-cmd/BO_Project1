using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Generic "put the right thing here" trigger.
///
/// Attach to any GameObject (pedestal, lock, plate, socket, altar...) and list the
/// object(s) that satisfy it in the Inspector. When those objects are placed on or
/// near this object, <see cref="onActivated"/> fires. If they are taken away again,
/// <see cref="onDeactivated"/> fires (unless one-shot or snapping is enabled).
///
/// Because every field is serialized, the same script covers every lock/socket in the
/// game — each instance just references different accepted objects.
///
/// Setup:
/// 1. Drop this on the receiving object.
/// 2. Drag the accepted object(s) into Accepted Objects, and/or type accepted tags.
/// 3. Hook up On Activated (e.g. DoorLock.Unlock, animation, audio, score...).
/// No trigger collider is required in Proximity mode; the radius is drawn as a gizmo.
/// </summary>
[DisallowMultipleComponent]
public class PlacementTrigger : MonoBehaviour
{
    public enum DetectionMode
    {
        /// <summary>Sphere check around this object (or Detection Point). No collider setup needed.</summary>
        Proximity,

        /// <summary>Uses OnTriggerEnter/Exit from a trigger Collider on this object.</summary>
        TriggerCollider
    }

    [Header("What Counts")]
    [Tooltip("Specific objects that satisfy this trigger. Leave empty to accept by tag only.")]
    [SerializeField] private List<GameObject> acceptedObjects = new List<GameObject>();

    [Tooltip("Any object with one of these tags satisfies this trigger. Leave empty to accept by reference only.")]
    [SerializeField] private List<string> acceptedTags = new List<string>();

    [Tooltip("ON: every entry in Accepted Objects must be present at once. OFF: any single match is enough.")]
    [SerializeField] private bool requireAllAcceptedObjects = true;

    [Tooltip("How many matching objects are needed. Ignored when Require All Accepted Objects is on.")]
    [Min(1)]
    [SerializeField] private int requiredMatchCount = 1;

    [Header("Detection")]
    [SerializeField] private DetectionMode detectionMode = DetectionMode.Proximity;

    [Tooltip("Where the sphere check is centred. Defaults to this transform.")]
    [SerializeField] private Transform detectionPoint;

    [Tooltip("Radius of the 'on or near' sphere check (Proximity mode).")]
    [Min(0.01f)]
    [SerializeField] private float detectionRadius = 0.5f;

    [Tooltip("Layers to look at. Set to Everything if unsure.")]
    [SerializeField] private LayerMask detectionLayers = ~0;

    [Tooltip("Seconds between checks. 0 = every frame.")]
    [Min(0f)]
    [SerializeField] private float checkInterval = 0.1f;

    [Header("Placement Rules")]
    [Tooltip("Object must not be held by the player (uses Pickable) to count as placed.")]
    [SerializeField] private bool mustNotBeHeld = true;

    [Tooltip("Object must be roughly at rest to count as placed. 0 disables the check.")]
    [Min(0f)]
    [SerializeField] private float maxRestingSpeed = 0.35f;

    [Header("Response")]
    [Tooltip("Fire only once, then stop checking.")]
    [SerializeField] private bool oneShot = true;

    [Tooltip("Fire On Deactivated when the objects are removed again. Ignored when One Shot is on.")]
    [SerializeField] private bool deactivateWhenRemoved = false;

    [Tooltip("Optional: attach placed objects to this transform. They then move and rotate with it, " +
             "so a snap point parented to a door leaf carries the object as the door swings.")]
    [SerializeField] private Transform snapPoint;

    [Tooltip("Keep the snapped object's own rotation instead of matching the snap point.")]
    [SerializeField] private bool preserveRotationOnSnap = false;

    [Header("Events")]
    [Tooltip("Fired when the required object(s) are placed.")]
    [SerializeField] private UnityEvent onActivated = new UnityEvent();

    [Tooltip("Fired when the required object(s) are no longer placed.")]
    [SerializeField] private UnityEvent onDeactivated = new UnityEvent();

    /// <summary>
    /// Everything needed to hold an object on the snap point and to put it back as it was.
    /// </summary>
    private class SnappedObject
    {
        public GameObject Object;
        public Transform OriginalParent;
        public Rigidbody Body;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public bool WasKinematic;
        public bool UsedGravity;
        public RigidbodyInterpolation Interpolation;
    }

    private readonly HashSet<GameObject> _overlapping = new HashSet<GameObject>();
    private readonly List<GameObject> _placed = new List<GameObject>();
    private readonly List<SnappedObject> _snapped = new List<SnappedObject>();
    private readonly Collider[] _overlapBuffer = new Collider[32];

    private bool _isActive;
    private float _nextCheckTime;

    /// <summary>Whether the required object(s) are currently placed.</summary>
    public bool IsActive => _isActive;

    /// <summary>The objects currently counting as placed here.</summary>
    public IReadOnlyList<GameObject> PlacedObjects => _placed;

    private void Update()
    {
        if (oneShot && _isActive)
            return;

        if (Time.time < _nextCheckTime)
            return;

        _nextCheckTime = Time.time + checkInterval;
        Evaluate();
    }

    /// <summary>
    /// Re-applies the anchored pose of every snapped object after physics has run.
    /// A Rigidbody nested under another transform is posed by the physics engine, not by its
    /// parent, so without this a snapped object would stay behind in world space while the
    /// snap point (e.g. a swinging door leaf) moves away from it.
    /// </summary>
    private void LateUpdate()
    {
        if (snapPoint == null)
            return;

        for (int i = 0; i < _snapped.Count; i++)
        {
            SnappedObject snapped = _snapped[i];

            // Objects without a Rigidbody already follow the hierarchy on their own.
            if (snapped.Object == null || snapped.Body == null)
                continue;

            Transform t = snapped.Object.transform;
            if (t.parent != snapPoint)
                continue;

            t.localPosition = snapped.LocalPosition;
            t.localRotation = snapped.LocalRotation;
        }
    }

    // --- Trigger collider mode -------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (detectionMode != DetectionMode.TriggerCollider)
            return;

        GameObject candidate = ResolveObject(other);
        if (candidate != null && IsAccepted(candidate))
            _overlapping.Add(candidate);
    }

    private void OnTriggerExit(Collider other)
    {
        if (detectionMode != DetectionMode.TriggerCollider)
            return;

        GameObject candidate = ResolveObject(other);
        if (candidate != null)
            _overlapping.Remove(candidate);
    }

    // --- Core ------------------------------------------------------------------

    private void Evaluate()
    {
        if (detectionMode == DetectionMode.Proximity)
            RefreshOverlapsByProximity();

        _placed.Clear();
        _overlapping.RemoveWhere(obj => obj == null);

        foreach (GameObject obj in _overlapping)
        {
            if (IsPlaced(obj))
                _placed.Add(obj);
        }

        bool satisfied = IsSatisfied();

        if (satisfied && !_isActive)
        {
            Activate();
        }
        else if (!satisfied && _isActive && deactivateWhenRemoved)
        {
            Deactivate();
        }
    }

    private void RefreshOverlapsByProximity()
    {
        _overlapping.Clear();

        Vector3 center = (detectionPoint != null ? detectionPoint : transform).position;
        int hitCount = Physics.OverlapSphereNonAlloc(
            center, detectionRadius, _overlapBuffer, detectionLayers, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hitCount; i++)
        {
            GameObject candidate = ResolveObject(_overlapBuffer[i]);
            if (candidate != null && IsAccepted(candidate))
                _overlapping.Add(candidate);
        }

        // Snapped objects have their colliders disabled, so keep counting them.
        for (int i = 0; i < _snapped.Count; i++)
        {
            if (_snapped[i].Object != null)
                _overlapping.Add(_snapped[i].Object);
        }
    }

    private bool IsSatisfied()
    {
        if (requireAllAcceptedObjects && acceptedObjects.Count > 0)
        {
            for (int i = 0; i < acceptedObjects.Count; i++)
            {
                if (acceptedObjects[i] != null && !_placed.Contains(acceptedObjects[i]))
                    return false;
            }

            return true;
        }

        return _placed.Count >= requiredMatchCount;
    }

    /// <summary>Resolves a collider to the object that should be matched (the rigidbody root if there is one).</summary>
    private GameObject ResolveObject(Collider col)
    {
        if (col == null)
            return null;

        GameObject candidate = col.attachedRigidbody != null
            ? col.attachedRigidbody.gameObject
            : col.gameObject;

        // Never match ourselves or our own children.
        return candidate.transform.IsChildOf(transform) ? null : candidate;
    }

    private bool IsAccepted(GameObject candidate)
    {
        for (int i = 0; i < acceptedObjects.Count; i++)
        {
            if (acceptedObjects[i] == candidate)
                return true;
        }

        for (int i = 0; i < acceptedTags.Count; i++)
        {
            if (!string.IsNullOrEmpty(acceptedTags[i]) && candidate.CompareTag(acceptedTags[i]))
                return true;
        }

        return false;
    }

    /// <summary>An accepted object only counts once it is actually put down and settled.</summary>
    private bool IsPlaced(GameObject candidate)
    {
        if (IsSnapped(candidate))
            return true;

        if (mustNotBeHeld)
        {
            Pickable pickable = candidate.GetComponent<Pickable>();
            if (pickable != null && pickable.IsHeld)
                return false;
        }

        if (maxRestingSpeed > 0f)
        {
            Rigidbody rb = candidate.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic && rb.linearVelocity.magnitude > maxRestingSpeed)
                return false;
        }

        return true;
    }

    private void Activate()
    {
        _isActive = true;

        if (snapPoint != null)
        {
            for (int i = 0; i < _placed.Count; i++)
                Snap(_placed[i]);
        }

        onActivated.Invoke();
    }

    private void Deactivate()
    {
        _isActive = false;
        onDeactivated.Invoke();
    }

    private bool IsSnapped(GameObject candidate)
    {
        for (int i = 0; i < _snapped.Count; i++)
        {
            if (_snapped[i].Object == candidate)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Attaches an object to the snap point so it moves and rotates with it.
    /// </summary>
    private void Snap(GameObject obj)
    {
        if (IsSnapped(obj))
            return;

        SnappedObject snapped = new SnappedObject
        {
            Object = obj,
            OriginalParent = obj.transform.parent
        };

        Pickable pickable = obj.GetComponent<Pickable>();
        if (pickable != null)
        {
            pickable.ForceDrop();
            pickable.enabled = false;
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            snapped.Body = rb;
            snapped.WasKinematic = rb.isKinematic;
            snapped.UsedGravity = rb.useGravity;
            snapped.Interpolation = rb.interpolation;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;

            // Interpolation would overwrite the pose we set in LateUpdate.
            rb.interpolation = RigidbodyInterpolation.None;
        }

        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        // Keep the world pose while reparenting so preserveRotationOnSnap has something to keep.
        obj.transform.SetParent(snapPoint, true);
        obj.transform.localPosition = Vector3.zero;
        if (!preserveRotationOnSnap)
            obj.transform.localRotation = Quaternion.identity;

        // The pose LateUpdate re-applies every frame.
        snapped.LocalPosition = obj.transform.localPosition;
        snapped.LocalRotation = obj.transform.localRotation;

        _snapped.Add(snapped);
    }

    /// <summary>
    /// Clears the activated state (and releases any snapped objects) so the trigger can be used again.
    /// </summary>
    public void ResetTrigger()
    {
        for (int i = 0; i < _snapped.Count; i++)
        {
            SnappedObject snapped = _snapped[i];
            GameObject obj = snapped.Object;
            if (obj == null)
                continue;

            obj.transform.SetParent(snapped.OriginalParent, true);

            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            for (int c = 0; c < colliders.Length; c++)
                colliders[c].enabled = true;

            if (snapped.Body != null)
            {
                snapped.Body.isKinematic = snapped.WasKinematic;
                snapped.Body.useGravity = snapped.UsedGravity;
                snapped.Body.interpolation = snapped.Interpolation;
            }

            Pickable pickable = obj.GetComponent<Pickable>();
            if (pickable != null)
                pickable.enabled = true;
        }

        _snapped.Clear();
        _overlapping.Clear();
        _placed.Clear();

        if (_isActive)
            Deactivate();
    }

    private void OnDrawGizmosSelected()
    {
        if (detectionMode != DetectionMode.Proximity)
            return;

        Vector3 center = (detectionPoint != null ? detectionPoint : transform).position;
        Gizmos.color = _isActive ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0.8f, 0f, 0.35f);
        Gizmos.DrawWireSphere(center, detectionRadius);
    }
}
