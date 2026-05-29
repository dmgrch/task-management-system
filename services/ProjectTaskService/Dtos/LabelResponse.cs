namespace ProjectTaskService.Dtos;

public class LabelResponse
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}