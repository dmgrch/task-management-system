using UserAccessService.Enums;
using UserAccessService.Services;
using Xunit;

namespace UserAccessService.Tests;

public class AccessPolicyTests
{
    private readonly AccessPolicy _policy = new();

    [Theory]
    [InlineData(ProjectAction.ManageProject)]
    [InlineData(ProjectAction.ArchiveProject)]
    [InlineData(ProjectAction.AddMember)]
    public void Owner_CanPerformProjectActions(ProjectAction action)
    {
        Assert.True(_policy.IsAllowed(ProjectRole.Owner, action));
    }

    [Theory]
    [InlineData(ProjectAction.CreateTask)]
    [InlineData(ProjectAction.AssignTask)]
    [InlineData(ProjectAction.EditTask)]
    public void Admin_CanManageTasks(ProjectAction action)
    {
        Assert.True(_policy.IsAllowed(ProjectRole.Admin, action));
    }

    [Theory]
    [InlineData(ProjectAction.ManageProject)]
    [InlineData(ProjectAction.ArchiveProject)]
    public void Admin_CannotManageOrArchiveProject(ProjectAction action)
    {
        Assert.False(_policy.IsAllowed(ProjectRole.Admin, action));
    }

    [Theory]
    [InlineData(ProjectAction.ViewTask)]
    [InlineData(ProjectAction.CreateTask)]
    [InlineData(ProjectAction.ChangeTaskStatus)]
    public void Member_HasBasicTaskAccess(ProjectAction action)
    {
        Assert.True(_policy.IsAllowed(ProjectRole.Member, action));
    }

    [Theory]
    [InlineData(ProjectAction.AssignTask)]
    [InlineData(ProjectAction.DeleteTask)]
    [InlineData(ProjectAction.AddMember)]
    public void Member_CannotPerformAdministrativeActions(ProjectAction action)
    {
        Assert.False(_policy.IsAllowed(ProjectRole.Member, action));
    }
}
