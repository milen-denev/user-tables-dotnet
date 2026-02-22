using System;
using System.Collections.Generic;
using UserTables.Client.Internal;

namespace UserTables.Client.ChangeTracking;

public sealed class TrackedEntityEntry
{
    internal TrackedEntityEntry(
        object entity,
        EntityMetadata metadata,
        EntityState state,
        Dictionary<string, object?>? originalData)
    {
        Entity = entity;
        Metadata = metadata;
        State = state;
        OriginalData = originalData;
    }

    public object Entity { get; }
    public Type EntityType => Metadata.EntityType;
    public EntityState State { get; set; }

    internal EntityMetadata Metadata { get; }
    internal Dictionary<string, object?>? OriginalData { get; set; }
}