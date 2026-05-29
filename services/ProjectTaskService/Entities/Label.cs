namespace ProjectTaskService.Entities;

public class Label
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}