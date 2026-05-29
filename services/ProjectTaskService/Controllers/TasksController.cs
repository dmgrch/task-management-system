using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;
using ProjectTaskService.Clients.UserAccess;

namespace ProjectTaskService.Controllers;

[ApiController]
[Route("tasks")]
public class TasksController : ControllerBase
{
    private readonly ProjectTaskDbContext _dbContext;
    private readonly IUserAccessClient _userAccessClient;

    public TasksController(ProjectTaskDbContext dbContext, IUserAccessClient userAccessClient)
    {
        _dbContext = dbContext;
        _userAccessClient = userAccessClient;
    }

    private async Task<IActionResult?> ValidateAccessAsync(
        Guid? actorUserId,
        Guid projectId,
        ProjectAction action,
        CancellationToken cancellationToken)
    {
        if (actorUserId is null || actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var accessCheck = await _userAccessClient.CheckAccessAsync(
            actorUserId.Value,
            projectId,
            action,
            cancellationToken);

        if (accessCheck is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse
            {
                Code = "USER_ACCESS_SERVICE_ERROR",
                Message = "Could not check access in UserAccessService"
            });
        }

        if (!accessCheck.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "ACCESS_DENIED",
                Message = "User does not have permission to perform this action"
            });
        }

        return null;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (request.CreatedByUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "CreatedByUserId is required" });

        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Title is required" });
        if (title.Length > 250)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Title must be <= 250 characters" });

        var description = request.Description?.Trim() ?? "";
        if (description.Length > 10000)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Description must be <= 10000 characters" });

        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId);
        if (project == null)
            return NotFound(new { code = "PROJECT_NOT_FOUND", message = "Project not found" });
        if (project.IsArchived)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot create task in archived project" });

        if (request.AssigneeUserId.HasValue && request.AssigneeUserId != Guid.Empty)
        {
            var isMember = await _userAccessClient.IsMemberAsync(request.AssigneeUserId.Value, request.ProjectId, cancellationToken);
            if (!isMember)
                return BadRequest(new { code = "ASSIGNEE_NOT_MEMBER", message = "Assignee is not a member of this project" });
        }

        var accessError = await ValidateAccessAsync(request.CreatedByUserId, request.ProjectId, ProjectAction.CreateTask, cancellationToken);
        if (accessError != null) return accessError;

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Title = title,
            Description = description,
            Status = TaskItemStatus.TODO,
            AssigneeUserId = request.AssigneeUserId,
            CreatedByUserId = request.CreatedByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _dbContext.Tasks.Add(task);

        _dbContext.TaskHistories.Add(new TaskHistory
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            ChangedByUserId = request.CreatedByUserId,
            ChangeType = TaskChangeType.Created,
            OldValue = null,
            NewValue = null,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTaskById), new { id = task.Id }, ToResponse(task));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTaskById(
        Guid id,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var accessError = await ValidateAccessAsync(
            actorUserId,
            task.ProjectId,
            ProjectAction.ViewTask,
            cancellationToken);

        if (accessError != null)
            return accessError;

        var labels = await GetTaskLabelsAsync(task.Id, cancellationToken);

        return Ok(ToResponse(task, labels));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTask(Guid id, UpdateTaskRequest request, [FromHeader(Name = "X-User-Id")] Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "X-User-Id is required" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var accessError = await ValidateAccessAsync(actorUserId, task.ProjectId, ProjectAction.EditTask, cancellationToken);
        if (accessError != null && task.CreatedByUserId != actorUserId)
            return accessError;
        
        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot edit task in archived project" });

        var oldTitle = task.Title;
        var oldDescription = task.Description;

        var newTitle = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(newTitle))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Title is required" });
        if (newTitle.Length > 250)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Title must be <= 250 characters" });

        var newDescription = request.Description?.Trim() ?? "";
        if (newDescription.Length > 10000)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Description must be <= 10000 characters" });

        task.Title = newTitle;
        task.Description = newDescription;
        task.UpdatedAt = DateTime.UtcNow;

        if (oldTitle != newTitle)
        {
            _dbContext.TaskHistories.Add(new TaskHistory
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                ChangedByUserId = actorUserId,
                ChangeType = TaskChangeType.TitleChanged,
                OldValue = oldTitle,
                NewValue = newTitle,
                CreatedAt = DateTime.UtcNow
            });
        }
        if (oldDescription != newDescription)
        {
            _dbContext.TaskHistories.Add(new TaskHistory
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                ChangedByUserId = actorUserId,
                ChangeType = TaskChangeType.DescriptionChanged,
                OldValue = oldDescription,
                NewValue = newDescription,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.ActorUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "ActorUserId is required" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);;
        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot change status in archived project" });

        if (!await _userAccessClient.IsMemberAsync(request.ActorUserId, task.ProjectId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "USER_NOT_MEMBER",
                Message = "User is not a member of this project"
            });
        }

        var accessError = await ValidateAccessAsync(request.ActorUserId, task.ProjectId, ProjectAction.ChangeTaskStatus, cancellationToken);
        if (accessError != null)
            return accessError;

        if (!IsValidStatusTransition(task.Status, request.NewStatus))
            return BadRequest(new { code = "INVALID_STATUS_TRANSITION", message = $"Cannot change from {task.Status} to {request.NewStatus}" });

        if (request.NewStatus == TaskItemStatus.DONE && task.AssigneeUserId == null)
            return BadRequest(new { code = "NO_ASSIGNEE", message = "Cannot set DONE without assignee" });

        var oldStatus = task.Status;
        task.Status = request.NewStatus;
        task.UpdatedAt = DateTime.UtcNow;

        _dbContext.TaskHistories.Add(new TaskHistory
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            ChangedByUserId = request.ActorUserId,
            ChangeType = TaskChangeType.StatusChanged,
            OldValue = oldStatus.ToString(),
            NewValue = request.NewStatus.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/assignee")]
    public async Task<IActionResult> AssignAssignee(Guid id, AssignAssigneeRequest request, CancellationToken cancellationToken)
    {
        if (request.ActorUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "ActorUserId is required" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot assign assignee in archived project" });
        
        if (request.AssigneeUserId.HasValue && request.AssigneeUserId != Guid.Empty)
        {
            var isMember = await _userAccessClient.IsMemberAsync(request.AssigneeUserId.Value, task.ProjectId, cancellationToken);
            if (!isMember)
                return BadRequest(new { code = "ASSIGNEE_NOT_MEMBER", message = "Assignee is not a member of this project" });
        }

        var accessError = await ValidateAccessAsync(request.ActorUserId, task.ProjectId, ProjectAction.AssignTask, cancellationToken);
        if (accessError != null) 
            return accessError;

        var oldAssignee = task.AssigneeUserId;
        task.AssigneeUserId = request.AssigneeUserId;
        task.UpdatedAt = DateTime.UtcNow;

        _dbContext.TaskHistories.Add(new TaskHistory
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            ChangedByUserId = request.ActorUserId,
            ChangeType = TaskChangeType.AssigneeChanged,
            OldValue = oldAssignee?.ToString(),
            NewValue = request.AssigneeUserId?.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTask(Guid id, [FromHeader(Name = "X-User-Id")] Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "X-User-Id is required" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot delete task in archived project" });

        var accessError = await ValidateAccessAsync(actorUserId, task.ProjectId, ProjectAction.DeleteTask, cancellationToken);
        if (accessError != null && task.CreatedByUserId != actorUserId)
            return accessError;

        task.IsDeleted = true;
        task.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<List<LabelResponse>> GetTaskLabelsAsync(
    Guid taskId,
    CancellationToken cancellationToken)
    {
    return await _dbContext.TaskLabels
        .Where(taskLabel => taskLabel.TaskId == taskId)
        .Join(
            _dbContext.Labels,
            taskLabel => taskLabel.LabelId,
            label => label.Id,
            (taskLabel, label) => label)
        .Where(label => label.IsActive)
        .OrderBy(label => label.Name)
        .Select(label => new LabelResponse
        {
            Id = label.Id,
            ProjectId = label.ProjectId,
            Name = label.Name,
            IsActive = label.IsActive
        })
        .ToListAsync(cancellationToken);
    }

    private static bool IsValidStatusTransition(TaskItemStatus from, TaskItemStatus to)
    {
        return (from, to) switch
        {
            (TaskItemStatus.TODO, TaskItemStatus.IN_PROGRESS) => true,
            (TaskItemStatus.IN_PROGRESS, TaskItemStatus.REVIEW) => true,
            (TaskItemStatus.REVIEW, TaskItemStatus.DONE) => true,
            _ => false
        };
    }

    private static TaskResponse ToResponse(
    TaskItem task,
    List<LabelResponse>? labels = null)
    {
        return new TaskResponse
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            AssigneeUserId = task.AssigneeUserId,
            CreatedByUserId = task.CreatedByUserId,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            Labels = labels ?? new List<LabelResponse>()
        };
    }
}