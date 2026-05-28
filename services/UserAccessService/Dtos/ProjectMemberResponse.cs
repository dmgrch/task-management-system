using UserAccessService.Enums;

namespace UserAccessService.Dtos;

public class ProjectMemberResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public ProjectRole Role { get; set; }

    public DateTime JoinedAt { get; set; }

    public bool IsActive { get; set; }
}