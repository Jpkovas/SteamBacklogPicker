using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace EpicDiscovery.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;

    public List<CapturedRequest> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        this.responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await ReadContentAsync(request, cancellationToken).ConfigureAwait(false);
        Requests.Add(new CapturedRequest(request, body));
        return await responder(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            return null;
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        request.Content = CloneContent(request.Content, body);
        return body;
    }

    private static HttpContent CloneContent(HttpContent original, string body)
    {
        var clone = new StringContent(body, Encoding.UTF8);
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    public sealed record CapturedRequest(HttpRequestMessage Message, string? Body);
}
