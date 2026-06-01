using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAccessService.Data;
using UserAccessService.Dtos;
using UserAccessService.Entities;

namespace UserAccessService.Controllers;

[ApiController]
[Route("users")]
public class UsersController : ControllerBase
{
    private readonly UserAccessDbContext _dbContext;

    public UsersController(UserAccessDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser(CreateUserRequest request)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "FullName is required"
            });
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Email is required"
            });
        }

        if (fullName.Length > 200)
        {
            return BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "FullName must be less than or equal to 200 characters"
            });
        }

        if (email.Length > 320)
        {
            BadRequest(new ErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = "Email must be less than or equal to 320 characters"
            });
        }

        var emailAlreadyExists = await _dbContext.Users.AnyAsync(user => user.Email == email);

        if (emailAlreadyExists)
        {
            return Conflict(new ErrorResponse
            {
                Code = "EMAIL_ALREADY_EXISTS",
                Message = "User with this email already exists"
            });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var response = ToResponse(user);

        return CreatedAtAction(
            nameof(GetUserById),
            new {userId = user.Id},
            response);
    }

    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetUsers()
    {
        var users = await _dbContext.Users.OrderBy(user => user.CreatedAt).Select(user => ToResponse(user)).ToListAsync();

        return Ok(users);
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<UserResponse>> GetUserById(Guid userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId);

        if (user is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = "USER_NOT_FOUND",
                Message = "User not found"
            });
        }

        return Ok(ToResponse(user));
    }

    [HttpPatch("{userId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateUser(Guid userId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId);

        if (user is null)
        {
            return NotFound(new ErrorResponse
            {
                Code ="USER_NOT_FOUND",
                Message = "User not found"
            });
        }

        if (!user.IsActive)
        {
            return Conflict(new ErrorResponse
            {
                Code = "USER_ALREADY_INACTIVE",
                Message = "User is already inactive"
            });
        }

        user.IsActive = false;

        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            CreatedAt = user.CreatedAt,
            IsActive = user.IsActive
        };
    }
}