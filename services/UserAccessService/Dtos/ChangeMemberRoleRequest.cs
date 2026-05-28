using UserAccessService.Enums;

namespace UserAccessService.Dtos;

public class ChangeMemberRoleRequest
{
    public ProjectRole Role { get; set; }
}