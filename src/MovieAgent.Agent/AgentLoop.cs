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

/// <summary>
/// The tool-use loop, driven by hand.
/// </summary>
/// <remarks>
/// Microsoft.Extensions.AI ships <c>UseFunctionInvocation()</c>, which would do all of this
/// automatically. It is deliberately not used: it hides the per-iteration boundary, and the
/// per-iteration boundary is the measurement. Everything the recorder needs — the raw
/// arguments the model sent, the exact text it got back, where the iteration cap bit —
/// only exists if the loop is explicit.
/// </remarks>
public sealed class AgentLoop(
    IChatClient chatClient,
    ToolExecutor toolExecutor,
    IOptions<AgentOptions> options,
    IWireCapture wireCapture,
    ILogger<AgentLoop> logger)
{
    public async Task<RunRecord> RunAsync(
        AgentRunRequest request,
        string provider,
        string model,
        CancellationToken cancellationToken = default)
    {
        var surface = ToolSurfaces.Get(request.ToolSurfaceName ?? options.Value.ToolSurface);
        var startedAt = DateTimeOffset.UtcNow;
        var runStopwatch = Stopwatch.StartNew();

        // Initialises the options used to configure the chat client
        // The tools it has available, temperature, reasoning, seed and max output
        var chatOptions = new ChatOptions
        {
            Tools = ToolDeclarationFactory.CreateFor(surface),
            ToolMode = ChatToolMode.Auto,
            Temperature = options.Value.Temperature,
            Reasoning = options.Value.ToReasoningOptions(),
            Seed = options.Value.Seed,
            MaxOutputTokens = options.Value.MaxOutputTokens,
        };

        // Agent starts the loop with the system prompt and the question
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, request.SystemPrompt),
            new(ChatRole.User, request.Question),
        };

        var iterations = new List<IterationRecord>();
        var outcome = RunOutcome.IterationCapReached;
        string? finalAnswer = null;
        string? error = null;

        // Tool name + exact arguments -> a short description of what that call returned.
        var previousCalls = new Dictionary<string, string>(StringComparer.Ordinal);

        wireCapture.Reset();
        var exchangesSeen = 0;
        var callSequence = 0;

        try
        {
            // Agent has a maximum number of iterations it can make to answer the question.
            for (var iteration = 1; iteration <= options.Value.MaxIterations; iteration++)
            {
                var turnStopwatch = Stopwatch.StartNew();
                var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
                turnStopwatch.Stop();

                // One turn may span several HTTP exchanges if the SDK retries; take the last.
                WireExchange? exchange = null;
                if (wireCapture.Enabled)
                {
                    var all = wireCapture.Exchanges;
                    if (all.Count > exchangesSeen)
                    {
                        exchange = all[^1];
                        exchangesSeen = all.Count;
                    }
                }

                // Check for reasoning text coming back if we've wanted it turned off
                // Some models don't honour the think=false flag
                var reasoningText = ExtractReasoningText(response);
                if (!options.Value.Thinking && !string.IsNullOrWhiteSpace(reasoningText))
                {
                    // Ollama's think=false is a request, not a guarantee — some models (qwen3
                    // among them) have been observed returning message.thinking anyway. This is
                    // the model's choice, not a harness bug, but it means Agent:Thinking=false is
                    // not a reliable claim that no reasoning was generated or paid for, so it is
                    // worth knowing about rather than silently trusting the flag.
                    logger.LogWarning(
                        "Question {QuestionId} iteration {Iteration}: Agent:Thinking is off but {Model} " +
                        "returned reasoning text anyway ({Length} chars) — it may be ignoring think=false.",
                        request.QuestionId,
                        iteration,
                        model,
                        reasoningText.Length);
                }

                var turnMessages = options.Value.NormaliseToolCallIds
                    ? NormaliseCallIds(response.Messages, ref callSequence)
                    : response.Messages;

                if (options.Value.ReplayThinking)
                {
                    turnMessages = ReplayReasoningIntoContent(turnMessages);
                }

                messages.AddRange(turnMessages);

                var calls = turnMessages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                if (calls.Count == 0)
                {
                    // No tool calls means the model considers itself finished.
                    finalAnswer = response.Text;
                    outcome = RunOutcome.Answered;
                    iterations.Add(BuildIteration(iteration, response, turnStopwatch, [], exchange, reasoningText));
                    break;
                }

                var callRecords = new List<ToolCallRecord>(calls.Count);
                var resultContents = new List<AIContent>(calls.Count);

                foreach (var call in calls)
                {
                    var argumentsRaw = SerialiseArguments(call.Arguments);
                    var signature = $"{call.Name}|{argumentsRaw}";
                    var isRepeat = previousCalls.TryGetValue(signature, out var previousOutcome);

                    ToolInvocationResult result;
                    var blocked = false;

                    if (isRepeat && options.Value.BlockRepeatedToolCalls)
                    {
                        blocked = true;
                        result = new ToolInvocationResult(
                            call.Name,
                            ToolOutputFormat.RepeatedCallError(call.Name, argumentsRaw, previousOutcome!),
                            IsError: true,
                            IsTerminal: false,
                            RowsReturned: 0,
                            ElapsedMilliseconds: 0);
                    }
                    else
                    {
                        result = await toolExecutor.ExecuteAsync(
                            surface,
                            new ToolInvocation(call.Name, call.Arguments?.AsReadOnly()),
                            cancellationToken);

                        previousCalls[signature] = DescribeOutcome(result);
                    }

                    logger.LogInformation(
                        "[{Iteration}] {Tool}({Args}) -> {Rows} row(s) in {Elapsed}ms{Status}",
                        iteration,
                        call.Name,
                        argumentsRaw,
                        result.RowsReturned,
                        result.ElapsedMilliseconds,
                        blocked ? " [repeat blocked]" : result.IsError ? " [error]" : string.Empty);

                    callRecords.Add(new ToolCallRecord
                    {
                        Iteration = iteration,
                        ToolName = call.Name,
                        CallId = call.CallId,
                        ArgumentsRaw = argumentsRaw,
                        ResultText = result.Output,
                        RowsReturned = result.RowsReturned,
                        IsError = result.IsError,
                        WasRepeat = isRepeat,
                        Blocked = blocked,
                        ElapsedMilliseconds = result.ElapsedMilliseconds,
                    });

                    resultContents.Add(new FunctionResultContent(call.CallId, result.Output));
                }

                iterations.Add(BuildIteration(iteration, response, turnStopwatch, callRecords, exchange, reasoningText));
                messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
            }

            if (outcome == RunOutcome.IterationCapReached)
            {
                logger.LogWarning(
                    "Question {QuestionId} hit the iteration cap of {Cap} after {Calls} tool call(s).",
                    request.QuestionId,
                    options.Value.MaxIterations,
                    iterations.Sum(i => i.ToolCalls.Count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            outcome = RunOutcome.Errored;
            error = ex.Message;
            logger.LogError(ex, "Run for question {QuestionId} failed.", request.QuestionId);
        }

        runStopwatch.Stop();

        return new RunRecord
        {
            RunId = Guid.NewGuid().ToString("n"),
            StartedAt = startedAt,
            QuestionId = request.QuestionId,
            Question = request.Question,
            ExpectedHops = request.ExpectedHops,
            ToolSurface = surface.Name,
            ToolNames = surface.ToolNames,
            Provider = provider,
            Model = model,
            Seed = options.Value.Seed,
            Temperature = options.Value.Temperature,
            MaxOutputTokens = options.Value.MaxOutputTokens,
            Thinking = options.Value.Thinking,
            ReplayThinking = options.Value.ReplayThinking,
            NormaliseToolCallIds = options.Value.NormaliseToolCallIds,
            RepairOllamaToolMessages = options.Value.RepairOllamaToolMessages,
            SendReasoningEffort = options.Value.SendReasoningEffort,
            Repeat = request.Repeat,
            SystemPrompt = request.SystemPrompt,
            SystemPromptSha256 = Agent.SystemPrompt.Sha256(request.SystemPrompt),
            OutputFormatVersion = ToolOutputFormat.Version,
            MaxIterations = options.Value.MaxIterations,
            Outcome = RunOutcomeClassifier.Effective(outcome, finalAnswer),
            CapHit = outcome == RunOutcome.IterationCapReached,
            FinalAnswer = finalAnswer,
            IterationCount = iterations.Count,
            ToolCallCount = iterations.Sum(i => i.ToolCalls.Count),
            TotalInputTokens = SumTokens(iterations, i => i.InputTokens),
            TotalOutputTokens = SumTokens(iterations, i => i.OutputTokens),
            ElapsedMilliseconds = runStopwatch.ElapsedMilliseconds,
            Error = error,
            Iterations = iterations,
        };
    }

    private static IterationRecord BuildIteration(
        int iteration,
        ChatResponse response,
        Stopwatch stopwatch,
        IReadOnlyList<ToolCallRecord> calls,
        WireExchange? exchange,
        string? reasoningText) => new()
        {
            Iteration = iteration,
            RequestSha256 = exchange?.RequestSha256,
            ResponseSha256 = exchange?.ResponseSha256,
            ContentSha256 = ContentHash(response),
            InputTokens = response.Usage?.InputTokenCount,
            OutputTokens = response.Usage?.OutputTokenCount,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            FinishReason = response.FinishReason?.ToString(),
            AssistantText = string.IsNullOrWhiteSpace(response.Text) ? null : response.Text,
            ReasoningText = reasoningText,
            ToolCalls = calls,
        };

    /// <summary>
    /// Concatenates every <see cref="TextReasoningContent"/> this turn produced. Normally at
    /// most one, but nothing enforces that, so this does not assume it.
    /// </summary>
    private static string? ExtractReasoningText(ChatResponse response)
    {
        var reasoning = string.Join(
            "\n",
            response.Messages
                .SelectMany(m => m.Contents)
                .OfType<TextReasoningContent>()
                .Select(c => c.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        return string.IsNullOrWhiteSpace(reasoning) ? null : reasoning;
    }

    /// <summary>
    /// Null when no iteration reported usage, so that "the provider did not tell us" is
    /// distinguishable from "it cost nothing".
    /// </summary>
    private static long? SumTokens(List<IterationRecord> iterations, Func<IterationRecord, long?> selector) =>
        iterations.Any(i => selector(i) is not null)
            ? iterations.Sum(i => selector(i) ?? 0)
            : null;

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
    /// Rewrites provider-generated tool-call identifiers to a deterministic sequence.
    /// </summary>
    /// <remarks>
    /// Both the assistant message and the tool result that answers it have to be rewritten, and
    /// with the same identifier, or the provider cannot pair them up. Everything else about the
    /// message is passed through untouched.
    /// </remarks>
    private static IList<ChatMessage> NormaliseCallIds(IList<ChatMessage> turnMessages, ref int callSequence)
    {
        var rewritten = new List<ChatMessage>(turnMessages.Count);

        foreach (var message in turnMessages)
        {
            if (!message.Contents.OfType<FunctionCallContent>().Any())
            {
                rewritten.Add(message);
                continue;
            }

            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent call)
                {
                    contents.Add(new FunctionCallContent($"call_{++callSequence}", call.Name, call.Arguments));
                }
                else
                {
                    contents.Add(content);
                }
            }

            rewritten.Add(new ChatMessage(message.Role, contents) { AuthorName = message.AuthorName });
        }

        return rewritten;
    }

    private const string ReplayedReasoningLabel = "[Your reasoning from this step, for reference on later steps]";

    /// <summary>
    /// Folds each message's <see cref="TextReasoningContent"/> into an ordinary
    /// <see cref="TextContent"/> item on the same message, so it survives being resent as plain
    /// history — see <see cref="AgentOptions.ReplayThinking"/> for why this is necessary and
    /// why it is the approximate fix rather than the faithful one.
    /// </summary>
    private static IList<ChatMessage> ReplayReasoningIntoContent(IList<ChatMessage> turnMessages)
    {
        var rewritten = new List<ChatMessage>(turnMessages.Count);

        foreach (var message in turnMessages)
        {
            var reasoning = string.Join(
                "\n",
                message.Contents.OfType<TextReasoningContent>()
                    .Select(c => c.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t)));

            if (string.IsNullOrWhiteSpace(reasoning))
            {
                rewritten.Add(message);
                continue;
            }

            // Placed first, ahead of the tool calls or reply text it led to, so a model reading
            // its own history sees the thought before the action — the order it happened in.
            var contents = new List<AIContent>(message.Contents.Count + 1)
            {
                new TextContent($"{ReplayedReasoningLabel}\n{reasoning}"),
            };
            contents.AddRange(message.Contents);

            rewritten.Add(new ChatMessage(message.Role, contents) { AuthorName = message.AuthorName });
        }

        return rewritten;
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
