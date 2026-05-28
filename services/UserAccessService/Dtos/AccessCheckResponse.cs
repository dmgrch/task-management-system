using UserAccessService.Enums;

namespace UserAccessService.Dtos;

public class AccessCheckResponse
{
    public bool Allowed { get; set; }

    public Guid UserId { get; set; }

    public Guid ProjectId { get; set; }

    public ProjectAction Action { get; set; }

    public ProjectRole? Role { get; set; }

    public string? Reason { get; set; }
}