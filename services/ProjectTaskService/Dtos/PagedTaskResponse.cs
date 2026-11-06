namespace ProjectTaskService.Dtos;

public class PagedTaskResponse
{
    public List<TaskResponse> Items { get; set; } = new();

    public int Page { get; set; }

    public int Limit { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}
