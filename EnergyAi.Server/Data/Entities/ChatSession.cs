namespace EnergyAi.Server.Data.Entities;

public class ChatSession
{
    public Guid     Id        { get; set; }
    public string   Title     { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool     IsPinned  { get; set; }

    public List<ChatMessageRecord> Messages { get; set; } = [];
}
