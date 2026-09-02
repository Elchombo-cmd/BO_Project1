/// <summary>
/// A single, replayable/undoable interaction (e.g. one button press).
/// Implement this on anything you want to record onto the <see cref="InteractionStack"/>.
///
/// The command pattern lets the stack treat every interaction uniformly:
/// it can Execute (do / re-trigger) and Undo (reverse) without knowing the details.
/// </summary>
public interface IInteractionCommand
{
    /// <summary>
    /// Human-readable label for logging / debugging (e.g. "Press Red Button").
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Perform the interaction. Called once when first recorded, and again
    /// whenever the command is re-triggered by ID.
    /// </summary>
    void Execute();

    /// <summary>
    /// Reverse the effect of <see cref="Execute"/>. Called when the interaction
    /// is undone. If an action cannot be undone, leave this empty.
    /// </summary>
    void Undo();
}
