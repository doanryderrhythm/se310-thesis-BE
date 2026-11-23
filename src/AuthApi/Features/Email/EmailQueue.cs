using System.Threading.Channels;

namespace AuthApi.Features.Email;

internal sealed record EmailMessage(string To, string Subject, string HtmlBody);

/// <summary>
/// In-memory outbox so request handlers never wait on SMTP. Messages are lost if the
/// process stops before they are sent; that is acceptable for promotional email.
/// </summary>
internal sealed class EmailQueue
{
    private readonly Channel<EmailMessage> channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite });

    public bool Enqueue(EmailMessage message) => channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
