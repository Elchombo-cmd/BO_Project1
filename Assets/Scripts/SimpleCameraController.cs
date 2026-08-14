using UnityEngine;

/// <summary>
/// Simple first-person camera controller.
/// Attach directly to a Camera GameObject.
/// Provides WASD/arrow movement, mouse look, and left-click interaction.
/// Uses legacy Input (no Input System package required).
/// </summary>
public class SimpleCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintMultiplier = 2f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxPitchAngle = 85f;
    [SerializeField] private bool invertY = false;

    [Header("Interaction")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactableLayer = ~0;

    [Header("Highlight / Glow")]
    [Tooltip("Color of the glow outline when looking at an interactable object.")]
    [SerializeField] private Color glowColor = new Color(1f, 0.8f, 0.2f, 1f);
    [Tooltip("Width of the glow outline effect.")]
    [SerializeField] private float glowWidth = 0.03f;

    private float _pitch;
    private float _yaw;
    private CharacterController _controller;
    private GameObject _currentHighlighted;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");

    private void Awake()
    {
        // Add a CharacterController at runtime if one doesn't exist
        _controller = GetComponent<CharacterController>();
        if (_controller == null)
        {
            _controller = gameObject.AddComponent<CharacterController>();
            _controller.height = 0.50f;
            _controller.radius = 0.2f;
            _controller.center = Vector3.zero;
        }
    }

    private void Start()
    {
        // Lock cursor for FPS-style controls
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialize rotation from current transform
        Vector3 euler = transform.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x;
    }

    private void Update()
    {
        HandleMouseLook();
        HandleMovement();
        HandleHighlight();
        HandleInteraction();
        HandleCursorToggle();
    }

    #region Mouse Look

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        _yaw += mouseX;

        float yDirection = invertY ? 1f : -1f;
        _pitch += mouseY * yDirection;
        _pitch = Mathf.Clamp(_pitch, -maxPitchAngle, maxPitchAngle);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    #endregion

    #region Movement

    private void HandleMovement()
    {
        // WASD and arrow keys (both are mapped to Horizontal/Vertical axes by default)
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 moveDirection = transform.right * horizontal + transform.forward * vertical;

        // Sprint with Left Shift
        float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * sprintMultiplier : moveSpeed;

        _controller.Move(moveDirection * speed * Time.deltaTime);

        // Simple gravity
        if (!_controller.isGrounded)
        {
            _controller.Move(Vector3.down * 9.81f * Time.deltaTime);
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

        Ray ray = new Ray(transform.position, transform.forward);

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

            // Clear previous highlight
            ClearHighlight();

            // Apply glow to new object
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
                    // Enable emission for a glow effect
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(EmissionColor, glowColor * 0.5f);
                }
                else
                {
                    // Disable emission
                    mat.SetColor(EmissionColor, Color.black);
                    mat.DisableKeyword("_EMISSION");
                }
            }
        }
    }

    #endregion

    #region Interaction

    private void HandleInteraction()
    {
        // Left-click to interact (only on initial press)
        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked)
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        Ray ray = new Ray(transform.position, transform.forward);

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
                Debug.Log($"[SimpleCameraController] Interacted with: {hit.collider.gameObject.name}");
            }
            else
            {
                Debug.Log($"[SimpleCameraController] Hit '{hit.collider.gameObject.name}' — no IInteractable component.");
            }
        }
    }

    #endregion

    #region Cursor Toggle

    private void HandleCursorToggle()
    {
        // Press Escape to unlock/lock cursor
        if (Input.GetKeyDown(KeyCode.Escape))
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
}
