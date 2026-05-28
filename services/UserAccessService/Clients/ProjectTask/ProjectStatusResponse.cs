namespace UserAccessService.Clients.ProjectTask;

public class ProjectStatusResponse
{
    public Guid ProjectId { get; set; }

    public bool Exists { get; set; }

    public bool IsArchived { get; set; }
}