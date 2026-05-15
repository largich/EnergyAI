namespace EnergyAi.Server.Dtos;

// ---- Inbound ----

public record ChatMessageDto(string Role, string Content);

public record ChatRequest(
    List<ChatMessageDto> Messages,
    Guid?   SessionId = null,
    bool    Persist   = false);

// ---- Session responses ----

public record ChatSessionDto(
    Guid     Id,
    string   Title,
    DateTime CreatedAt,
    bool     IsPinned);

public record ChatSessionDetailDto(
    Guid                  Id,
    string                Title,
    DateTime              CreatedAt,
    bool                  IsPinned,
    List<ChatMessageDto>  Messages);
