using UserAccessService.Enums;

namespace UserAccessService.Services;

public interface IAccessPolicy
{
    bool IsAllowed(ProjectRole role, ProjectAction action);
}