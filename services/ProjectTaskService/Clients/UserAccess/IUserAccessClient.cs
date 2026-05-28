using ProjectTaskService.Enums;

namespace ProjectTaskService.Clients.UserAccess;

public interface IUserAccessClient
{
    Task<UserAccessUserResponse?> GetUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken);
    
    Task<bool> AddProjectOwnerAsync(
        Guid projectId,
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<AccessCheckResponse?> CheckAccessAsync(
        Guid userId,
        Guid projectId,
        ProjectAction action,
        CancellationToken cancellationToken);
}