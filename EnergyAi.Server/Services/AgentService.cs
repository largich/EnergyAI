using Anthropic.SDK;
using EnergyAi.Server.Dtos;
using EnergyAi.Server.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EnergyAi.Server.Services;

public interface IAgentService
{
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessageDto> messages,
        CancellationToken ct = default);
}

public class AgentService : IAgentService
{
    private const string SystemPrompt = """
        You are an energy consumption analyst assistant for an industrial facility.
        You have access to real-time sensor data for electricity, gas, water, and other
        utility consumption across multiple equipment units.

        When answering questions:
        - Always clarify the time period you used if the user did not specify one.
        - Present numeric values with their units (kWh, m³, etc.).
        - When detecting anomalies, explain what the anomaly means (spike, sustained high usage, potential leak).
        - For comparisons, highlight the most significant changes.
        - If a question is ambiguous, make a reasonable assumption and state it.

        Available date range in the system: data may span months to years; prefer the last 30 days
        when no range is specified.
        """;

    private readonly IChatClient            _chatClient;
    private readonly EnergyPlugin           _plugin;
    private readonly ILogger<AgentService>  _logger;
    private readonly string                 _modelId;

    public AgentService(IChatClient chatClient, EnergyPlugin plugin, ILogger<AgentService> logger, string modelId)
    {
        _chatClient = chatClient;
        _plugin     = plugin;
        _logger     = logger;
        _modelId    = modelId;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessageDto> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastUser = messages.LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        _logger.LogDebug("Agent stream started. Turn={Turn} UserMessage={Message}",
            messages.Count, lastUser?.Content?.Length > 120 ? lastUser.Content[..120] + "…" : lastUser?.Content);

        var history = BuildHistory(messages);

        var options = new ChatOptions
        {
            ModelId         = _modelId,
            MaxOutputTokens = 4096,
            Tools           = [.. _plugin.CreateTools()]
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(history, options, ct))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    private static List<ChatMessage> BuildHistory(IReadOnlyList<ChatMessageDto> messages)
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt)
        };

        foreach (var m in messages)
        {
            var role = m.Role.ToLowerInvariant() switch
            {
                "assistant" => ChatRole.Assistant,
                "system"    => ChatRole.System,
                _           => ChatRole.User
            };
            history.Add(new ChatMessage(role, m.Content));
        }

        return history;
    }
}
