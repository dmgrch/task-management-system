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

public class ProjectsControllerTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GetTasks_UsesDefaultsAndStableOrderingAndExcludesDeletedTasks()
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        db.Tasks.AddRange(
            NewTask(secondId, createdAt),
            NewTask(firstId, createdAt),
            NewTask(Guid.NewGuid(), createdAt.AddMinutes(-1), isDeleted: true));
        await db.SaveChangesAsync();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var response = ResponseFrom(result);
        Assert.Equal(1, response.Page);
        Assert.Equal(20, response.Limit);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
        Assert.Equal(new[] { firstId, secondId }, response.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task GetTasks_AppliesPaginationAndCalculatesMetadata()
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 5; index++)
            db.Tasks.Add(NewTask(Guid.NewGuid(), createdAt.AddMinutes(index), title: $"Task {index + 1}"));
        await db.SaveChangesAsync();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, "2", "2", null, CancellationToken.None);

        var response = ResponseFrom(result);
        Assert.Equal(2, response.Page);
        Assert.Equal(2, response.Limit);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(3, response.TotalPages);
        Assert.Equal(new[] { "Task 3", "Task 4" }, response.Items.Select(x => x.Title));
    }

    [Fact]
    public async Task GetTasks_FiltersBeforeCountingAndPaginating()
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        db.Tasks.AddRange(
            NewTask(Guid.NewGuid(), createdAt, TaskItemStatus.DONE),
            NewTask(Guid.NewGuid(), createdAt.AddMinutes(1), TaskItemStatus.TODO),
            NewTask(Guid.NewGuid(), createdAt.AddMinutes(2), TaskItemStatus.DONE));
        await db.SaveChangesAsync();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, "1", "1", "DONE", CancellationToken.None);

        var response = ResponseFrom(result);
        Assert.Single(response.Items);
        Assert.All(response.Items, item => Assert.Equal(TaskItemStatus.DONE, item.Status));
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.TotalPages);
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
    public async Task GetTasks_InvalidQueryReturnsValidationError(
        string? page, string? limit, string? status)
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, page, limit, status, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(badRequest.Value);
        Assert.Equal("VALIDATION_ERROR", error.Code);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("100")]
    public async Task GetTasks_AcceptsLimitBoundaries(string limit)
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, null, limit, null, CancellationToken.None);

        var response = ResponseFrom(result);
        Assert.Equal(int.Parse(limit), response.Limit);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(0, response.TotalPages);
    }

    [Fact]
    public async Task GetTasks_PagePastEndReturnsEmptyItemsWithUnchangedTotals()
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.Add(NewTask(Guid.NewGuid(), DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, int.MaxValue.ToString(), "100", null, CancellationToken.None);

        var response = ResponseFrom(result);
        Assert.Empty(response.Items);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
    }

    [Fact]
    public async Task GetTasks_MissingProjectKeepsExistingNotFoundBehavior()
    {
        await using var db = NewDb();

        var result = await Controller(db).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ErrorResponse>(notFound.Value);
        Assert.Equal("PROJECT_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task GetTasks_DeniedAccessKeepsExistingForbiddenBehavior()
    {
        await using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var result = await Controller(db, allowed: false).GetTasksByProject(
            ProjectId, UserId, null, null, null, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
        var error = Assert.IsType<ErrorResponse>(forbidden.Value);
        Assert.Equal("ACCESS_DENIED", error.Code);
    }

    private static PagedTaskResponse ResponseFrom(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<PagedTaskResponse>(ok.Value);
    }

    private static ProjectTaskDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ProjectTaskDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ProjectTaskDbContext(options);
    }

    private static ProjectsController Controller(ProjectTaskDbContext db, bool allowed = true) =>
        new(db, new StubUserAccessClient { Allowed = allowed });

    private static Project NewProject() => new()
    {
        Id = ProjectId,
        Name = "Project",
        CreatedByUserId = UserId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static TaskItem NewTask(
        Guid id,
        DateTime createdAt,
        TaskItemStatus status = TaskItemStatus.TODO,
        bool isDeleted = false,
        string title = "Task") => new()
    {
        Id = id,
        ProjectId = ProjectId,
        Title = title,
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
            Guid userId, Guid projectId, ProjectAction action, CancellationToken ct) =>
            Task.FromResult<AccessCheckResponse?>(new AccessCheckResponse { Allowed = Allowed });

        public Task<bool> IsMemberAsync(Guid userId, Guid projectId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
