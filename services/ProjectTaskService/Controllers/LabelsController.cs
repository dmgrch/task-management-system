using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Controllers;

[ApiController]
[Route("")]
public class LabelsController : ControllerBase
{
    private readonly ProjectTaskDbContext _dbContext;
    private readonly IUserAccessClient _userAccessClient;

    public LabelsController(ProjectTaskDbContext dbContext, IUserAccessClient userAccessClient)
    {
        _dbContext = dbContext;
        _userAccessClient = userAccessClient;
    }

    [HttpPost("projects/{projectId:guid}/labels")]
    public async Task<IActionResult> CreateLabel(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CreateLabelRequest request,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
            return NotFound(new { code = "PROJECT_NOT_FOUND", message = "Project not found" });

        if (project.IsArchived)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot create label in archived project" });

        var accessError = await ValidateAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ManageProject,
            cancellationToken);

        if (accessError != null)
            return accessError;

        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Label name is required" });

        if (name.Length > 100)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Label name must be <= 100 characters" });

        var exists = await _dbContext.Labels
            .AnyAsync(l => l.ProjectId == projectId && l.Name.ToLower() == name.ToLower(), cancellationToken);

        if (exists)
            return Conflict(new { code = "LABEL_ALREADY_EXISTS", message = "Label with this name already exists" });

        var label = new Label
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            IsActive = true
        };

        _dbContext.Labels.Add(label);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created($"/projects/{projectId}/labels", ToResponse(label));
    }

    [HttpGet("projects/{projectId:guid}/labels")]
    public async Task<IActionResult> GetLabelsByProject(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
            return NotFound(new { code = "PROJECT_NOT_FOUND", message = "Project not found" });

        var accessError = await ValidateAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ViewProject,
            cancellationToken);

        if (accessError != null)
            return accessError;

        var labels = await _dbContext.Labels
            .Where(l => l.ProjectId == projectId && l.IsActive)
            .OrderBy(l => l.Name)
            .Select(l => ToResponse(l))
            .ToListAsync(cancellationToken);

        return Ok(labels);
    }

    [HttpPost("tasks/{taskId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> AssignLabelToTask(
        Guid taskId,
        Guid labelId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);

        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);

        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot assign label in archived project" });

        var label = await _dbContext.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.IsActive, cancellationToken);

        if (label == null)
            return NotFound(new { code = "LABEL_NOT_FOUND", message = "Label not found" });

        if (label.ProjectId != task.ProjectId)
            return Conflict(new { code = "LABEL_PROJECT_MISMATCH", message = "Label belongs to another project" });

        var accessError = await ValidateAccessAsync(
            actorUserId,
            task.ProjectId,
            ProjectAction.EditTask,
            cancellationToken);

        if (accessError != null && task.CreatedByUserId != actorUserId)
            return accessError;

        var exists = await _dbContext.TaskLabels
            .AnyAsync(tl => tl.TaskId == taskId && tl.LabelId == labelId, cancellationToken);

        if (exists)
            return Conflict(new { code = "LABEL_ALREADY_ASSIGNED", message = "Label is already assigned to this task" });

        _dbContext.TaskLabels.Add(new TaskLabel
        {
            TaskId = taskId,
            LabelId = labelId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpDelete("tasks/{taskId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> RemoveLabelFromTask(
        Guid taskId,
        Guid labelId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);

        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);

        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot remove label in archived project" });

        var accessError = await ValidateAccessAsync(
            actorUserId,
            task.ProjectId,
            ProjectAction.EditTask,
            cancellationToken);

        if (accessError != null && task.CreatedByUserId != actorUserId)
            return accessError;

        var taskLabel = await _dbContext.TaskLabels
            .FirstOrDefaultAsync(tl => tl.TaskId == taskId && tl.LabelId == labelId, cancellationToken);

        if (taskLabel == null)
            return NotFound(new { code = "TASK_LABEL_NOT_FOUND", message = "Label is not assigned to this task" });

        _dbContext.TaskLabels.Remove(taskLabel);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<IActionResult?> ValidateAccessAsync(
        Guid? actorUserId,
        Guid projectId,
        ProjectAction action,
        CancellationToken cancellationToken)
    {
        if (actorUserId is null || actorUserId == Guid.Empty)
        {
            return Unauthorized(new
            {
                code = "USER_ID_REQUIRED",
                message = "X-User-Id header is required"
            });
        }

        var accessCheck = await _userAccessClient.CheckAccessAsync(
            actorUserId.Value,
            projectId,
            action,
            cancellationToken);

        if (accessCheck is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                code = "USER_ACCESS_SERVICE_ERROR",
                message = "Could not check access in UserAccessService"
            });
        }

        if (!accessCheck.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "ACCESS_DENIED",
                message = "User does not have permission to perform this action"
            });
        }

        return null;
    }

    private static LabelResponse ToResponse(Label label)
    {
        return new LabelResponse
        {
            Id = label.Id,
            ProjectId = label.ProjectId,
            Name = label.Name,
            IsActive = label.IsActive
        };
    }
}