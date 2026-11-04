namespace Tasklog.Api.Domain;

// Enums are stored as integers in the database (so ORDER BY Priority works)
// and serialized as strings in JSON (see Program.cs).

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum Recurrence
{
    None = 0,
    Daily = 1,
    Weekdays = 2,
    Weekly = 3,
    Monthly = 4
}

public enum ActivityAction
{
    Created = 0,
    Updated = 1,
    Completed = 2,
    Reopened = 3,
    Deleted = 4
}

public enum ActivityEntityType
{
    Task = 0,
    SubTask = 1
}
