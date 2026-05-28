using ProjectTaskService.Enums;

namespace ProjectTaskService.Dtos;

public class ChangeStatusRequest
{
    public TaskItemStatus NewStatus { get; set; }
    public Guid ActorUserId { get; set; }
}