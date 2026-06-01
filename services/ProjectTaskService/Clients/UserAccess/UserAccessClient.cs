using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectTaskService.Enums;

namespace ProjectTaskService.Clients.UserAccess;

public class UserAccessClient : IUserAccessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly HttpClient _httpClient;

    public UserAccessClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UserAccessUserResponse?> GetUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/users/{userId}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<UserAccessUserResponse>(JsonOptions, cancellationToken);
    }

    public async Task<bool> AddProjectOwnerAsync(
        Guid projectId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var requestBody = new AddProjectMemberRequest
        {
            UserId = ownerUserId,
            Role = ProjectRole.Owner
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/projects/{projectId}/members");

        request.Headers.Add("X-User-Id", ownerUserId.ToString());

        request.Content = JsonContent.Create(
            requestBody,
            options: JsonOptions);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<AccessCheckResponse?> CheckAccessAsync(
        Guid userId,
        Guid projectId,
        ProjectAction action,
        CancellationToken cancellationToken)
    {
        var requestBody = new AccessCheckRequest
        {
            UserId = userId,
            ProjectId = projectId,
            Action = action
        };

        using var response = await _httpClient.PostAsJsonAsync(
            "/access/check",
            requestBody,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<AccessCheckResponse>(
            JsonOptions,
            cancellationToken);
    }
    public async Task<bool> IsMemberAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/projects/{projectId}/members/{userId}/is-member", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return false;
        var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return content.TryGetProperty("isMember", out var isMember) && isMember.GetBoolean();
    }
}