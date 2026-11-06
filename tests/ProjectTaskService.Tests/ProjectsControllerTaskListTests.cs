using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Controllers;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;
using Xunit;

namespace ProjectTaskService.Tests;

public class ProjectsControllerTaskListTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetTasks_UsesDefaultsSortsStablyAndExcludesDeletedTasks()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.AddRange(
            NewTask("00000000-0000-0000-0000-000000000003", CreatedAt.AddMinutes(1)),
            NewTask("00000000-0000-0000-0000-000000000002", CreatedAt),
            NewTask("00000000-0000-0000-0000-000000000001", CreatedAt),
            NewTask("00000000-0000-0000-0000-000000000004", CreatedAt, isDeleted: true));
        await db.SaveChangesAsync();

        var response = await GetPageAsync(db);

        Assert.Equal(1, response.Page);
        Assert.Equal(20, response.Limit);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
        Assert.Equal(new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Guid.Parse("00000000-0000-0000-0000-000000000003")
        }, response.Items.Select(task => task.Id));
    }

    [Fact]
    public async Task GetTasks_FiltersBeforeCountingAndPaginates()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        for (var index = 1; index <= 5; index++)
        {
            db.Tasks.Add(NewTask(
                $"00000000-0000-0000-0000-{index:D12}",
                CreatedAt.AddMinutes(index),
                index <= 4 ? TaskItemStatus.DONE : TaskItemStatus.TODO));
        }
        await db.SaveChangesAsync();

        var response = await GetPageAsync(db, page: "2", limit: "2", status: "done");

        Assert.Equal(2, response.Page);
        Assert.Equal(2, response.Limit);
        Assert.Equal(4, response.TotalCount);
        Assert.Equal(2, response.TotalPages);
        Assert.Equal(new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Guid.Parse("00000000-0000-0000-0000-000000000004")
        }, response.Items.Select(task => task.Id));
    }

    [Fact]
    public async Task GetTasks_ReturnsEmptyPageWhenPageIsPastTheEnd()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.Add(NewTask("00000000-0000-0000-0000-000000000001", CreatedAt));
        await db.SaveChangesAsync();

        var response = await GetPageAsync(db, page: int.MaxValue.ToString(), limit: "100");

        Assert.Empty(response.Items);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
        Assert.Equal(100, response.Limit);
    }

    [Fact]
    public async Task GetTasks_ReturnsZeroTotalPagesForEmptyResult()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var response = await GetPageAsync(db, status: "REVIEW");

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(0, response.TotalPages);
    }

    [Theory]
    [InlineData("0", null, null)]
    [InlineData("-1", null, null)]
    [InlineData("abc", null, null)]
    [InlineData(null, "0", null)]
    [InlineData(null, "101", null)]
    [InlineData(null, "abc", null)]
    [InlineData(null, null, "")]
    [InlineData(null, null, "UNKNOWN")]
    [InlineData(null, null, "1")]
    public async Task GetTasks_RejectsInvalidQueryParameters(
        string? page,
        string? limit,
        string? status)
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var result = await controller.GetTasksByProject(
            ProjectId, UserId, page, limit, status, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Equal("VALIDATION_ERROR", error.Code);
    }

    [Fact]
    public async Task GetTasks_ReturnsProjectNotFoundForMissingProject()
    {
        using var db = NewDb();

        var result = await NewController(db).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("PROJECT_NOT_FOUND", Assert.IsType<ErrorResponse>(notFound.Value).Code);
    }

    [Fact]
    public async Task GetTasks_PreservesAccessCheck()
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

    private static async Task<PagedTasksResponse> GetPageAsync(
        ProjectTaskDbContext db,
        string? page = null,
        string? limit = null,
        string? status = null)
    {
        var result = await NewController(db).GetTasksByProject(
            ProjectId, UserId, page, limit, status, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<PagedTasksResponse>(ok.Value);
    }

    private static ProjectTaskDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ProjectTaskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ProjectTaskDbContext(options);
    }

    private static ProjectsController NewController(ProjectTaskDbContext db, bool allowed = true) =>
        new(db, new StubUserAccessClient { Allowed = allowed });

    private static Project NewProject() => new()
    {
        Id = ProjectId,
        Name = "Project",
        CreatedByUserId = UserId,
        CreatedAt = CreatedAt,
        UpdatedAt = CreatedAt
    };

    private static TaskItem NewTask(
        string id,
        DateTime createdAt,
        TaskItemStatus status = TaskItemStatus.TODO,
        bool isDeleted = false) => new()
    {
        Id = Guid.Parse(id),
        ProjectId = ProjectId,
        Title = id,
        CreatedByUserId = UserId,
        Status = status,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        IsDeleted = isDeleted
    };

    private sealed class StubUserAccessClient : IUserAccessClient
    {
        public bool Allowed { get; init; }

        public Task<UserAccessUserResponse?> GetUserByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<UserAccessUserResponse?>(null);

        public Task<bool> AddProjectOwnerAsync(Guid projectId, Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<AccessCheckResponse?> CheckAccessAsync(
            Guid userId,
            Guid projectId,
            ProjectAction action,
            CancellationToken ct) =>
            Task.FromResult<AccessCheckResponse?>(new AccessCheckResponse { Allowed = Allowed });

        public Task<bool> IsMemberAsync(Guid userId, Guid projectId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
