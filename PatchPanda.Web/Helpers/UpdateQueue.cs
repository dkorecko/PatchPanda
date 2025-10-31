using System.Threading.Channels;

namespace PatchPanda.Web.Helpers;

public record PendingUpdateWork(int ContainerId, int TargetVersionId, string TargetVersionNumber);

public class UpdateQueue
{
    private readonly Channel<PendingUpdateWork> _channel;

    public UpdateQueue()
    {
        _channel = Channel.CreateUnbounded<PendingUpdateWork>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );
    }

    public ChannelReader<PendingUpdateWork> Reader => _channel.Reader;
    public ChannelWriter<PendingUpdateWork> Writer => _channel.Writer;

    public ValueTask EnqueueAsync(
        PendingUpdateWork work,
        CancellationToken cancellationToken = default
    )
    {
        return Writer.WriteAsync(work, cancellationToken);
    }
}
