using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person camera controller with WASD/arrow movement, mouse look, and left-click interaction.
/// Attach this script to the main Camera (or a player object with a child Camera).
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
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxLookAngle = 85f;
    [SerializeField] private bool invertY = false;

    [Header("Interaction")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer = ~0; // Everything by default

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController _controller;
    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private float _verticalVelocity;
    private float _cameraPitch;
    private bool _isSprinting;

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
    }

    private void Start()
    {
        // Lock and hide cursor for FPS controls
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        HandleMovement();
        HandleMouseLook();
        HandleInteraction();
        HandleCursorToggle();
    }

    #region Movement

    private void HandleMovement()
    {
        // Ground check
        bool isGrounded = _controller.isGrounded;
        if (isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f; // Small downward force to keep grounded
        }

        // Calculate move direction relative to where we're facing
        Vector3 moveDirection = transform.right * _moveInput.x + transform.forward * _moveInput.y;

        float speed = _isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        _controller.Move(moveDirection * speed * Time.deltaTime);

        // Gravity
        _verticalVelocity += gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    #endregion

    #region Mouse Look

    private void HandleMouseLook()
    {
        // Only rotate when cursor is locked
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mouseX = _lookInput.x * mouseSensitivity;
        float mouseY = _lookInput.y * mouseSensitivity;

        // Horizontal rotation (rotate the whole player)
        transform.Rotate(Vector3.up * mouseX);

        // Vertical rotation (pitch the camera only)
        float yDirection = invertY ? 1f : -1f;
        _cameraPitch += mouseY * yDirection;
        _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);

        cameraTransform.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
    }

    #endregion

    #region Interaction

    private void HandleInteraction()
    {
        // Left-click to interact (handled via Input System callback)
    }

    private void TryInteract()
    {
        // Raycast from center of screen
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayer))
        {
            // Try to find an IInteractable on the hit object
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                interactable.Interact();
                Debug.Log($"Interacted with: {hit.collider.gameObject.name}");
            }
            else
            {
                Debug.Log($"Hit object '{hit.collider.gameObject.name}' has no IInteractable component.");
            }
        }
    }

    #endregion

    #region Cursor

    private void HandleCursorToggle()
    {
        // Press Escape to toggle cursor lock (useful for UI / debugging)
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

    /// <summary>
    /// Called by PlayerInput component or InputSystem_Actions for the "Move" action.
    /// </summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        _moveInput = context.ReadValue<Vector2>();
    }

    /// <summary>
    /// Called by PlayerInput component or InputSystem_Actions for the "Look" action.
    /// </summary>
    public void OnLook(InputAction.CallbackContext context)
    {
        _lookInput = context.ReadValue<Vector2>();
    }

    /// <summary>
    /// Called by PlayerInput component or InputSystem_Actions for the "Interact" action (left click).
    /// </summary>
    public void OnInteract(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryInteract();
        }
    }

    /// <summary>
    /// Called by PlayerInput component or InputSystem_Actions for the "Attack" action.
    /// Can be remapped to left-click interaction if preferred.
    /// </summary>
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            TryInteract();
        }
    }

    /// <summary>
    /// Called by PlayerInput component for "Sprint" action.
    /// </summary>
    public void OnSprint(InputAction.CallbackContext context)
    {
        _isSprinting = context.performed;
    }

    /// <summary>
    /// Called by PlayerInput component for "Jump" action.
    /// </summary>
    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed && _controller.isGrounded)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    #endregion
}
