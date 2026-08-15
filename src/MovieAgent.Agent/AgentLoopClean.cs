using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieAgent.Agent.Recording;
using MovieAgent.Agent.Abstractions;
using MovieAgent.Agent.Tools;

namespace MovieAgent.Agent;

// Copy of AgentLoop but truncated and simplified for the blog content
// This code is never called
public sealed class AgentLoopClean(
    IChatClient chatClient,
    ToolExecutor toolExecutor,
    IOptions<AgentOptions> options,
    ILogger<AgentLoop> logger)
{
    public async Task<string?> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var surface = ToolSurfaces.Get("standard+desc");

        var chatOptions = new ChatOptions
        {
            Tools = ToolDeclarationFactory.CreateFor(surface),

            // On each turn, the model may call tools or return its final answer.
            ToolMode = ChatToolMode.Auto,

            Temperature = settings.Temperature,
            Reasoning = settings.ToReasoningOptions(),
            Seed = settings.Seed,
            MaxOutputTokens = settings.MaxOutputTokens,
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, request.SystemPrompt),
            new(ChatRole.User, request.Question),
        };

        // Used to prevent a model repeatedly making the identical call.
        var previousCalls =
            new Dictionary<string, string>(StringComparer.Ordinal);

        for (var iteration = 1;
            iteration <= settings.MaxIterations;
            iteration++)
        {
            var response = await chatClient.GetResponseAsync(
                messages,
                chatOptions,
                cancellationToken);

            // Preserve the assistant message containing any tool calls.
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            // No tool calls means the model considers the task complete.
            if (calls.Count == 0)
            {
                return response.Text;
            }

            var toolResults = new List<AIContent>(calls.Count);

            foreach (var call in calls)
            {
                var arguments = SerialiseArguments(call.Arguments);
                var signature = $"{call.Name}|{arguments}";

                ToolInvocationResult result;

                if (settings.BlockRepeatedToolCalls &&
                    previousCalls.TryGetValue(signature, out var previousOutcome))
                {
                    result = new ToolInvocationResult(
                        call.Name,
                        ToolOutputFormat.RepeatedCallError(
                            call.Name,
                            arguments,
                            previousOutcome),
                        IsError: true,
                        IsTerminal: false,
                        RowsReturned: 0,
                        ElapsedMilliseconds: 0);
                }
                else
                {
                    result = await toolExecutor.ExecuteAsync(
                        surface,
                        new ToolInvocation(
                            call.Name,
                            call.Arguments?.AsReadOnly()),
                        cancellationToken);

                    previousCalls[signature] = DescribeOutcome(result);
                }

                // CallId associates each result with the model's original call.
                toolResults.Add(
                    new FunctionResultContent(call.CallId, result.Output));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, toolResults));
        }

        logger.LogWarning(
            "Question {QuestionId} hit the iteration cap of {Cap}.",
            request.QuestionId,
            settings.MaxIterations);

        return null;
    }

    private static IterationRecord BuildIteration(
        int iteration,
        ChatResponse response) => new()
        {
            Iteration = iteration,
            RequestSha256 = null,
            ResponseSha256 = null,
            ContentSha256 = ContentHash(response),
            InputTokens = response.Usage?.InputTokenCount,
            OutputTokens = response.Usage?.OutputTokenCount,
            ElapsedMilliseconds = 0,
            FinishReason = response.FinishReason?.ToString(),
            AssistantText = string.IsNullOrWhiteSpace(response.Text) ? null : response.Text,
            ReasoningText = null,
            ToolCalls = [],
        };

    /// <summary>
    /// Hashes what the model generated, ignoring identifiers and timings.
    /// </summary>
    private static string ContentHash(ChatResponse response)
    {
        var calls = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(c => $"{c.Name}({SerialiseArguments(c.Arguments)})");

        var canonical = string.Join("\n", [response.Text ?? string.Empty, .. calls]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// A one-line description of what a call returned, quoted back to the model if it repeats
    /// the call. Short on purpose: it is prompt text, and the full result is already upthread.
    /// </summary>
    private static string DescribeOutcome(ToolInvocationResult result) => result switch
    {
        { IsError: true } => "an error",
        { RowsReturned: 0 } => ToolOutputFormat.NoRowsMarker,
        { RowsReturned: 1 } => "1 row",
        var r => $"{r.RowsReturned} rows",
    };

    private static string SerialiseArguments(IDictionary<string, object?>? arguments) =>
        arguments is null or { Count: 0 }
            ? "{}"
            : JsonSerializer.Serialize(arguments);
}
