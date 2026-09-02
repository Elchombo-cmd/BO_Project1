using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records interactions on an indexed stack. Each recorded interaction is assigned a
/// unique, monotonically increasing ID. You can:
///
///   * Record()          - push a new interaction (runs it immediately by default)
///   * Undo()            - reverse the most recent interaction
///   * UndoTo(id)        - reverse everything down to (but not including) the given ID
///   * Retrigger(id)     - re-run a stored interaction's Execute() without re-recording
///   * RecordAndRun / Replay helpers
///
/// Attach this to a single GameObject in your scene (it behaves as a lightweight singleton).
/// </summary>
public class InteractionStack : MonoBehaviour
{
    public static InteractionStack Instance { get; private set; }

    /// <summary>An interaction plus the ID it was filed under.</summary>
    public readonly struct Entry
    {
        public readonly int Id;
        public readonly IInteractionCommand Command;
        public readonly float TimeStamp;

        public Entry(int id, IInteractionCommand command, float timeStamp)
        {
            Id = id;
            Command = command;
            TimeStamp = timeStamp;
        }
    }

    // The live undo stack (most recent on top).
    private readonly List<Entry> _history = new List<Entry>();

    // Fast lookup by ID so Retrigger(id) is O(1). IDs are kept here even
    // after undo so you can still re-trigger a previously undone action.
    private readonly Dictionary<int, Entry> _byId = new Dictionary<int, Entry>();

    private int _nextId;

    /// <summary>Fired whenever the stack changes (record / undo). Useful for UI.</summary>
    public event Action OnStackChanged;

    /// <summary>Number of interactions currently on the undo stack.</summary>
    public int Count => _history.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[InteractionStack] Duplicate instance on {gameObject.name}; destroying it.");
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Record an interaction and return the ID it was stored under.
    /// By default the command is executed immediately (runImmediately = true),
    /// which matches "press a button -> it does its thing AND gets logged".
    /// </summary>
    public int Record(IInteractionCommand command, bool runImmediately = true)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        int id = _nextId++;
        var entry = new Entry(id, command, Time.time);

        _history.Add(entry);
        _byId[id] = entry;

        if (runImmediately)
        {
            command.Execute();
        }

        Debug.Log($"[InteractionStack] Recorded #{id}: {command.Description}");
        OnStackChanged?.Invoke();
        return id;
    }

    /// <summary>
    /// Undo the most recent interaction. Returns true if something was undone.
    /// The entry stays in the ID lookup so it can still be re-triggered later.
    /// </summary>
    public bool Undo()
    {
        if (_history.Count == 0)
        {
            Debug.Log("[InteractionStack] Nothing to undo.");
            return false;
        }

        int lastIndex = _history.Count - 1;
        Entry entry = _history[lastIndex];
        _history.RemoveAt(lastIndex);

        entry.Command.Undo();

        Debug.Log($"[InteractionStack] Undid #{entry.Id}: {entry.Command.Description}");
        OnStackChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Undo repeatedly until the entry with <paramref name="id"/> is on top of the stack
    /// (i.e. everything recorded AFTER that ID is reversed). Pass an ID that is not on the
    /// stack to undo everything.
    /// </summary>
    public void UndoTo(int id)
    {
        while (_history.Count > 0 && _history[_history.Count - 1].Id != id)
        {
            Undo();
        }
    }

    /// <summary>Undo everything currently on the stack.</summary>
    public void UndoAll()
    {
        while (Undo()) { }
    }

    /// <summary>
    /// Re-run a stored interaction by its ID WITHOUT recording a new entry.
    /// Works even if the interaction was previously undone. Returns true on success.
    /// </summary>
    public bool Retrigger(int id)
    {
        if (!_byId.TryGetValue(id, out Entry entry))
        {
            Debug.LogWarning($"[InteractionStack] No interaction with ID #{id}.");
            return false;
        }

        Debug.Log($"[InteractionStack] Re-triggering #{id}: {entry.Command.Description}");
        entry.Command.Execute();
        return true;
    }

    /// <summary>
    /// Re-run a stored interaction by ID AND push it back onto the stack as a new entry,
    /// so it becomes undoable again. Returns the new ID, or -1 if the source ID was unknown.
    /// </summary>
    public int RetriggerAndRecord(int id)
    {
        if (!_byId.TryGetValue(id, out Entry entry))
        {
            Debug.LogWarning($"[InteractionStack] No interaction with ID #{id}.");
            return -1;
        }

        return Record(entry.Command);
    }

    /// <summary>Look up an entry by ID. Returns true if found.</summary>
    public bool TryGet(int id, out Entry entry) => _byId.TryGetValue(id, out entry);

    /// <summary>Snapshot of the current undo stack, oldest first.</summary>
    public IReadOnlyList<Entry> GetHistory() => _history;

    /// <summary>Clear the undo stack. Optionally forget IDs too (disables future re-triggering).</summary>
    public void Clear(bool forgetIds = false)
    {
        _history.Clear();
        if (forgetIds)
        {
            _byId.Clear();
        }
        OnStackChanged?.Invoke();
    }
}
