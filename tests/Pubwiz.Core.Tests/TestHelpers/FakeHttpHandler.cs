using System.Net;

namespace Pubwiz.Core.Tests.TestHelpers;

/// <summary>
/// An HTTP handler that answers from a queue of canned responses and records the requests.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpMethod Method, Uri Url, string? Authorization, long? BodyLength)> Requests { get; } = [];

    public FakeHttpHandler Respond(HttpStatusCode status, string? json = null, Uri? location = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            response.Headers.Location = location;
            return response;
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        long? bodyLength = null;
        if (request.Content is not null)
        {
            bodyLength = (await request.Content.ReadAsByteArrayAsync(cancellationToken)).Length;
        }

        Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), bodyLength));
        return _responses.Dequeue()(request);
    }
}
