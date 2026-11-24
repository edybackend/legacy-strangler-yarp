using Yarp.ReverseProxy.Model;

namespace Gateway.Migration;

/// <summary>
/// Runs inside YARP's proxy pipeline, after the route matched and before the request is forwarded.
/// It narrows the cluster's destinations to the one the migration plan picks, tags the response with
/// <c>X-Served-By</c> and, in shadow mode, keeps a copy of the legacy answer for the comparison.
/// </summary>
public sealed class MigrationProxyStep(MigrationState state, MigrationStats stats, ShadowBacklog shadow)
{
    public const string ServedByHeader = "X-Served-By";
    public const string CanaryKeyHeader = "X-Canary-Key";

    public async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var feature = context.GetReverseProxyFeature();
        var routeId = feature.Route.Config.RouteId;
        var route = state.Get(routeId) ?? new RouteMigration(routeId, MigrationMode.Legacy, 0, ModernReady: false, ReadOnly: false);

        var target = DestinationSelector.Choose(route, context.Request.Headers[CanaryKeyHeader], () => Random.Shared.Next(100));
        feature.AvailableDestinations = [.. feature.AllDestinations.Where(d => d.DestinationId == target)];
        context.Response.Headers[ServedByHeader] = target;
        stats.Served(routeId, target);

        if (route.Mode != MigrationMode.Shadow || !HttpMethods.IsGet(context.Request.Method))
        {
            await next();
            return;
        }

        var client = context.Response.Body;
        using var copy = new MemoryStream();
        context.Response.Body = new TeeStream(client, copy);
        try
        {
            await next();
        }
        finally
        {
            context.Response.Body = client;
        }

        shadow.Enqueue(new ShadowRequest(routeId, context.Request.Path + context.Request.QueryString, context.Response.StatusCode, copy.ToArray()));
    }

    /// <summary>Writes to the client and to a copy at the same time.</summary>
    private sealed class TeeStream(Stream client, Stream copy) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            client.Write(buffer, offset, count);
            copy.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await client.WriteAsync(buffer, cancellationToken);
            await copy.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => client.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => client.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
