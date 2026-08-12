using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MovieAgent.Agent.Recording;

public interface IRunRecorder
{
    /// <summary>Appends one run as one line. Flushed before returning.</summary>
    Task RecordAsync(RunRecord record, CancellationToken cancellationToken = default);

    /// <summary>Absolute path of the file being written.</summary>
    string FilePath { get; }
}

/// <summary>
/// Appends runs to a JSONL file, one line each.
/// </summary>
/// <remarks>
/// Flushes on every write rather than buffering. A harness run can take hours and be killed
/// part way through; losing the completed runs to a buffer would be worse than the write cost.
/// </remarks>
public sealed class JsonlRunRecorder : IRunRecorder, IDisposable
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// UTF-8 with no BOM. <see cref="Encoding.UTF8"/> emits one, which lands at the start of
    /// the first line and makes the file fail to parse in every standard JSONL reader.
    /// </summary>
    private static readonly Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<JsonlRunRecorder> _logger;

    public JsonlRunRecorder(IOptions<RecorderOptions> options, ILogger<JsonlRunRecorder> logger)
    {
        _logger = logger;

        var configured = options.Value.FilePath;
        FilePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine("runs", $"runs-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl")
            : configured);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _logger.LogInformation("Recording runs to {FilePath}", FilePath);
    }

    public string FilePath { get; }

    public async Task RecordAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        var line = JsonSerializer.Serialize(record, _serializerOptions);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(FilePath, line + "\n", _utf8NoBom, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();
}

public sealed class RecorderOptions
{
    public const string SectionName = "Recorder";

    /// <summary>Blank means a timestamped file under ./runs.</summary>
    public string? FilePath { get; set; }
}
