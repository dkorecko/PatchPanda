using System.Threading.Channels;

namespace PatchPanda.Web.Helpers;

public class JobQueue
{
    private readonly Channel<AbstractJob> _channel;

    public JobQueue()
    {
        _channel = Channel.CreateUnbounded<AbstractJob>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );
    }

    public ChannelReader<AbstractJob> Reader => _channel.Reader;
    public ChannelWriter<AbstractJob> Writer => _channel.Writer;

    public ValueTask EnqueueAsync(AbstractJob work, CancellationToken cancellationToken = default)
    {
        return Writer.WriteAsync(work, cancellationToken);
    }
}
