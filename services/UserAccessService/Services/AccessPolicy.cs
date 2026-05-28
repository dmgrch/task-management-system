using UserAccessService.Enums;

namespace UserAccessService.Services;

public class AccessPolicy : IAccessPolicy
{
    public bool IsAllowed(ProjectRole role, ProjectAction action)
    {
        return role switch
        {
            ProjectRole.Owner => IsOwnerAllowed(action),
            ProjectRole.Admin => IsAdminAllowed(action),
            ProjectRole.Member => IsMemberAllowed(action),
            _ => false
        };
    }

    private static bool IsOwnerAllowed(ProjectAction action)
    {
        return true;
    }

    private static bool IsAdminAllowed(ProjectAction action)
    {
        return action switch
        {
            ProjectAction.ViewProject => true,
            ProjectAction.ViewTask => true,

            ProjectAction.AddMember => true,
            ProjectAction.RemoveMember => true,
            ProjectAction.ChangeMemberRole => true,

            ProjectAction.CreateTask => true,
            ProjectAction.AssignTask => true,
            ProjectAction.ChangeTaskStatus => true,

            ProjectAction.AddComment => true,

            ProjectAction.EditTask => true,
            ProjectAction.DeleteTask => true,
            ProjectAction.DeleteComment => true,

            ProjectAction.ViewTaskHistory => true,

            ProjectAction.ManageProject => false,
            ProjectAction.ArchiveProject => false,

            _ => false
        };
    }

    private static bool IsMemberAllowed(ProjectAction action)
    {
        return action switch
        {
            ProjectAction.ViewProject => true,
            ProjectAction.ViewTask => true,

            ProjectAction.CreateTask => true,
            ProjectAction.ChangeTaskStatus => true,
            ProjectAction.AddComment => true,
            ProjectAction.ViewTaskHistory => true,

            ProjectAction.ManageProject => false,
            ProjectAction.AddMember => false,
            ProjectAction.RemoveMember => false,
            ProjectAction.ChangeMemberRole => false,
            ProjectAction.AssignTask => false,
            ProjectAction.EditTask => false,
            ProjectAction.DeleteTask => false,
            ProjectAction.DeleteComment => false,
            ProjectAction.ArchiveProject => false,

            _ => false
        };
    }
}