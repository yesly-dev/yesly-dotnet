using System.Net;
using System.Text;

namespace Yesly.Tests;

/// <summary>Answers from a queue and records each request.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    public sealed record Call(string Method, string Url, Dictionary<string, string> Headers, string? Body);

    public List<Call> Calls { get; } = new();
    private readonly Queue<object> _answers = new();

    public FakeHandler Answer(int status, string body, Dictionary<string, string>? headers = null)
    {
        _answers.Enqueue((status, body, headers ?? new Dictionary<string, string>()));
        return this;
    }

    public FakeHandler Fail(Exception e)
    {
        _answers.Enqueue(e);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(h.Key == "User-Agent" ? " " : ",", h.Value));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add(new Call(request.Method.Method, request.RequestUri!.AbsoluteUri, headers, body));
        var a = _answers.Dequeue();
        if (a is Exception e) throw e;
        var (status, text, extra) = ((int, string, Dictionary<string, string>))a;
        var res = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        foreach (var (k, v) in extra) res.Headers.TryAddWithoutValidation(k, v);
        return res;
    }
}
