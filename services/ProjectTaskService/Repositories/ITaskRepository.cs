using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Repositories;

public interface ITaskRepository
{
    Task<Project?> GetProjectAsync(Guid projectId, CancellationToken ct);
    Task<TaskItem?> GetTaskAsync(Guid taskId, CancellationToken ct);
    Task<Dictionary<TaskItemStatus, int>> GetTaskCountsByStatusAsync(
        Guid projectId, CancellationToken ct);
    Task<List<LabelResponse>> GetActiveLabelsAsync(Guid taskId, CancellationToken ct);
    void AddTask(TaskItem task);
    void AddHistory(TaskHistory history);
    Task SaveAsync(CancellationToken ct);
}
