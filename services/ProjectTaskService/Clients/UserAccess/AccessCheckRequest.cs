using ProjectTaskService.Enums;

namespace ProjectTaskService.Clients.UserAccess;

public class AccessCheckRequest
{
    public Guid UserId { get; set; }

    public Guid ProjectId { get; set; }

    public ProjectAction Action { get; set; }
}