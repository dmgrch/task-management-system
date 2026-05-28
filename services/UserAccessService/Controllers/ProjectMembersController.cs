using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAccessService.Clients.ProjectTask;
using UserAccessService.Data;
using UserAccessService.Dtos;
using UserAccessService.Entities;
using UserAccessService.Enums;
using UserAccessService.Services;

namespace UserAccessService.Controllers;

[ApiController]
[Route("projects/{projectId:guid}/members")]
public class ProjectMembersController : ControllerBase
{
    private readonly UserAccessDbContext _dbContext;
    private readonly IAccessPolicy _accessPolicy;
    private readonly IProjectTaskClient _projectTaskClient;

    public ProjectMembersController(
        UserAccessDbContext dbContext,
        IAccessPolicy accessPolicy,
        IProjectTaskClient projectTaskClient)
    {
        _dbContext = dbContext;
        _accessPolicy = accessPolicy;
        _projectTaskClient = projectTaskClient;
    }

    [HttpPost]
    public async Task<ActionResult<ProjectMemberResponse>> AddMember(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId,
        AddProjectMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var projectError = await ValidateProjectForMemberChangesAsync(
            projectId,
            cancellationToken);

        if (projectError is not null)
        {
            return projectError;
        }

        if (request.UserId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "UserId is required"
            });
        }

        if (!Enum.IsDefined(typeof(ProjectRole), request.Role))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Invalid project role"
            });
        }

        var targetUser = await _dbContext.Users
            .FirstOrDefaultAsync(
                user => user.Id == request.UserId,
                cancellationToken);

        if (targetUser is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "USER_NOT_FOUND",
                Message = "User not found"
            });
        }

        if (!targetUser.IsActive)
        {
            return Conflict(new ErrorResponse
            {
                Code = "USER_NOT_ACTIVE",
                Message = "Inactive user cannot be added to project"
            });
        }

        var hasActiveMembers = await _dbContext.ProjectMembers
            .AnyAsync(
                member => member.ProjectId == projectId && member.IsActive,
                cancellationToken);

        if (!hasActiveMembers)
        {
            var firstMemberResult = ValidateFirstProjectMember(actorUserId, request);

            if (firstMemberResult is not null)
            {
                return firstMemberResult;
            }
        }
        else
        {
            var actorHasAccess = await ActorHasAccessAsync(
                actorUserId,
                projectId,
                ProjectAction.AddMember,
                cancellationToken);

            if (!actorHasAccess)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = "ACCESS_DENIED",
                    Message = "User does not have permission to add members"
                });
            }
        }

        var existingMember = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(
                member =>
                    member.ProjectId == projectId &&
                    member.UserId == request.UserId,
                cancellationToken);

        if (existingMember is not null && existingMember.IsActive)
        {
            return Conflict(new ErrorResponse
            {
                Code = "MEMBER_ALREADY_EXISTS",
                Message = "User is already an active member of this project"
            });
        }

        ProjectMember member;

        if (existingMember is not null)
        {
            existingMember.Role = request.Role;
            existingMember.JoinedAt = DateTime.UtcNow;
            existingMember.IsActive = true;

            member = existingMember;
        }
        else
        {
            member = new ProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = request.UserId,
                Role = request.Role,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };

            _dbContext.ProjectMembers.Add(member);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Created(
            $"/projects/{projectId}/members/{member.UserId}",
            ToResponse(member));
    }

    [HttpGet]
    public async Task<ActionResult<List<ProjectMemberResponse>>> GetMembers(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var projectError = await ValidateProjectExistsAsync(
            projectId,
            cancellationToken);

        if (projectError is not null)
        {
            return projectError;
        }

        var actorHasAccess = await ActorHasAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ViewProject,
            cancellationToken);

        if (!actorHasAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "ACCESS_DENIED",
                Message = "User does not have permission to view project members"
            });
        }

        var members = await _dbContext.ProjectMembers
            .Where(member => member.ProjectId == projectId && member.IsActive)
            .OrderBy(member => member.JoinedAt)
            .Select(member => ToResponse(member))
            .ToListAsync(cancellationToken);

        return Ok(members);
    }

    [HttpPatch("{userId:guid}/role")]
    public async Task<ActionResult<ProjectMemberResponse>> ChangeRole(
        Guid projectId,
        Guid userId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var projectError = await ValidateProjectForMemberChangesAsync(
            projectId,
            cancellationToken);

        if (projectError is not null)
        {
            return projectError;
        }

        if (userId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "UserId cannot be empty"
            });
        }

        if (!Enum.IsDefined(typeof(ProjectRole), request.Role))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Invalid project role"
            });
        }

        var actorHasAccess = await ActorHasAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ChangeMemberRole,
            cancellationToken);

        if (!actorHasAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "ACCESS_DENIED",
                Message = "User does not have permission to change member roles"
            });
        }

        var member = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(
                member =>
                    member.ProjectId == projectId &&
                    member.UserId == userId &&
                    member.IsActive,
                cancellationToken);

        if (member is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_MEMBER_NOT_FOUND",
                Message = "Project member not found"
            });
        }

        if (member.Role == ProjectRole.Owner && request.Role != ProjectRole.Owner)
        {
            var activeOwnerCount = await CountActiveOwnersAsync(
                projectId,
                cancellationToken);

            if (activeOwnerCount <= 1)
            {
                return Conflict(new ErrorResponse
                {
                    Code = "LAST_OWNER_CONFLICT",
                    Message = "Cannot change role of the last project owner"
                });
            }
        }

        member.Role = request.Role;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(member));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid projectId,
        Guid userId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var projectError = await ValidateProjectForMemberChangesAsync(
            projectId,
            cancellationToken);

        if (projectError is not null)
        {
            return projectError;
        }

        if (userId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "UserId cannot be empty"
            });
        }

        var actorHasAccess = await ActorHasAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.RemoveMember,
            cancellationToken);

        if (!actorHasAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "ACCESS_DENIED",
                Message = "User does not have permission to remove members"
            });
        }

        var member = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(
                member =>
                    member.ProjectId == projectId &&
                    member.UserId == userId &&
                    member.IsActive,
                cancellationToken);

        if (member is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_MEMBER_NOT_FOUND",
                Message = "Project member not found"
            });
        }

        if (member.Role == ProjectRole.Owner)
        {
            var activeOwnerCount = await CountActiveOwnersAsync(
                projectId,
                cancellationToken);

            if (activeOwnerCount <= 1)
            {
                return Conflict(new ErrorResponse
                {
                    Code = "LAST_OWNER_CONFLICT",
                    Message = "Cannot remove the last project owner"
                });
            }
        }

        member.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<ActionResult?> ValidateProjectForMemberChangesAsync(
        Guid projectId,
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

        var projectStatus = await _projectTaskClient.GetProjectStatusAsync(
            projectId,
            cancellationToken);

        if (projectStatus is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse
            {
                Code = "PROJECT_TASK_SERVICE_ERROR",
                Message = "Could not check project status in ProjectTaskService"
            });
        }

        if (!projectStatus.Exists)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_NOT_FOUND",
                Message = "Project not found"
            });
        }

        if (projectStatus.IsArchived)
        {
            return Conflict(new ErrorResponse
            {
                Code = "PROJECT_ARCHIVED",
                Message = "Cannot change members of archived project"
            });
        }

        return null;
    }

    private async Task<ActionResult?> ValidateProjectExistsAsync(
        Guid projectId,
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

        var projectStatus = await _projectTaskClient.GetProjectStatusAsync(
            projectId,
            cancellationToken);

        if (projectStatus is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse
            {
                Code = "PROJECT_TASK_SERVICE_ERROR",
                Message = "Could not check project status in ProjectTaskService"
            });
        }

        if (!projectStatus.Exists)
        {
            return NotFound(new ErrorResponse
            {
                Code = "PROJECT_NOT_FOUND",
                Message = "Project not found"
            });
        }

        return null;
    }

    private ActionResult? ValidateFirstProjectMember(
        Guid actorUserId,
        AddProjectMemberRequest request)
    {
        if (request.Role != ProjectRole.Owner)
        {
            return Conflict(new ErrorResponse
            {
                Code = "FIRST_MEMBER_MUST_BE_OWNER",
                Message = "First project member must have Owner role"
            });
        }

        if (request.UserId != actorUserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "FIRST_OWNER_MUST_BE_ACTOR",
                Message = "First project owner must be the actor user"
            });
        }

        return null;
    }

    private async Task<bool> ActorHasAccessAsync(
        Guid actorUserId,
        Guid projectId,
        ProjectAction action,
        CancellationToken cancellationToken)
    {
        var actor = await _dbContext.Users
            .FirstOrDefaultAsync(
                user => user.Id == actorUserId,
                cancellationToken);

        if (actor is null || !actor.IsActive)
        {
            return false;
        }

        var actorMembership = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(
                member =>
                    member.ProjectId == projectId &&
                    member.UserId == actorUserId &&
                    member.IsActive,
                cancellationToken);

        if (actorMembership is null)
        {
            return false;
        }

        return _accessPolicy.IsAllowed(actorMembership.Role, action);
    }

    private async Task<int> CountActiveOwnersAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ProjectMembers
            .CountAsync(
                member =>
                    member.ProjectId == projectId &&
                    member.Role == ProjectRole.Owner &&
                    member.IsActive,
                cancellationToken);
    }

    private static ProjectMemberResponse ToResponse(ProjectMember member)
    {
        return new ProjectMemberResponse
        {
            Id = member.Id,
            ProjectId = member.ProjectId,
            UserId = member.UserId,
            Role = member.Role,
            JoinedAt = member.JoinedAt,
            IsActive = member.IsActive
        };
    }
}