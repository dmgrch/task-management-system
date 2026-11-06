namespace ProjectTaskService.Services;

public enum TaskErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    DependencyUnavailable
}

public sealed class TaskOperationException : Exception
{
    public TaskErrorKind Kind { get; }
    public string Code { get; }

    public TaskOperationException(TaskErrorKind kind, string code, string message)
        : base(message)
    {
        Kind = kind;
        Code = code;
    }
}
