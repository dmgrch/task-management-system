using Microsoft.EntityFrameworkCore;
using ProjectTaskService.Clients.UserAccess;
using ProjectTaskService.Data;
using ProjectTaskService.Dtos;
using ProjectTaskService.Entities;
using ProjectTaskService.Enums;
using ProjectTaskService.Repositories;
using ProjectTaskService.Services;
using Xunit;

namespace ProjectTaskService.Tests;

public class TaskServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Create_CreatesTaskAndHistory()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();
        var service = NewService(db);

        var result = await service.CreateAsync(new CreateTaskRequest
        {
            ProjectId = ProjectId, CreatedByUserId = OwnerId,
            Title = "  New task  ", Description = "  Description  "
        }, CancellationToken.None);

        Assert.Equal("New task", result.Title);
        Assert.Equal("Description", result.Description);
        Assert.Equal(TaskItemStatus.TODO, result.Status);
        Assert.Single(db.Tasks);
        Assert.Contains(db.TaskHistories, h => h.TaskId == result.Id && h.ChangeType == TaskChangeType.Created);
    }

    [Fact]
    public async Task Create_RejectsBlankTitle()
    {
        using var db = NewDb();
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            service.CreateAsync(new CreateTaskRequest
            {
                CreatedByUserId = OwnerId, Title = "  "
            }, CancellationToken.None));

        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.Equal(TaskErrorKind.Validation, error.Kind);
        Assert.Empty(db.Tasks);
    }

    [Fact]
    public async Task Create_RejectsForbiddenAccess()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();
        var service = NewService(db, allowed: false);

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            service.CreateAsync(new CreateTaskRequest
            {
                ProjectId = ProjectId, CreatedByUserId = OwnerId, Title = "Test"
            }, CancellationToken.None));

        Assert.Equal("ACCESS_DENIED", error.Code);
        Assert.Empty(db.Tasks);
    }

    [Fact]
    public async Task Get_DoesNotReturnDeletedTask()
    {
        using var db = NewDb();
        var task = NewTask();
        task.IsDeleted = true;
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        var service = NewService(db);

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            service.GetAsync(task.Id, OwnerId, CancellationToken.None));

        Assert.Equal("TASK_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task Get_ReturnsOnlyActiveLabels()
    {
        using var db = NewDb();
        var task = NewTask();
        var active = new Label { Id = Guid.NewGuid(), ProjectId = ProjectId, Name = "Bug", IsActive = true };
        var inactive = new Label { Id = Guid.NewGuid(), ProjectId = ProjectId, Name = "Old", IsActive = false };
        db.Tasks.Add(task);
        db.Labels.AddRange(active, inactive);
        db.TaskLabels.AddRange(
            new TaskLabel { TaskId = task.Id, LabelId = active.Id },
            new TaskLabel { TaskId = task.Id, LabelId = inactive.Id });
        await db.SaveChangesAsync();

        var result = await NewService(db).GetAsync(task.Id, OwnerId, CancellationToken.None);

        Assert.Single(result.Labels);
        Assert.Equal("Bug", result.Labels[0].Name);
    }

    [Fact]
    public async Task GetProjectTasks_ReturnsRequestedPageInStableOrder()
    {
        using var db = NewDb();
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        db.Tasks.AddRange(
            NewTask(Guid.Parse("00000000-0000-0000-0000-000000000004"), createdAt.AddMinutes(1)),
            NewTask(Guid.Parse("00000000-0000-0000-0000-000000000003"), createdAt),
            NewTask(Guid.Parse("00000000-0000-0000-0000-000000000001"), createdAt),
            NewTask(Guid.Parse("00000000-0000-0000-0000-000000000002"), createdAt));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetProjectTasksAsync(
            ProjectId, page: 2, limit: 2, status: null, ct: CancellationToken.None);

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.Limit);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(
            new[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Guid.Parse("00000000-0000-0000-0000-000000000004")
            },
            result.Items.Select(task => task.Id));
    }

    [Fact]
    public async Task GetProjectTasks_FiltersBeforeCountingAndExcludesDeletedTasks()
    {
        using var db = NewDb();
        var todo = NewTask();
        var done = NewTask();
        done.Status = TaskItemStatus.DONE;
        var deletedDone = NewTask();
        deletedDone.Status = TaskItemStatus.DONE;
        deletedDone.IsDeleted = true;
        var otherProjectDone = NewTask();
        otherProjectDone.ProjectId = Guid.NewGuid();
        otherProjectDone.Status = TaskItemStatus.DONE;
        db.Tasks.AddRange(todo, done, deletedDone, otherProjectDone);
        await db.SaveChangesAsync();

        var result = await NewService(db).GetProjectTasksAsync(
            ProjectId, page: 1, limit: 20, status: TaskItemStatus.DONE, ct: CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Collection(result.Items, item => Assert.Equal(done.Id, item.Id));
    }

    [Fact]
    public async Task GetProjectTasks_EmptyResultHasZeroTotalPages()
    {
        using var db = NewDb();

        var result = await NewService(db).GetProjectTasksAsync(
            ProjectId, page: 1, limit: 100, status: null, ct: CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task GetProjectTasks_VeryLargePageReturnsEmptyItemsWithoutOverflow()
    {
        using var db = NewDb();
        db.Tasks.Add(NewTask());
        await db.SaveChangesAsync();

        var result = await NewService(db).GetProjectTasksAsync(
            ProjectId, page: int.MaxValue, limit: 100, status: null, ct: CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task ChangeStatus_RejectsInvalidTransition()
    {
        using var db = NewDb();
        var task = NewTask();
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            NewService(db).ChangeStatusAsync(task.Id, new ChangeStatusRequest
            {
                ActorUserId = OwnerId, NewStatus = TaskItemStatus.DONE
            }, CancellationToken.None));

        Assert.Equal("INVALID_STATUS_TRANSITION", error.Code);
        Assert.Equal(TaskItemStatus.TODO, task.Status);
    }

    [Fact]
    public async Task ChangeStatus_RequiresAssigneeForDone()
    {
        using var db = NewDb();
        var task = NewTask();
        task.Status = TaskItemStatus.REVIEW;
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            NewService(db).ChangeStatusAsync(task.Id, new ChangeStatusRequest
            {
                ActorUserId = OwnerId, NewStatus = TaskItemStatus.DONE
            }, CancellationToken.None));

        Assert.Equal("NO_ASSIGNEE", error.Code);
    }

    [Fact]
    public async Task ChangeStatus_RecordsHistory()
    {
        using var db = NewDb();
        var task = NewTask();
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await NewService(db).ChangeStatusAsync(task.Id, new ChangeStatusRequest
        {
            ActorUserId = OwnerId, NewStatus = TaskItemStatus.IN_PROGRESS
        }, CancellationToken.None);

        Assert.Equal(TaskItemStatus.IN_PROGRESS, task.Status);
        Assert.Contains(db.TaskHistories, h => h.ChangeType == TaskChangeType.StatusChanged
            && h.OldValue == "TODO" && h.NewValue == "IN_PROGRESS");
    }

    [Fact]
    public async Task Assign_RejectsNonMember()
    {
        using var db = NewDb();
        var task = NewTask();
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            NewService(db, isMember: false).AssignAsync(task.Id, new AssignAssigneeRequest
            {
                ActorUserId = OwnerId, AssigneeUserId = Guid.NewGuid()
            }, CancellationToken.None));

        Assert.Equal("ASSIGNEE_NOT_MEMBER", error.Code);
    }

    [Fact]
    public async Task Delete_UsesSoftDelete()
    {
        using var db = NewDb();
        var task = NewTask();
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await NewService(db).DeleteAsync(task.Id, OwnerId, CancellationToken.None);

        Assert.True(task.IsDeleted);
        Assert.Single(db.Tasks); // record still exists in the database
        var repository = new TaskRepository(db);
        Assert.Null(await repository.GetTaskAsync(task.Id, CancellationToken.None));
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
        Id = ProjectId, Name = "Project", CreatedByUserId = OwnerId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static TaskItem NewTask() => new()
    {
        Id = Guid.NewGuid(), ProjectId = ProjectId, Title = "Task",
        CreatedByUserId = OwnerId, Status = TaskItemStatus.TODO,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static TaskItem NewTask(Guid id, DateTime createdAt)
    {
        var task = NewTask();
        task.Id = id;
        task.CreatedAt = createdAt;
        task.UpdatedAt = createdAt;
        return task;
    }

    private static TaskService NewService(ProjectTaskDbContext db, bool allowed = true, bool isMember = true) =>
        new(new TaskRepository(db), new StubUserAccessClient { Allowed = allowed, IsMember = isMember });

    private sealed class StubUserAccessClient : IUserAccessClient
    {
        public bool Allowed { get; init; }
        public bool IsMember { get; init; }

        public Task<UserAccessUserResponse?> GetUserByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<UserAccessUserResponse?>(null);

        public Task<bool> AddProjectOwnerAsync(Guid projectId, Guid ownerUserId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<AccessCheckResponse?> CheckAccessAsync(Guid userId, Guid projectId,
            ProjectAction action, CancellationToken ct) =>
            Task.FromResult<AccessCheckResponse?>(new AccessCheckResponse { Allowed = Allowed });

        public Task<bool> IsMemberAsync(Guid userId, Guid projectId, CancellationToken ct) =>
            Task.FromResult(IsMember);
    }
}
