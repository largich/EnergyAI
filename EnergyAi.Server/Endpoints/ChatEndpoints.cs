using System.Text;
using System.Text.Json;
using EnergyAi.Server.Dtos;
using EnergyAi.Server.Services;

namespace EnergyAi.Server.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/chat").WithTags("Chat");

        // ---- Agent streaming ----

        // SSE stream — typed events:
        //   event: session   data: {"id":"<guid>"}      (only when a new session was created)
        //   event: text      data: "<chunk>"
        //   event: done      data: {}
        group.MapPost("/message", async (
            ChatRequest req,
            IAgentService agent,
            IChatHistoryService history,
            HttpResponse httpResponse,
            CancellationToken ct) =>
        {
            httpResponse.Headers.ContentType  = "text/event-stream";
            httpResponse.Headers.CacheControl = "no-cache";
            httpResponse.Headers.Connection   = "keep-alive";

            Guid? sessionId = req.SessionId;

            // Create session on first persisted message
            if (req.Persist && sessionId is null)
            {
                var userMsg = req.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "New chat";
                var session = await history.CreateSessionAsync(userMsg, ct);
                sessionId = session.Id;
                await WriteSseEventAsync(httpResponse, "session",
                    JsonSerializer.Serialize(new { id = sessionId }), ct);
            }

            // Stream agent response, collecting full text for optional persistence
            var fullResponse = new StringBuilder();
            await foreach (var chunk in agent.StreamAsync(req.Messages, ct))
            {
                fullResponse.Append(chunk);
                await WriteSseEventAsync(httpResponse, "text",
                    JsonSerializer.Serialize(chunk), ct);
            }

            // Persist messages after stream completes
            if (req.Persist && sessionId.HasValue)
            {
                var userContent = req.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
                await history.AppendMessagesAsync(sessionId.Value, userContent, fullResponse.ToString(), ct);
            }

            await WriteSseEventAsync(httpResponse, "done", "{}", ct);
        });

        // ---- Session management ----

        group.MapGet("/sessions", async (IChatHistoryService history, CancellationToken ct) =>
            Results.Ok(await history.GetSessionsAsync(ct)));

        group.MapGet("/sessions/{id:guid}", async (Guid id, IChatHistoryService history, CancellationToken ct) =>
        {
            var session = await history.GetSessionAsync(id, ct);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapDelete("/sessions/{id:guid}", async (Guid id, IChatHistoryService history, CancellationToken ct) =>
        {
            var deleted = await history.DeleteSessionAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return api;
    }

    private static async Task WriteSseEventAsync(
        HttpResponse response, string eventName, string data, CancellationToken ct)
    {
        await response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
