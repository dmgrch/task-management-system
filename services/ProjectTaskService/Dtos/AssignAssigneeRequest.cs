namespace ProjectTaskService.Dtos;

public class AssignAssigneeRequest
{
    public Guid? AssigneeUserId { get; set; }
    public Guid ActorUserId { get; set; }
}