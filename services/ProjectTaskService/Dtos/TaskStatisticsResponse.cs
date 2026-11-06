namespace ProjectTaskService.Dtos;

public class TaskStatisticsResponse
{
    public int TotalCount { get; set; }
    public TaskStatisticsByStatusResponse ByStatus { get; set; } = new();
}

public class TaskStatisticsByStatusResponse
{
    public int Todo { get; set; }
    public int InProgress { get; set; }
    public int Review { get; set; }
    public int Done { get; set; }
}
