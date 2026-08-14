using UnityEngine;

/// <summary>
/// Attach to any GameObject with a Collider and Rigidbody to make it pickable.
/// While the player holds left-click on this object, it floats in front of the camera.
/// Requires the camera to have a "PickupHolder" tag or uses a default hold position.
/// 
/// Works with SimpleCameraController or any camera-based raycast setup.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Pickable : MonoBehaviour, IInteractable
{
    [Header("Hold Settings")]
    [Tooltip("Distance in front of the camera to hold the object.")]
    [SerializeField] private float holdDistance = 2f;

    [Tooltip("How quickly the object moves toward the hold position.")]
    [SerializeField] private float followSpeed = 10f;

    [Tooltip("How quickly the object stops rotating when held.")]
    [SerializeField] private float rotationDamping = 5f;

    [Header("Push / Pull (Mouse Wheel)")]
    [Tooltip("How much the hold distance changes per scroll tick.")]
    [SerializeField] private float scrollSensitivity = 0.5f;

    [Tooltip("Minimum hold distance (closest you can pull the object).")]
    [SerializeField] private float minHoldDistance = 1f;

    [Tooltip("Maximum hold distance (farthest you can push the object).")]
    [SerializeField] private float maxHoldDistance = 8f;

    private Rigidbody _rb;
    private bool _isHeld;
    private Transform _holder;
    private float _defaultHoldDistance;

    // Cached original rigidbody settings
    private bool _originalUseGravity;
    private float _originalDrag;
    private float _originalAngularDrag;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _defaultHoldDistance = holdDistance;
    }

    private void Update()
    {
        if (!_isHeld)
            return;

        // Drop the object when left-click is released
        if (Input.GetMouseButtonUp(0))
        {
            Drop();
            return;
        }

        // Mouse wheel to push/pull the held object
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            holdDistance += scroll * scrollSensitivity;
            holdDistance = Mathf.Clamp(holdDistance, minHoldDistance, maxHoldDistance);
        }
    }

    private void FixedUpdate()
    {
        if (!_isHeld || _holder == null)
            return;

        // Calculate target position in front of the holder (camera)
        Vector3 targetPosition = _holder.position + _holder.forward * holdDistance;

        // Smoothly move toward hold position using velocity
        Vector3 direction = targetPosition - transform.position;
        _rb.linearVelocity = direction * followSpeed;

        // Dampen rotation so the object doesn't spin wildly
        _rb.angularVelocity = Vector3.Lerp(_rb.angularVelocity, Vector3.zero, rotationDamping * Time.fixedDeltaTime);
    }

    /// <summary>
    /// Called by the interaction system (raycast left-click).
    /// </summary>
    public void Interact()
    {
        if (_isHeld)
            return;

        Pickup();
    }

    private void Pickup()
    {
        // Find the main camera as the hold reference
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[Pickable] No main camera found. Cannot pick up object.");
            return;
        }

        _holder = cam.transform;
        _isHeld = true;
        holdDistance = _defaultHoldDistance;

        // Store original rigidbody settings
        _originalUseGravity = _rb.useGravity;
        _originalDrag = _rb.linearDamping;
        _originalAngularDrag = _rb.angularDamping;

        // Adjust rigidbody for holding
        _rb.useGravity = false;
        _rb.linearDamping = 10f;
        _rb.angularDamping = 10f;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Prevent the held object from pushing the player around
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        Debug.Log($"[Pickable] Picked up: {gameObject.name}");
    }

    private void Drop()
    {
        _isHeld = false;

        // Restore original rigidbody settings
        _rb.useGravity = _originalUseGravity;
        _rb.linearDamping = _originalDrag;
        _rb.angularDamping = _originalAngularDrag;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.interpolation = RigidbodyInterpolation.None;

        _holder = null;

        Debug.Log($"[Pickable] Dropped: {gameObject.name}");
    }

    /// <summary>
    /// Force-drop the object (e.g., if the player dies or a menu opens).
    /// </summary>
    public void ForceDrop()
    {
        if (_isHeld)
        {
            Drop();
        }
    }

    /// <summary>
    /// Whether this object is currently being held.
    /// </summary>
    public bool IsHeld => _isHeld;
}
