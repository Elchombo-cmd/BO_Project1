using System;
using UnityEngine;

/// <summary>
/// Example button that records every press onto the <see cref="InteractionStack"/>.
///
/// When interacted with (via your existing IInteractable flow), it creates a command
/// describing "toggle this button on/off", runs it, and pushes it to the stack.
/// You can then Undo the press, or re-trigger it later by its recorded ID.
///
/// Attach to a GameObject with a Collider (same as SampleInteractable).
/// </summary>
public class ButtonInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string buttonName = "Button";
    [SerializeField] private Color pressedColor = Color.green;

    private Renderer _renderer;
    private Color _idleColor;
    private bool _isPressed;

    /// <summary>The ID of the most recent press recorded on the stack (-1 if none).</summary>
    public int LastPressId { get; private set; } = -1;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _idleColor = _renderer.material.color;
        }
    }

    /// <summary>Called by your interaction system (left-click, etc.).</summary>
    public void Interact()
    {
        if (InteractionStack.Instance == null)
        {
            Debug.LogWarning("[ButtonInteractable] No InteractionStack in scene; pressing without recording.");
            ApplyToggle();
            return;
        }

        // Build a command that captures how to do and undo THIS press,
        // then hand it to the stack. Record() runs Execute() for us.
        var command = new ToggleButtonCommand(this);
        LastPressId = InteractionStack.Instance.Record(command);
    }

    // --- Actual effect of the button. Kept separate so the command can call it. ---

    private void ApplyToggle()
    {
        _isPressed = !_isPressed;
        UpdateVisual();
        Debug.Log($"[ButtonInteractable] {buttonName} is now {(_isPressed ? "PRESSED" : "released")}.");
    }

    private void UpdateVisual()
    {
        if (_renderer != null)
        {
            _renderer.material.color = _isPressed ? pressedColor : _idleColor;
        }
    }

    /// <summary>
    /// The recorded, undoable unit of work for one press.
    /// Execute() and Undo() both just toggle, because a toggle is its own inverse.
    /// For non-symmetric actions, store the "before" state here and restore it in Undo().
    /// </summary>
    private sealed class ToggleButtonCommand : IInteractionCommand
    {
        private readonly ButtonInteractable _button;

        public ToggleButtonCommand(ButtonInteractable button)
        {
            _button = button;
        }

        public string Description => $"Press '{_button.buttonName}'";

        public void Execute() => _button.ApplyToggle();

        public void Undo() => _button.ApplyToggle();
    }
}
