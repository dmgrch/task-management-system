using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public ProjectMembersController(
        UserAccessDbContext dbContext,
        IAccessPolicy accessPolicy)
    {
        _dbContext = dbContext;
        _accessPolicy = accessPolicy;
    }

    [HttpPost]
    public async Task<ActionResult<ProjectMemberResponse>> AddMember(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId,
        AddProjectMemberRequest request)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
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
            .FirstOrDefaultAsync(user => user.Id == request.UserId);

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
            .AnyAsync(member => member.ProjectId == projectId && member.IsActive);

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
                ProjectAction.AddMember);

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
            .FirstOrDefaultAsync(member =>
                member.ProjectId == projectId &&
                member.UserId == request.UserId);

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

        await _dbContext.SaveChangesAsync();

        return Created(
            $"/projects/{projectId}/members/{member.UserId}",
            ToResponse(member));
    }

    [HttpGet]
    public async Task<ActionResult<List<ProjectMemberResponse>>> GetMembers(
        Guid projectId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var actorHasAccess = await ActorHasAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.ViewProject);

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
            .ToListAsync();

        return Ok(members);
    }

    [HttpPatch("{userId:guid}/role")]
    public async Task<ActionResult<ProjectMemberResponse>> ChangeRole(
        Guid projectId,
        Guid userId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId,
        ChangeMemberRoleRequest request)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
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
            ProjectAction.ChangeMemberRole);

        if (!actorHasAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "ACCESS_DENIED",
                Message = "User does not have permission to change member roles"
            });
        }

        var member = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(member =>
                member.ProjectId == projectId &&
                member.UserId == userId &&
                member.IsActive);

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
            var activeOwnerCount = await CountActiveOwnersAsync(projectId);

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

        await _dbContext.SaveChangesAsync();

        return Ok(ToResponse(member));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid projectId,
        Guid userId,
        [FromHeader(Name = "X-User-Id")] Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = "USER_ID_REQUIRED",
                Message = "X-User-Id header is required"
            });
        }

        var actorHasAccess = await ActorHasAccessAsync(
            actorUserId,
            projectId,
            ProjectAction.RemoveMember);

        if (!actorHasAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = "ACCESS_DENIED",
                Message = "User does not have permission to remove members"
            });
        }

        var member = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(member =>
                member.ProjectId == projectId &&
                member.UserId == userId &&
                member.IsActive);

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
            var activeOwnerCount = await CountActiveOwnersAsync(projectId);

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

        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    private ActionResult<ProjectMemberResponse>? ValidateFirstProjectMember(
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
        ProjectAction action)
    {
        var actor = await _dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == actorUserId);

        if (actor is null || !actor.IsActive)
        {
            return false;
        }

        var actorMembership = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(member =>
                member.ProjectId == projectId &&
                member.UserId == actorUserId &&
                member.IsActive);

        if (actorMembership is null)
        {
            return false;
        }

        return _accessPolicy.IsAllowed(actorMembership.Role, action);
    }

    private async Task<int> CountActiveOwnersAsync(Guid projectId)
    {
        return await _dbContext.ProjectMembers
            .CountAsync(member =>
                member.ProjectId == projectId &&
                member.Role == ProjectRole.Owner &&
                member.IsActive);
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