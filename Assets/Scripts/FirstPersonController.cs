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
    [SerializeField] private LayerMask interactableLayer = ~0;

    [Header("Highlight / Glow")]
    [Tooltip("Color of the glow outline when looking at an interactable object.")]
    [SerializeField] private Color glowColor = new Color(1f, 0.8f, 0.2f, 1f);

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
    }

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

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            GameObject hitObj = hit.collider.gameObject;

            // Only highlight objects that have an IInteractable component
            if (hitObj.GetComponent<IInteractable>() == null)
            {
                ClearHighlight();
                return;
            }

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

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            // If we hit a held Pickable, ignore (it will handle its own drop)
            Pickable pickable = hit.collider.GetComponent<Pickable>();
            if (pickable != null && pickable.IsHeld)
                return;

            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                interactable.Interact();
                Debug.Log($"Interacted with: {hit.collider.gameObject.name}");

                // Track if we just picked up a Pickable so we can forward drop/scroll.
                if (pickable != null && pickable.IsHeld)
                    _heldPickable = pickable;
            }
        }
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
