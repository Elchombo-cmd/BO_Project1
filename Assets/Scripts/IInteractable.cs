/// <summary>
/// Implement this interface on any GameObject that should respond to player interaction (left-click).
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Called when the player interacts with this object.
    /// </summary>
    void Interact();
}
