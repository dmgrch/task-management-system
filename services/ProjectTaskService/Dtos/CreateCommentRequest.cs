using System;

namespace ProjectTaskService.Dtos;

public class CreateCommentRequest
{
    public Guid TaskId { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid AuthorUserId { get; set; }
}