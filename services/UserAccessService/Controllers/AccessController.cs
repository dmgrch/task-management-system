using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAccessService.Data;
using UserAccessService.Dtos;
using UserAccessService.Services;

namespace UserAccessService.Controllers;

[ApiController]
[Route("access")]
public class AccessController : ControllerBase
{
    private readonly UserAccessDbContext _dbContext;
    private readonly IAccessPolicy _accessPolicy;

    public AccessController(
        UserAccessDbContext dbContext,
        IAccessPolicy accessPolicy)
    {
        _dbContext = dbContext;
        _accessPolicy = accessPolicy;
    }

    [HttpPost("check")]
    public async Task<ActionResult<AccessCheckResponse>> CheckAccess(
        AccessCheckRequest request)
    {
        if (request.UserId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "UserId is required"
            });
        }

        if (request.ProjectId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "ProjectId is required"
            });
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == request.UserId);

        if (user is null)
        {
            return Ok(new AccessCheckResponse
            {
                Allowed = false,
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                Action = request.Action,
                Role = null,
                Reason = "USER_NOT_FOUND"
            });
        }

        if (!user.IsActive)
        {
            return Ok(new AccessCheckResponse
            {
                Allowed = false,
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                Action = request.Action,
                Role = null,
                Reason = "USER_NOT_ACTIVE"
            });
        }

        var membership = await _dbContext.ProjectMembers
            .FirstOrDefaultAsync(member =>
                member.ProjectId == request.ProjectId &&
                member.UserId == request.UserId &&
                member.IsActive);

        if (membership is null)
        {
            return Ok(new AccessCheckResponse
            {
                Allowed = false,
                UserId = request.UserId,
                ProjectId = request.ProjectId,
                Action = request.Action,
                Role = null,
                Reason = "USER_NOT_PROJECT_MEMBER"
            });
        }

        var allowed = _accessPolicy.IsAllowed(membership.Role, request.Action);

        return Ok(new AccessCheckResponse
        {
            Allowed = allowed,
            UserId = request.UserId,
            ProjectId = request.ProjectId,
            Action = request.Action,
            Role = membership.Role,
            Reason = allowed ? null : "ACCESS_DENIED"
        });
    }
}