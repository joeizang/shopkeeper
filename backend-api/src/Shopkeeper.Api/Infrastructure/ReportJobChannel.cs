using System.Threading.Channels;

namespace Shopkeeper.Api.Infrastructure;

/// <summary>
/// Singleton channel used to signal the background ReportJobWorker when a new
/// report job is queued. Eliminates constant 5-second polling when idle.
/// </summary>
public sealed class ReportJobChannel
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    public ChannelWriter<Guid> Writer => _channel.Writer;
    public ChannelReader<Guid> Reader => _channel.Reader;
}
