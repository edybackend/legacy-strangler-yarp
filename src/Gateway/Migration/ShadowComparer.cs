using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Gateway.Migration;

public sealed record ShadowRequest(string RouteId, string PathAndQuery, int LegacyStatus, byte[] LegacyBody);

/// <summary>
/// Shadow comparisons run in the background, so the client never waits for the modern service and is never
/// affected by it. The queue is bounded: under load, comparisons are dropped, never requests.
/// </summary>
public sealed class ShadowBacklog
{
    private readonly Channel<ShadowRequest> _channel = Channel.CreateBounded<ShadowRequest>(
        new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<ShadowRequest> Reader => _channel.Reader;

    public void Enqueue(ShadowRequest request) => _channel.Writer.TryWrite(request);
}

/// <summary>Sends each shadowed request to the modern service and records whether the answers match.</summary>
public sealed partial class ShadowComparer(
    ShadowBacklog queue,
    IHttpClientFactory httpClientFactory,
    MigrationStats stats,
    TimeProvider clock,
    ILogger<ShadowComparer> logger) : BackgroundService
{
    public const string ModernClient = "shadow-modern";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await CompareAsync(request, stoppingToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                stats.ShadowFailed(request.RouteId);
                LogShadowFailed(request.RouteId, request.PathAndQuery, ex.Message);
            }
        }
    }

    private async Task CompareAsync(ShadowRequest request, CancellationToken ct)
    {
        using var response = await httpClientFactory.CreateClient(ModernClient).GetAsync(request.PathAndQuery.TrimStart('/'), ct);
        var modernBody = await response.Content.ReadAsByteArrayAsync(ct);

        var differences = new List<Difference>();
        if ((int)response.StatusCode != request.LegacyStatus)
        {
            differences.Add(new Difference("status", request.LegacyStatus.ToString(System.Globalization.CultureInfo.InvariantCulture), ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        else if (request.LegacyStatus is >= 200 and < 300)
        {
            differences.AddRange(JsonDiff.Compare(JsonNode.Parse(request.LegacyBody), JsonNode.Parse(modernBody)));
        }

        var mismatch = differences.Count == 0 ? null : new Mismatch(clock.GetUtcNow(), request.RouteId, request.PathAndQuery, differences);
        stats.ShadowCompared(mismatch, request.RouteId);
        if (mismatch is not null)
        {
            LogMismatch(request.RouteId, request.PathAndQuery, differences.Count, differences[0].Path, differences[0].Legacy, differences[0].Modern);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shadow mismatch on {RouteId} {Path}: {Count} difference(s), first at {Field}: legacy {Legacy} vs modern {Modern}")]
    private partial void LogMismatch(string routeId, string path, int count, string field, string? legacy, string? modern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shadow call failed on {RouteId} {Path}: {Reason}")]
    private partial void LogShadowFailed(string routeId, string path, string reason);
}
