using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person camera controller with WASD/arrow movement, mouse look, left-click interaction,
/// object highlighting, and Pickable hold/drop support.
/// Uses the new Unity Input System exclusively — no legacy Input calls.
///
/// Attach this script to the main Camera (or a player object with a child Camera).
/// Requires a <see cref="CharacterController"/> and a PlayerInput component wired to
/// InputSystem_Actions.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintMultiplier = 1.8f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float maxLookAngle = 85f;
    [SerializeField] private bool invertY = false;

    [Header("Interaction")]
    [SerializeField] private float interactRange = 3f;
    [Tooltip("Layers the interaction trace is allowed to hit at all. Anything not on these " +
             "layers is completely ignored by the trace. Keep this to the layer(s) your " +
             "interactable objects and any solid walls that should block interaction live on.")]
    [SerializeField] private LayerMask interactableLayer = ~0;
    [Tooltip("Layers treated as solid blockers: if one of these is hit before an interactable, " +
             "the trace stops and nothing is interacted with (you can't reach through a wall). " +
             "Colliders that are neither interactable nor on a blocking layer are skipped, so " +
             "decoration meshes and triggers in front of an object don't get in the way.")]
    [SerializeField] private LayerMask blockingLayers = 0;
    [Tooltip("When enabled, trigger colliders are ignored by the interaction trace.")]
    [SerializeField] private bool ignoreTriggers = true;

    [Header("Highlight / Glow")]
    [Tooltip("Color of the glow outline when looking at an interactable object.")]
    [SerializeField] private Color glowColor = new Color(1f, 0.8f, 0.2f, 1f);

    [Header("Camera Position")]
    [Tooltip("Distance between the very top of the capsule and the camera (eye) height, so the " +
             "eyes sit just under the crown of the head. The capsule height is derived from the " +
             "camera's height above the floor plus this value.")]
    [SerializeField] private float eyeInset = 0.1f;

    [Header("Capsule Collision")]
    [Tooltip("Radius of the CharacterController capsule.")]
    [SerializeField] private float capsuleRadius = 0.5f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController _controller;
    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private float _verticalVelocity;
    private float _cameraPitch;
    private float _cameraYaw;
    private bool _isSprinting;
    private bool _hasSeparateCamera;

    // Capsule dimensions derived from the camera height (see SyncCapsuleToCamera).
    private float _capsuleHeight = 2f;
    private Vector3 _capsuleCenter = new Vector3(0f, 1f, 0f);

    // Pickable tracking — lets us forward drop/scroll to the held object.
    private Pickable _heldPickable;

    // Highlight tracking
    private GameObject _currentHighlighted;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        // If no camera reference assigned, try to find one
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
                cameraTransform = cam.transform;
            else
                cameraTransform = transform;
        }

        // Detect whether the camera is a separate child or the same object.
        _hasSeparateCamera = cameraTransform != null && cameraTransform != transform;

        SyncCapsuleToCamera();
    }

    /// <summary>
    /// Sizes the CharacterController capsule so its top reaches the camera's height above the
    /// floor. The camera's local Y (relative to the player root, which stands on the floor) is
    /// the eye height; the capsule top is that plus <see cref="eyeInset"/>, and the center is
    /// placed at half the height so the capsule bottom rests on the floor (local y = 0).
    /// </summary>
    private void SyncCapsuleToCamera()
    {
        if (_controller == null || !_hasSeparateCamera || cameraTransform == null)
            return;

        float eyeHeight = cameraTransform.localPosition.y;
        float newHeight = eyeHeight + eyeInset;

        // Guard against degenerate capsules: height must be at least 2 * radius.
        newHeight = Mathf.Max(newHeight, capsuleRadius * 2f);

        _capsuleHeight = newHeight;
        _capsuleCenter = new Vector3(0f, newHeight * 0.5f, 0f);

        _controller.height = _capsuleHeight;
        _controller.radius = capsuleRadius;
        _controller.center = _capsuleCenter;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Live-update in the editor while tweaking the camera position or radius.
        if (_controller == null)
            _controller = GetComponent<CharacterController>();

        _hasSeparateCamera = cameraTransform != null && cameraTransform != transform;

        SyncCapsuleToCamera();
    }
#endif

    private void Start()
    {
        // Lock and hide cursor for FPS controls
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialise from current orientation so we don't snap to zero.
        Vector3 euler = transform.eulerAngles;
        _cameraYaw = euler.y;
        _cameraPitch = euler.x;
    }

    private void Update()
    {
        HandleMouseLook();
        HandleMovement();
        HandleHighlight();
        HandleCursorToggle();
    }

    #region Movement

    private void HandleMovement()
    {
        bool isGrounded = _controller.isGrounded;
        if (isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        Vector3 moveDirection = transform.right * _moveInput.x + transform.forward * _moveInput.y;

        float speed = _isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        _controller.Move(moveDirection * speed * Time.deltaTime);

        _verticalVelocity += gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    #endregion

    #region Mouse Look

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mouseX = _lookInput.x * mouseSensitivity;
        float mouseY = _lookInput.y * mouseSensitivity;

        _cameraYaw += mouseX;

        float yDirection = invertY ? 1f : -1f;
        _cameraPitch += mouseY * yDirection;
        _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);

        if (_hasSeparateCamera)
        {
            // Classic FPS: yaw rotates the body, pitch rotates the camera child.
            transform.rotation = Quaternion.Euler(0f, _cameraYaw, 0f);
            cameraTransform.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
        }
        else
        {
            // Camera IS the player — apply both to the single transform.
            transform.rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);
        }
    }

    #endregion

    #region Highlight / Glow

    private void HandleHighlight()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            ClearHighlight();
            return;
        }

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (TryTraceInteractable(ray, out RaycastHit hit, out IInteractable interactable))
        {
            // Highlight the interactable's GameObject (the collider may be on a child).
            GameObject hitObj = (interactable as Component) != null
                ? ((Component)interactable).gameObject
                : hit.collider.gameObject;

            // Same object as before — no change needed
            if (_currentHighlighted == hitObj)
                return;

            // Clear previous, apply to new
            ClearHighlight();
            _currentHighlighted = hitObj;
            ApplyGlow(_currentHighlighted, true);
        }
        else
        {
            ClearHighlight();
        }
    }

    private void ClearHighlight()
    {
        if (_currentHighlighted != null)
        {
            ApplyGlow(_currentHighlighted, false);
            _currentHighlighted = null;
        }
    }

    private void ApplyGlow(GameObject obj, bool enable)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            foreach (Material mat in rend.materials)
            {
                if (enable)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(EmissionColor, glowColor * 0.5f);
                }
                else
                {
                    mat.SetColor(EmissionColor, Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
            }
        }
    }

    #endregion

    #region Interaction

    private void TryInteract()
    {
        // If we're holding a Pickable, drop it instead of picking up something new.
        if (_heldPickable != null && _heldPickable.IsHeld)
        {
            _heldPickable.RequestDrop();
            _heldPickable = null;
            return;
        }

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (TryTraceInteractable(ray, out RaycastHit hit, out IInteractable interactable))
        {
            // If we hit a held Pickable, ignore (it will handle its own drop)
            Pickable pickable = hit.collider.GetComponent<Pickable>();
            if (pickable != null && pickable.IsHeld)
                return;

            interactable.Interact();
            Debug.Log($"Interacted with: {hit.collider.gameObject.name}");

            // Track if we just picked up a Pickable so we can forward drop/scroll.
            if (pickable != null && pickable.IsHeld)
                _heldPickable = pickable;
        }
    }

    /// <summary>
    /// Traces along <paramref name="ray"/> up to <see cref="interactRange"/> and returns the
    /// nearest <see cref="IInteractable"/>, ignoring non-blocking, non-interactable geometry
    /// (decoration meshes, triggers) that sit in front of it. If a collider on
    /// <see cref="blockingLayers"/> is encountered before any interactable, the trace stops and
    /// returns false — you cannot interact through a solid wall.
    /// </summary>
    /// <param name="ray">The trace ray, typically from the camera.</param>
    /// <param name="hit">The hit that resolved to an interactable (only valid when true).</param>
    /// <param name="interactable">The interactable found (only valid when true).</param>
    /// <returns>True if an unobstructed interactable was found.</returns>
    private bool TryTraceInteractable(Ray ray, out RaycastHit hit, out IInteractable interactable)
    {
        hit = default;
        interactable = null;

        QueryTriggerInteraction triggerMode =
            ignoreTriggers ? QueryTriggerInteraction.Ignore : QueryTriggerInteraction.Collide;

        RaycastHit[] hits = Physics.RaycastAll(ray, interactRange, interactableLayer, triggerMode);
        if (hits.Length == 0)
            return false;

        // Nearest first so we respect what's physically in front.
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit h in hits)
        {
            // A solid blocker in the way stops the trace — no reaching through walls.
            if (IsOnLayerMask(h.collider.gameObject.layer, blockingLayers))
                return false;

            IInteractable candidate = h.collider.GetComponentInParent<IInteractable>();
            if (candidate != null)
            {
                hit = h;
                interactable = candidate;
                return true;
            }

            // Otherwise it's a non-blocking, non-interactable mesh/trigger — skip and continue.
        }

        return false;
    }

    /// <summary>Returns true if the given layer index is included in the mask.</summary>
    private static bool IsOnLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    #endregion

    #region Cursor

    private void HandleCursorToggle()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    #endregion

    #region Input System Callbacks

    /// <summary>Called by PlayerInput for the "Move" action.</summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        _moveInput = context.ReadValue<Vector2>();
    }

    /// <summary>Called by PlayerInput for the "Look" action.</summary>
    public void OnLook(InputAction.CallbackContext context)
    {
        _lookInput = context.ReadValue<Vector2>();
    }

    /// <summary>Called by PlayerInput for the "Interact" action.</summary>
    public void OnInteract(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryInteract();
        }
    }

    /// <summary>Called by PlayerInput for the "Attack" action (left click).</summary>
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryInteract();
        }
    }

    /// <summary>Called by PlayerInput for "Sprint" action.</summary>
    public void OnSprint(InputAction.CallbackContext context)
    {
        _isSprinting = context.performed;
    }

    /// <summary>Called by PlayerInput for "Jump" action.</summary>
    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed && _controller.isGrounded)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    /// <summary>
    /// Called by PlayerInput for the "Next" / scroll forward action.
    /// Pushes a held Pickable further away.
    /// </summary>
    public void OnNext(InputAction.CallbackContext context)
    {
        if (context.performed && _heldPickable != null && _heldPickable.IsHeld)
            _heldPickable.AdjustHoldDistance(1f);
    }

    /// <summary>
    /// Called by PlayerInput for the "Previous" / scroll back action.
    /// Pulls a held Pickable closer.
    /// </summary>
    public void OnPrevious(InputAction.CallbackContext context)
    {
        if (context.performed && _heldPickable != null && _heldPickable.IsHeld)
            _heldPickable.AdjustHoldDistance(-1f);
    }

    #endregion
}
