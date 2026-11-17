using System;
using System.Collections.Generic;

namespace Adamantium.ECS;

/// <summary>What changed in a scene while it is recorded: the entities changed - a value of theirs or of their components -
/// those that joined it, and those taken out of it. From any thread; read once the recording stopped.</summary>
public sealed class SceneChanges
{
    private readonly object _sync = new();
    private readonly HashSet<Entity> _changed = [];
    private readonly HashSet<Entity> _added = [];
    private readonly HashSet<Entity> _removed = [];
    private volatile bool _recording;
    // Which recording an entity was last noted changed in: noted once, the hot path takes no lock again.
    private int _recordingNumber;

    internal SceneChanges()
    {
    }

    /// <summary>Whether changes are being recorded.</summary>
    public bool IsRecording => _recording;

    /// <summary>The entities changed while recorded, those taken out among them.</summary>
    public IReadOnlyCollection<Entity> Changed => _changed;

    /// <summary>The entities that joined the scene while recorded - a root added, or an entity put under one of it.</summary>
    public IReadOnlyCollection<Entity> Added => _added;

    /// <summary>The entities taken out of the scene while recorded, roots and parts alike.</summary>
    public IReadOnlyCollection<Entity> Removed => _removed;

    /// <summary>Forgets what was recorded and records anew.</summary>
    public void Start()
    {
        lock (_sync)
        {
            Clear();
            _recordingNumber++;
            _recording = true;
        }
    }

    /// <summary>Stops recording; what was recorded stays until <see cref="Start"/> or <see cref="Clear"/>.</summary>
    public void Stop()
    {
        lock (_sync)
        {
            _recording = false;
        }
    }

    /// <summary>Forgets what was recorded.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _changed.Clear();
            _added.Clear();
            _removed.Clear();
        }
    }

    internal void NoteChanged(Entity entity)
    {
        // Noted already, or forgotten.
        if (!_recording || Math.Abs(entity.ChangedDuringRecording) == _recordingNumber)
            return;

        lock (_sync)
        {
            if (!_recording)
                return;

            _changed.Add(entity);
            entity.ChangedDuringRecording = _recordingNumber;
        }
    }

    internal void NoteAdded(Entity entity)
    {
        if (!_recording)
            return;

        lock (_sync)
        {
            if (!_recording)
                return;

            _added.Add(entity);
            // Joined again, a forgotten one is followed again.
            if (entity.ChangedDuringRecording == -_recordingNumber)
                entity.ChangedDuringRecording = 0;
        }
    }

    // One that joined while recorded and leaves again is forgotten - marked, so its leaving said twice is not kept: a
    // game's bullets do not pile up. One of the scene before, taken out earlier, stays among those taken out and changed.
    internal void NoteRemoved(Entity entity)
    {
        if (!_recording || entity.ChangedDuringRecording == -_recordingNumber)
            return;

        lock (_sync)
        {
            if (!_recording)
                return;

            if (!_added.Remove(entity))
            {
                _removed.Add(entity);
            }
            else if (!_removed.Contains(entity))
            {
                _changed.Remove(entity);
                entity.ChangedDuringRecording = -_recordingNumber;
            }
        }
    }
}
