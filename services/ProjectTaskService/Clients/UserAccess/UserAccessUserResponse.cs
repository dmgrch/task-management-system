namespace ProjectTaskService.Clients.UserAccess;

public class UserAccessUserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }
}