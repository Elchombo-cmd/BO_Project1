using UnityEngine;

/// <summary>
/// Sample interactable object. Attach to any GameObject with a Collider.
/// Logs a message and changes color when interacted with.
/// Replace this logic with your own gameplay behavior.
/// </summary>
public class SampleInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private string interactionMessage = "Object interacted!";
    [SerializeField] private Color highlightColor = Color.yellow;

    private Renderer _renderer;
    private Color _originalColor;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _originalColor = _renderer.material.color;
        }
    }

    public void Interact()
    {
        Debug.Log($"[Interact] {gameObject.name}: {interactionMessage}");

        if (_renderer != null)
        {
            // Toggle color to give visual feedback
            _renderer.material.color = _renderer.material.color == _originalColor
                ? highlightColor
                : _originalColor;
        }
    }
}
