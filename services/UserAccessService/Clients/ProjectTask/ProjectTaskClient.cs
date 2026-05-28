using System.Net.Http.Json;

namespace UserAccessService.Clients.ProjectTask;

public class ProjectTaskClient : IProjectTaskClient
{
    private readonly HttpClient _httpClient;

    public ProjectTaskClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProjectStatusResponse?> GetProjectStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/internal/projects/{projectId}/status",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ProjectStatusResponse>(
            cancellationToken: cancellationToken);
    }
}