namespace EnergyAi.Server.Data.Entities;

public class ChatMessageRecord
{
    public Guid     Id        { get; set; }
    public Guid     SessionId { get; set; }
    public string   Role      { get; set; } = string.Empty;  // "user" | "assistant"
    public string   Content   { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ChatSession Session { get; set; } = null!;
}
