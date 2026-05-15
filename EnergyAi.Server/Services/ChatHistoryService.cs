using EnergyAi.Server.Data;
using EnergyAi.Server.Data.Entities;
using EnergyAi.Server.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EnergyAi.Server.Services;

public interface IChatHistoryService
{
    Task<ChatSession>            CreateSessionAsync(string firstUserMessage, CancellationToken ct = default);
    Task                         AppendMessagesAsync(Guid sessionId, string userContent, string assistantContent, CancellationToken ct = default);
    Task<List<ChatSessionDto>>   GetSessionsAsync(CancellationToken ct = default);
    Task<ChatSessionDetailDto?>  GetSessionAsync(Guid id, CancellationToken ct = default);
    Task<bool>                   DeleteSessionAsync(Guid id, CancellationToken ct = default);
}

public class ChatHistoryService : IChatHistoryService
{
    private readonly ChatDbContext _db;

    public ChatHistoryService(ChatDbContext db) => _db = db;

    public async Task<ChatSession> CreateSessionAsync(string firstUserMessage, CancellationToken ct = default)
    {
        var title = firstUserMessage.Length <= 80
            ? firstUserMessage
            : firstUserMessage[..77] + "…";

        var session = new ChatSession
        {
            Id        = Guid.NewGuid(),
            Title     = title,
            CreatedAt = DateTime.UtcNow
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public async Task AppendMessagesAsync(
        Guid sessionId, string userContent, string assistantContent,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        _db.Messages.AddRange(
            new ChatMessageRecord { Id = Guid.NewGuid(), SessionId = sessionId, Role = "user",      Content = userContent,      CreatedAt = now },
            new ChatMessageRecord { Id = Guid.NewGuid(), SessionId = sessionId, Role = "assistant", Content = assistantContent, CreatedAt = now.AddMicroseconds(1) }
        );
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<ChatSessionDto>> GetSessionsAsync(CancellationToken ct = default)
        => await _db.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.IsPinned)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => new ChatSessionDto(s.Id, s.Title, s.CreatedAt, s.IsPinned))
            .ToListAsync(ct);

    public async Task<ChatSessionDetailDto?> GetSessionAsync(Guid id, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .AsNoTracking()
            .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null) return null;

        return new ChatSessionDetailDto(
            session.Id, session.Title, session.CreatedAt, session.IsPinned,
            session.Messages.Select(m => new ChatMessageDto(m.Role, m.Content)).ToList());
    }

    public async Task<bool> DeleteSessionAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await _db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0;
    }
}
