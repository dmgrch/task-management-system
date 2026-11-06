using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Controllers;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;
using ProjectTaskService.Repositories;
using ProjectTaskService.Services;
using Xunit;

namespace ProjectTaskService.Tests;

public class ProjectsControllerTaskListTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GetTasksByProject_UsesDefaultPagination()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Title = "Task",
            Status = TaskItemStatus.TODO,
            CreatedByUserId = UserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await NewController(db).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var response = Assert.IsType<PagedTaskResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, response.Page);
        Assert.Equal(20, response.Limit);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
        Assert.Single(response.Items);
    }

    [Theory]
    [InlineData("0", null, null)]
    [InlineData("-1", null, null)]
    [InlineData("abc", null, null)]
    [InlineData(null, "0", null)]
    [InlineData(null, "101", null)]
    [InlineData(null, "abc", null)]
    [InlineData(null, null, "UNKNOWN")]
    [InlineData(null, null, "1")]
    [InlineData(null, null, "")]
    public async Task GetTasksByProject_InvalidQueryReturnsStructuredValidationError(
        string? page,
        string? limit,
        string? status)
    {
        using var db = NewDb();

        var result = await NewController(db).GetTasksByProject(
            ProjectId, UserId, page, limit, status, CancellationToken.None);

        var error = Assert.IsType<ErrorResponse>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal("VALIDATION_ERROR", error.Code);
    }

    [Fact]
    public async Task GetTasksByProject_MissingProjectStillReturnsNotFound()
    {
        using var db = NewDb();

        var result = await NewController(db).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("PROJECT_NOT_FOUND", Assert.IsType<ErrorResponse>(notFound.Value).Code);
    }

    [Fact]
    public async Task GetTasksByProject_AcceptsCaseInsensitiveStatusFilter()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.AddRange(
            NewTask(TaskItemStatus.TODO),
            NewTask(TaskItemStatus.DONE));
        await db.SaveChangesAsync();

        var result = await NewController(db).GetTasksByProject(
            ProjectId, UserId, "1", "100", "done", CancellationToken.None);

        var response = Assert.IsType<PagedTaskResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(TaskItemStatus.DONE, Assert.Single(response.Items).Status);
    }

    [Fact]
    public async Task GetTasksByProject_ForbiddenAccessStillReturnsForbidden()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var result = await NewController(db, allowed: false).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal("ACCESS_DENIED", Assert.IsType<ErrorResponse>(forbidden.Value).Code);
    }

    [Fact]
    public async Task GetTasksByProject_MissingUserHeaderStillReturnsUnauthorized()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var result = await NewController(db).GetTasksByProject(
            ProjectId, null, null, null, null, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("USER_ID_REQUIRED", Assert.IsType<ErrorResponse>(unauthorized.Value).Code);
    }

    private static ProjectTaskDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ProjectTaskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ProjectTaskDbContext(options);
    }

    private static Project NewProject() => new()
    {
        Id = ProjectId,
        Name = "Project",
        CreatedByUserId = UserId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static TaskItem NewTask(TaskItemStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = ProjectId,
        Title = "Task",
        Status = status,
        CreatedByUserId = UserId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ProjectsController NewController(ProjectTaskDbContext db, bool allowed = true)
    {
        var access = new StubUserAccessClient(allowed);
        var tasks = new TaskService(new TaskRepository(db), access);
        return new ProjectsController(db, access, tasks);
    }

    private sealed class StubUserAccessClient(bool allowed) : IUserAccessClient
    {
        public Task<UserAccessUserResponse?> GetUserByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<UserAccessUserResponse?>(null);

        public Task<bool> AddProjectOwnerAsync(Guid projectId, Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<AccessCheckResponse?> CheckAccessAsync(
            Guid userId,
            Guid projectId,
            ProjectAction action,
            CancellationToken ct) =>
            Task.FromResult<AccessCheckResponse?>(new AccessCheckResponse { Allowed = allowed });

        public Task<bool> IsMemberAsync(Guid userId, Guid projectId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
