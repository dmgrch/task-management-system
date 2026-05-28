using UserAccessService.Enums;

namespace UserAccessService.Dtos;

public class AccessCheckRequest
{
    public Guid UserId { get; set; }

    public Guid ProjectId { get; set; }

    public ProjectAction Action { get; set; }
}