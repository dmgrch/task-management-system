using ProjectTaskService.Enums;

namespace ProjectTaskService.Entities;

public class TaskHistory
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid ChangedByUserId { get; set; }
    public TaskChangeType ChangeType { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; }
}