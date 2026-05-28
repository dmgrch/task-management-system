using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;

namespace ProjectTaskService.Controllers;

[ApiController]
[Route("internal/projects")]
public class InternalProjectsController : ControllerBase
{
    private readonly ProjectTaskDbContext _dbContext;

    public InternalProjectsController(ProjectTaskDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("{projectId:guid}/status")]
    public async Task<ActionResult<ProjectStatusResponse>> GetProjectStatus(
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

        var project = await _dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(project => project.Id == projectId, cancellationToken);

        if (project is null)
        {
            return Ok(new ProjectStatusResponse
            {
                ProjectId = projectId,
                Exists = false,
                IsArchived = false
            });
        }

        return Ok(new ProjectStatusResponse
        {
            ProjectId = project.Id,
            Exists = true,
            IsArchived = project.IsArchived
        });
    }
}