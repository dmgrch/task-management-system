using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Controllers;

[ApiController]
[Route("projects")]
public class ProjectsController : ControllerBase
{
    private readonly ProjectTaskDbContext _dbContext;
    private readonly IUserAccessClient _userAccessClient;

    public ProjectsController(
        ProjectTaskDbContext dbContext,
        IUserAccessClient userAccessClient)
    {
        _dbContext = dbContext;
        _userAccessClient = userAccessClient;
    }

    [HttpPost]
    public async Task<IActionResult> CreateProject(
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CreateProjectRequest request,
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

        var actor = await _userAccessClient.GetUserByIdAsync(
            actorUserId.Value,
            cancellationToken);

        if (actor is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "USER_NOT_FOUND",
                Message = "Actor user not found"
            });
        }

        if (!actor.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "USER_NOT_ACTIVE",
                Message = "Inactive user cannot create project"
            });
        }

        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Project name is required"
            });
        }

        if (name.Length > 250)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Project name must be less than or equal to 250 characters"
            });
        }

        var description = NormalizeNullableString(request.Description);
        if (description is not null && description.Length > 10000)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Description must be less than or equal to 10000 characters"
            });
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedByUserId = actorUserId.Value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsArchived = false
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var ownerCreated = await _userAccessClient.AddProjectOwnerAsync(
            project.Id,
            actorUserId.Value,
            cancellationToken);

        if (!ownerCreated)
        {
            _dbContext.Projects.Remove(project);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse
            {
                Code = "USER_ACCESS_SERVICE_ERROR",
                Message = "Project was not created because owner could not be registered in UserAccessService"
            });
        }

        return CreatedAtAction(
            nameof(GetProjectById),
            new { projectId = project.Id },
            ToResponse(project));
    }

    [HttpGet("{projectId:guid}")]
    public async Task<IActionResult> GetProjectById(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "ProjectId cannot be empty"
            });
        }

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_NOT_FOUND",
                Message = "Project not found"
            });
        }

        var accessError = await ValidateAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ViewProject,
            cancellationToken);

        if (accessError is not null)
        {
            return accessError;
        }

        return Ok(ToResponse(project));
    }

    [HttpPatch("{projectId:guid}")]
    public async Task<IActionResult> UpdateProject(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        [FromBody] JsonElement request,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "ProjectId cannot be empty"
            });
        }

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_NOT_FOUND",
                Message = "Project not found"
            });
        }

        var accessError = await ValidateAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ManageProject,
            cancellationToken);

        if (accessError is not null)
        {
            return accessError;
        }

        if (project.IsArchived)
        {
            return Conflict(new ErrorResponse
            {
                Code = "PROJECT_ARCHIVED",
                Message = "Archived project cannot be edited"
            });
        }

        var hasChanges = false;

        if (request.TryGetProperty("name", out var nameElement))
        {
            if (nameElement.ValueKind != JsonValueKind.String)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "VALIDATION_ERROR",
                    Message = "Project name must be a string"
                });
            }

            var name = nameElement.GetString()?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "VALIDATION_ERROR",
                    Message = "Project name cannot be empty"
                });
            }

            if (name.Length > 250)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "VALIDATION_ERROR",
                    Message = "Project name must be less than or equal to 250 characters"
                });
            }

            project.Name = name;
            hasChanges = true;
        }

        if (request.TryGetProperty("description", out var descriptionElement))
        {
            if (descriptionElement.ValueKind == JsonValueKind.Null)
            {
                project.Description = null;
                hasChanges = true;
            }
            else if (descriptionElement.ValueKind == JsonValueKind.String)
            {
                var description = NormalizeNullableString(descriptionElement.GetString());

                if (description is not null && description.Length > 10000)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Code = "VALIDATION_ERROR",
                        Message = "Description must be less than or equal to 10000 characters"
                    });
                }

                project.Description = description;
                hasChanges = true;
            }
            else
            {
                return BadRequest(new ErrorResponse
                {
                    Code = "VALIDATION_ERROR",
                    Message = "Description must be a string or null"
                });
            }
        }

        if (!hasChanges)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "At least one field must be provided"
            });
        }

        project.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(project));
    }

    [HttpPatch("{projectId:guid}/archive")]
    public async Task<IActionResult> ArchiveProject(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "ProjectId cannot be empty"
            });
        }

        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_NOT_FOUND",
                Message = "Project not found"
            });
        }

        var accessError = await ValidateAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ArchiveProject,
            cancellationToken);

        if (accessError is not null)
        {
            return accessError;
        }

        if (project.IsArchived)
        {
            return Conflict(new ErrorResponse
            {
                Code = "PROJECT_ALREADY_ARCHIVED",
                Message = "Project is already archived"
            });
        }

        project.IsArchived = true;
        project.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(project));
    }

    [HttpGet("{projectId:guid}/tasks")]
    public async Task<IActionResult> GetTasksByProject(
    Guid projectId,
    [FromHeader(Name = "X-User-Id")] Guid? actorUserId,
    CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project == null)
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_NOT_FOUND",
                Message = "Project not found"
            });

        var accessError = await ValidateAccessAsync(actorUserId, projectId, ProjectAction.ViewTask, cancellationToken);
        if (accessError != null)
            return accessError;

        var tasks = await _dbContext.Tasks
            .Where(t => t.ProjectId == projectId && !t.IsDeleted)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TaskResponse
            {
                Id = t.Id,
                ProjectId = t.ProjectId,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status,
                AssigneeUserId = t.AssigneeUserId,
                CreatedByUserId = t.CreatedByUserId,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(tasks);
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

    private static ProjectResponse ToResponse(Project project)
    {
        return new ProjectResponse
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            CreatedByUserId = project.CreatedByUserId,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            IsArchived = project.IsArchived
        };
    }

    private static string? NormalizeNullableString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }
}