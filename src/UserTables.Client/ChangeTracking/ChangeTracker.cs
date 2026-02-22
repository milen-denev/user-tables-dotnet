using System;
using System.Collections.Generic;
using System.Linq;
using UserTables.Client.Internal;

namespace UserTables.Client.ChangeTracking;

public sealed class ChangeTracker
{
    private readonly List<TrackedEntityEntry> _entries = [];
    private readonly Dictionary<object, TrackedEntityEntry> _entriesByEntity =
        new(ReferenceEqualityComparer.Instance);

    public bool AutoDetectChangesEnabled { get; set; } = true;

    public IReadOnlyList<TrackedEntityEntry> Entries => _entries;

    internal TrackedEntityEntry Track(object entity, EntityMetadata metadata, EntityState state, Dictionary<string, object?>? originalData = null)
    {
        if (_entriesByEntity.TryGetValue(entity, out var existing))
        {
            existing.State = state;
            if (originalData is not null)
            {
                existing.OriginalData = new Dictionary<string, object?>(originalData, StringComparer.Ordinal);
            }

            return existing;
        }

        var entry = new TrackedEntityEntry(entity, metadata, state, originalData is null ? null : new Dictionary<string, object?>(originalData, StringComparer.Ordinal));
        _entries.Add(entry);
        _entriesByEntity[entity] = entry;
        return entry;
    }

    internal void DetectChanges()
    {
        if (!AutoDetectChangesEnabled)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (entry.State != EntityState.Unchanged || entry.OriginalData is null)
            {
                continue;
            }

            var current = entry.Metadata.ToRowData(entry.Entity);
            if (!DictionaryComparer.Equals(entry.OriginalData, current))
            {
                entry.State = EntityState.Modified;
            }
        }
    }

    internal void AcceptAllChanges()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.State == EntityState.Deleted)
            {
                _entriesByEntity.Remove(entry.Entity);
                _entries.RemoveAt(i);
                continue;
            }

            entry.State = EntityState.Unchanged;
            entry.OriginalData = entry.Metadata.ToRowData(entry.Entity);
        }
    }
}