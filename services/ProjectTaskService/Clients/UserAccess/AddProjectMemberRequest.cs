using ProjectTaskService.Enums;

namespace ProjectTaskService.Clients.UserAccess;

public class AddProjectMemberRequest
{
    public Guid UserId { get; set; }

    public ProjectRole Role { get; set; }
}