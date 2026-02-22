namespace UserTables.Client.ChangeTracking;

public enum EntityState
{
    Detached = 0,
    Unchanged = 1,
    Added = 2,
    Modified = 3,
    Deleted = 4
}