namespace CleanArchitectureBase.Application.Comments.Dto;

public class CommentDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string? Content { get; set; }
    public UserCreateCommentDto? CreateBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int ReplyCount  { get; set; }
    public List<CommentDto>? ChildComments { get; set; }
}

public class UserCreateCommentDto
{
    public Guid? UserId { get; set; }
    public string? FullName { get; set; }
    public Guid? AvatarId { get; set; }
}
