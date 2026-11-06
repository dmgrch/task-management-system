using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly ProjectTaskDbContext _db;

    public TaskRepository(ProjectTaskDbContext db) => _db = db;

    public Task<Project?> GetProjectAsync(Guid projectId, CancellationToken ct) =>
        _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);

    public Task<TaskItem?> GetTaskAsync(Guid taskId, CancellationToken ct) =>
        _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, ct);

    public async Task<(List<TaskItem> Items, int TotalCount)> GetTasksPageAsync(
        Guid projectId,
        int page,
        int limit,
        TaskItemStatus? status,
        CancellationToken ct)
    {
        var query = _db.Tasks
            .AsNoTracking()
            .Where(task => task.ProjectId == projectId && !task.IsDeleted);

        if (status.HasValue)
            query = query.Where(task => task.Status == status.Value);

        var totalCount = await query.CountAsync(ct);
        var offset = ((long)page - 1) * limit;
        if (offset > int.MaxValue)
            return (new List<TaskItem>(), totalCount);

        var items = await query
            .OrderBy(task => task.CreatedAt)
            .ThenBy(task => task.Id)
            .Skip((int)offset)
            .Take(limit)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<List<LabelResponse>> GetActiveLabelsAsync(Guid taskId, CancellationToken ct) =>
        _db.TaskLabels
            .Where(link => link.TaskId == taskId)
            .Join(_db.Labels, link => link.LabelId, label => label.Id,
                (_, label) => label)
            .Where(label => label.IsActive)
            .OrderBy(label => label.Name)
            .Select(label => new LabelResponse
            {
                Id = label.Id,
                ProjectId = label.ProjectId,
                Name = label.Name,
                IsActive = label.IsActive
            })
            .ToListAsync(ct);

    public void AddTask(TaskItem task) => _db.Tasks.Add(task);
    public void AddHistory(TaskHistory history) => _db.TaskHistories.Add(history);
    public async Task SaveAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
