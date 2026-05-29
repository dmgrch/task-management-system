using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Data;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Controllers;

[ApiController]
[Route("task-history")]
public class TaskHistoryController : ControllerBase
{
    private readonly ProjectTaskDbContext _dbContext;
    private readonly IUserAccessClient _userAccessClient;

    public TaskHistoryController(ProjectTaskDbContext dbContext, IUserAccessClient userAccessClient)
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
            return Unauthorized(new { code = "USER_ID_REQUIRED", message = "X-User-Id header is required" });
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

    [HttpGet("tasks/{taskId:guid}")]
    public async Task<IActionResult> GetHistoryByTask(
        Guid taskId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var accessError = await ValidateAccessAsync(actorUserId, task.ProjectId, ProjectAction.ViewTaskHistory, cancellationToken);
        if (accessError != null) return accessError;

        var history = await _dbContext.TaskHistories
            .Where(h => h.TaskId == taskId)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new
            {
                h.Id,
                h.ChangedByUserId,
                h.ChangeType,
                h.OldValue,
                h.NewValue,
                h.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(history);
    }
}