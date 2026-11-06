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
    public async Task GetStatistics_EmptyProject_ReturnsAllStatusesWithZeroCounts()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var result = await NewService(db)
            .GetStatisticsAsync(ProjectId, OwnerId, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.ByStatus.Todo);
        Assert.Equal(0, result.ByStatus.InProgress);
        Assert.Equal(0, result.ByStatus.Review);
        Assert.Equal(0, result.ByStatus.Done);
    }

    [Fact]
    public async Task GetStatistics_CountsStatusesForRequestedProjectOnly()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.AddRange(
            NewTask(TaskItemStatus.TODO),
            NewTask(TaskItemStatus.TODO),
            NewTask(TaskItemStatus.IN_PROGRESS),
            NewTask(TaskItemStatus.REVIEW),
            NewTask(TaskItemStatus.DONE),
            NewTask(TaskItemStatus.DONE),
            NewTask(TaskItemStatus.DONE),
            NewTask(TaskItemStatus.TODO, projectId: Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await NewService(db)
            .GetStatisticsAsync(ProjectId, OwnerId, CancellationToken.None);

        Assert.Equal(7, result.TotalCount);
        Assert.Equal(2, result.ByStatus.Todo);
        Assert.Equal(1, result.ByStatus.InProgress);
        Assert.Equal(1, result.ByStatus.Review);
        Assert.Equal(3, result.ByStatus.Done);
    }

    [Fact]
    public async Task GetStatistics_ExcludesDeletedTasks()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        db.Tasks.AddRange(
            NewTask(TaskItemStatus.TODO),
            NewTask(TaskItemStatus.DONE, isDeleted: true));
        await db.SaveChangesAsync();

        var result = await NewService(db)
            .GetStatisticsAsync(ProjectId, OwnerId, CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.ByStatus.Todo);
        Assert.Equal(0, result.ByStatus.Done);
    }

    [Fact]
    public async Task GetStatistics_RejectsMissingUser()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            NewService(db).GetStatisticsAsync(ProjectId, null, CancellationToken.None));

        Assert.Equal(TaskErrorKind.Unauthorized, error.Kind);
        Assert.Equal("USER_ID_REQUIRED", error.Code);
    }

    [Fact]
    public async Task GetStatistics_RejectsForbiddenAccess()
    {
        using var db = NewDb();
        db.Projects.Add(NewProject());
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            NewService(db, allowed: false)
                .GetStatisticsAsync(ProjectId, OwnerId, CancellationToken.None));

        Assert.Equal(TaskErrorKind.Forbidden, error.Kind);
        Assert.Equal("ACCESS_DENIED", error.Code);
    }

    [Fact]
    public async Task GetStatistics_ReturnsProjectNotFound()
    {
        using var db = NewDb();

        var error = await Assert.ThrowsAsync<TaskOperationException>(() =>
            NewService(db).GetStatisticsAsync(ProjectId, OwnerId, CancellationToken.None));

        Assert.Equal(TaskErrorKind.NotFound, error.Kind);
        Assert.Equal("PROJECT_NOT_FOUND", error.Code);
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

    private static TaskItem NewTask(
        TaskItemStatus status = TaskItemStatus.TODO,
        bool isDeleted = false,
        Guid? projectId = null) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId ?? ProjectId, Title = "Task",
        CreatedByUserId = OwnerId, Status = status, IsDeleted = isDeleted,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

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
