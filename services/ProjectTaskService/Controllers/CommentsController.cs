using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Controllers;

[ApiController]
[Route("comments")]
public class CommentsController : ControllerBase
{
    private readonly ProjectTaskDbContext _dbContext;
    private readonly IUserAccessClient _userAccessClient;

    public CommentsController(ProjectTaskDbContext dbContext, IUserAccessClient userAccessClient)
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

    [HttpPost]
    public async Task<IActionResult> CreateComment(CreateCommentRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Comment text is required" });
        if (text.Length > 5000)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "Comment text must be <= 5000 characters" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == request.TaskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project == null)
            return NotFound(new { code = "PROJECT_NOT_FOUND", message = "Project not found" });
        if (project.IsArchived)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot comment on task in archived project" });

        var isMember = await _userAccessClient.IsMemberAsync(request.AuthorUserId, task.ProjectId, cancellationToken);
        if (!isMember)
            return StatusCode(403, new { code = "USER_NOT_MEMBER", message = "User is not a member of this project" });

        var accessError = await ValidateAccessAsync(request.AuthorUserId, task.ProjectId, ProjectAction.AddComment, cancellationToken);
        if (accessError != null) return accessError;

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            TaskId = request.TaskId,
            AuthorUserId = request.AuthorUserId,
            Text = text,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        _dbContext.Comments.Add(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetCommentsByTask), new { taskId = comment.TaskId }, ToResponse(comment));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteComment(Guid id, [FromHeader(Name = "X-User-Id")] Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "X-User-Id is required" });

        var comment = await _dbContext.Comments.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
        if (comment == null)
            return NotFound(new { code = "COMMENT_NOT_FOUND", message = "Comment not found" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == comment.TaskId, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });
        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project?.IsArchived == true)
            return Conflict(new { code = "PROJECT_ARCHIVED", message = "Cannot delete comment in archived project" });

        if (comment.AuthorUserId != actorUserId)
        {
            var accessError = await ValidateAccessAsync(actorUserId, task.ProjectId, ProjectAction.DeleteComment, cancellationToken);
            if (accessError != null) return accessError;
        }

        comment.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("/tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> GetCommentsByTask(Guid taskId, [FromHeader(Name = "X-User-Id")] Guid actorUserId, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
            return BadRequest(new { code = "VALIDATION_ERROR", message = "X-User-Id is required" });

        var task = await _dbContext.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            return NotFound(new { code = "TASK_NOT_FOUND", message = "Task not found" });

        var isMember = await _userAccessClient.IsMemberAsync(actorUserId, task.ProjectId, cancellationToken);
        if (!isMember)
            return StatusCode(403, new { code = "USER_NOT_MEMBER", message = "User is not a member of this project" });

        var comments = await _dbContext.Comments
            .Where(c => c.TaskId == taskId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .Select(c => ToResponse(c))
            .ToListAsync(cancellationToken);

        return Ok(comments);
    }

    private static CommentResponse ToResponse(Comment comment)
    {
        return new CommentResponse
        {
            Id = comment.Id,
            TaskId = comment.TaskId,
            AuthorUserId = comment.AuthorUserId,
            Text = comment.Text,
            CreatedAt = comment.CreatedAt,
            IsDeleted = comment.IsDeleted
        };
    }
}