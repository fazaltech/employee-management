namespace EmployeeApi.Models;

public sealed class SupportConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OrganizationId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SupportMessage
{
    public long Id { get; set; }
    public string ConversationId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public bool FromAdmin { get; set; }
    public string Text { get; set; } = "";
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
