using UserAccessService.Enums;

namespace UserAccessService.Dtos;

public class AddProjectMemberRequest
{
    public Guid UserId { get; set; }

    public ProjectRole Role { get; set; }
}