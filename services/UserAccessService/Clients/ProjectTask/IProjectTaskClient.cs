namespace UserAccessService.Clients.ProjectTask;

public interface IProjectTaskClient
{
    Task<ProjectStatusResponse?> GetProjectStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}