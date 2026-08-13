using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Agent;
using MovieAgent.Hosting;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Configuration;
using MovieAgent.Agent.Tools;

namespace MovieAgent.EntryPoints;

/// <summary>
/// Confirms the two external dependencies respond. Deliberately does not put database rows
/// into the prompt — grounding the model with pre-fetched data is exactly the shortcut this
/// harness exists to avoid, and a smoke test that models the wrong thing gets copied.
/// </summary>
public sealed class ConnectivityCheckEntryPoint : IAppEntryPoint
{
    private readonly ISqlQueryExecutor _sql;
    private readonly IChatClient _chatClient;
    private readonly LlmOptions _llmOptions;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<ConnectivityCheckEntryPoint> _logger;

    public ConnectivityCheckEntryPoint(
        ISqlQueryExecutor sql,
        IChatClient chatClient,
        IOptions<LlmOptions> llmOptions,
        IOptions<AgentOptions> agentOptions,
        ILogger<ConnectivityCheckEntryPoint> logger)
    {
        _sql = sql;
        _chatClient = chatClient;
        _llmOptions = llmOptions.Value;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var failed = false;

        try
        {
            var result = await _sql.QueryAsync("select count(*) as film_count from film", null, cancellationToken);
            Console.WriteLine($"database  OK   film rows: {ToolOutputFormat.Rows(result, 1)}");
        }
        catch (Exception ex)
        {
            failed = true;
            _logger.LogError(ex, "Database check failed.");
            Console.WriteLine("database  FAIL");
        }

        try
        {
            var model = _llmOptions.Provider == LlmProvider.OpenAI
                ? _llmOptions.OpenAI.Model
                : _llmOptions.Ollama.Model;

            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Reply with the single word: ready")],
                new ChatOptions
                {
                    Reasoning = _agentOptions.ToReasoningOptions(),
                    MaxOutputTokens = _agentOptions.MaxOutputTokens,
                },
                cancellationToken);

            var thinking = _agentOptions.Thinking ? "on" : "off";
            Console.WriteLine($"model     OK   {_llmOptions.Provider}/{model} (thinking {thinking}) said: {response.Text.Trim()}");
        }
        catch (Exception ex)
        {
            failed = true;
            _logger.LogError(ex, "Model check failed.");
            Console.WriteLine("model     FAIL");
        }

        // Tool-calling support is not implied by the model responding at all, and a model that
        // silently ignores tools produces a run of zero tool calls that looks like a reasoning
        // failure rather than a capability gap.
        try
        {
            var probe = ToolSurfaces.Get("standard");
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Look up the film with film_id 6 using the tools provided.")],
                new ChatOptions
                {
                    Tools = ToolDeclarationFactory.CreateFor(probe),
                    ToolMode = ChatToolMode.Auto,
                    Reasoning = _agentOptions.ToReasoningOptions(),
                    MaxOutputTokens = _agentOptions.MaxOutputTokens,
                },
                cancellationToken);

            var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToArray();

            Console.WriteLine(calls.Length > 0
                ? $"tool call OK   model requested: {string.Join(", ", calls.Select(c => c.Name))}"
                : "tool call WARN the model returned no tool call. Check the model supports tools.");
        }
        catch (Exception ex)
        {
            failed = true;
            _logger.LogError(ex, "Tool-calling check failed.");
            Console.WriteLine("tool call FAIL");
        }

        return failed ? 1 : 0;
    }
}
