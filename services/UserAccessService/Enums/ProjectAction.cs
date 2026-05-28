namespace UserAccessService.Enums;

public enum ProjectAction
{
    ViewProject = 1,
    ViewTask = 2,
    ManageProject = 3,

    AddMember = 4,
    RemoveMember = 5,
    ChangeMemberRole = 6,

    CreateTask = 7,
    AssignTask = 8,
    ChangeTaskStatus = 9,

    AddComment = 10,

    EditTask = 11,
    DeleteTask = 12,
    DeleteComment = 13,

    ViewTaskHistory = 14,
    ArchiveProject = 15
}