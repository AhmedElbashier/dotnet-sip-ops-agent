using SipOpsAgent.Llm;

namespace SipOpsAgent.Tests.TestSupport;

public sealed class QueueChatClient : IChatClient
{
    private readonly Queue<ChatMessage> _replies;

    public QueueChatClient(IEnumerable<ChatMessage> replies) => _replies = new Queue<ChatMessage>(replies);

    public string ProviderName => "queued";

    public string Model => "queued-test";

    public int Remaining => _replies.Count;

    public Task<ChatMessage> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (_replies.Count == 0)
        {
            throw new InvalidOperationException("The queued chat client has no remaining replies.");
        }

        LastRequest = request;
        return Task.FromResult(_replies.Dequeue());
    }

    public ChatRequest? LastRequest { get; private set; }
}

public sealed class RecordingHandler : HttpMessageHandler
{
    public RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        Responder = responder;
    }

    public Func<HttpRequestMessage, string, HttpResponseMessage> Responder { get; set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastBody { get; private set; }

    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Responder(request, LastBody ?? "");
    }
}
