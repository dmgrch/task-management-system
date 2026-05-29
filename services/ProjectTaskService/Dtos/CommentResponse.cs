using System;

namespace ProjectTaskService.Dtos;

public class CommentResponse
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}