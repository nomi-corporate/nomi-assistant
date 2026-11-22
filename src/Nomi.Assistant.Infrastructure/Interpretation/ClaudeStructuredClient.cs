using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;
using Nomi.Assistant.Application;

namespace Nomi.Assistant.Infrastructure.Interpretation;

/// <summary>
/// One Anthropic API client for the Assistant's interpreters: a single request with structured
/// outputs (JSON Schema) and low effort. Returns the JSON text, or null when the model refused.
/// </summary>
public sealed class ClaudeStructuredClient
{
    public const string DefaultModelId = "claude-haiku-5-5";

    private readonly string _modelId;
    private readonly Lazy<AnthropicClient> _client;

    public ClaudeStructuredClient(IConfiguration configuration)
    {
        _modelId = configuration["Interpretation:ModelId"] is { Length: > 0 } model ? model : DefaultModelId;
        var apiKey = configuration["Anthropic:ApiKey"];
        IsConfigured = !string.IsNullOrWhiteSpace(apiKey)
            && !string.Equals(configuration["Interpretation:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        _client = new Lazy<AnthropicClient>(() => new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(20), MaxRetries = 2 });
    }

    public bool IsConfigured { get; }

    /// <exception cref="InterpreterUnavailableException">Not configured, unreachable, failed, or truncated.</exception>
    public async Task<string?> CompleteJsonAsync(string system, string user, Dictionary<string, JsonElement> schema, CancellationToken ct)
    {
        if (!IsConfigured) throw new InterpreterUnavailableException("The Anthropic API key is not configured.");
        Message response;
        try
        {
            response = await _client.Value.Messages.Create(new MessageCreateParams
            {
                Model = _modelId,
                MaxTokens = 2048,
                System = system,
                Messages = [new UserMessageParam { Content = user }],
                OutputConfig = new OutputConfig { Effort = Effort.Low, Format = new JsonOutputFormat { Schema = schema } },
            }, cancellationToken: ct);
        }
        catch (AnthropicApiException error) { throw new InterpreterUnavailableException("Anthropic API rejected or failed the request.", error); }
        catch (HttpRequestException error) { throw new InterpreterUnavailableException("Anthropic API could not be reached.", error); }
        catch (TaskCanceledException error) when (!ct.IsCancellationRequested) { throw new InterpreterUnavailableException("Anthropic API timed out.", error); }

        if (response.StopReason == StopReason.Refusal) return null;
        if (response.StopReason == StopReason.MaxTokens) throw new InterpreterUnavailableException("The model output was truncated.");
        return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
    }

    public static Dictionary<string, JsonElement> Schema(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? throw new InvalidOperationException("Invalid schema.");
}
