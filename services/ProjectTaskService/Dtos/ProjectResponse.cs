namespace ProjectTaskService.Dtos;

public class ProjectResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsArchived { get; set; }
}