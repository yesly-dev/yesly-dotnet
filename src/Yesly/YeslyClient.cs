using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Yesly;

/// <summary>
/// Client for the Yesly API. The key defaults to the <c>YESLY_API_KEY</c> environment variable.
/// The resources (<see cref="Appointments"/>, <see cref="Slots"/>, …) are generated from the OpenAPI specification.
/// </summary>
public sealed partial class YeslyClient
{
    /// <summary>Source-generated metadata for every API type, so the library works with trimming and Native AOT.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = YeslyJsonContext.Default.Options;

    internal static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)JsonOptions.GetTypeInfo(typeof(T));

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly string? _apiKey;
    private readonly HttpClient _http;

    /// <summary>Waits before a retry; replaced in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public YeslyClient(string? apiKey = null) : this(new YeslyClientOptions { ApiKey = apiKey }) { }

    public YeslyClient(YeslyClientOptions options)
    {
        _apiKey = options.ApiKey ?? Env("YESLY_API_KEY");
        BaseUrl = (options.BaseUrl ?? Env("YESLY_BASE_URL") ?? Yesly.ApiVersion.BaseUrl).TrimEnd('/');
        ApiVersion = options.ApiVersion ?? Yesly.ApiVersion.Current;
        Timeout = options.Timeout;
        MaxRetries = options.MaxRetries;
        _http = options.HttpClient ?? SharedHttp;
        InitResources();
    }

    public string BaseUrl { get; }

    public string ApiVersion { get; }

    public TimeSpan Timeout { get; }

    public int MaxRetries { get; }

    /// <summary><c>live</c> or <c>test</c>, from the key's prefix.</summary>
    public string? Mode => _apiKey switch
    {
        not null when _apiKey.StartsWith("ysk_live_", StringComparison.Ordinal) => "live",
        not null when _apiKey.StartsWith("ysk_test_", StringComparison.Ordinal) => "test",
        _ => null,
    };

    internal async Task<T> RequestAsync<T>(string method, string path, object? query, object? body, bool idempotent, RequestOptions? options, CancellationToken cancellationToken)
    {
        var json = await SendAsync(method, path, Flatten(query), body, idempotent, options, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, TypeInfo<T>())!;
    }

    internal Task<Page<T>> ListAsync<T>(string method, string path, object? query, RequestOptions? options, CancellationToken cancellationToken)
        => ListAsync<T>(method, path, Flatten(query), null, options, cancellationToken);

    internal async IAsyncEnumerable<T> ListAutoPagingAsync<T>(string method, string path, object? query, RequestOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = await ListAsync<T>(method, path, query, options, cancellationToken).ConfigureAwait(false);
        await foreach (var item in page.AutoPagingAsync(cancellationToken).ConfigureAwait(false)) yield return item;
    }

    private async Task<Page<T>> ListAsync<T>(string method, string path, List<KeyValuePair<string, string>> query, string? startingAfter, RequestOptions? options, CancellationToken cancellationToken)
    {
        var q = startingAfter is null ? query : query.Append(new("starting_after", startingAfter)).ToList();
        var json = await SendAsync(method, path, q, null, false, options, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var data = new List<T>();
        string? lastId = null;
        if (root.TryGetProperty("data", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                data.Add(item.Deserialize(TypeInfo<T>())!);
                lastId = item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            }
        }
        var url = root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : "";
        var hasMore = root.TryGetProperty("has_more", out var h) && h.ValueKind == JsonValueKind.True;
        return new Page<T>(url, hasMore, data, lastId, (after, ct) => ListAsync<T>(method, path, query, after, options, ct));
    }

    private async Task<string> SendAsync(string method, string path, List<KeyValuePair<string, string>> query, object? body, bool idempotent, RequestOptions? options, CancellationToken cancellationToken)
    {
        if (_apiKey is null) throw new YeslyException("No API key. Pass one to new YeslyClient(…) or set YESLY_API_KEY.");
        var url = BaseUrl + path + QueryString(query);
        string? payload = body is not null ? JsonSerializer.Serialize(body, JsonOptions.GetTypeInfo(body.GetType())) : method == "POST" ? "{}" : null;
        var idempotencyKey = idempotent ? options?.IdempotencyKey ?? Guid.NewGuid().ToString() : null;
        var timeout = options?.Timeout ?? Timeout;
        var maxRetries = options?.MaxRetries ?? MaxRetries;

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Yesly-Version", ApiVersion);
            request.Headers.TryAddWithoutValidation("User-Agent", $"yesly-dotnet/{YeslyVersion.Current} dotnet/{Environment.Version}");
            if (idempotencyKey is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (attempt < maxRetries)
                {
                    await Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw new ApiConnectionException(e is OperationCanceledException ? $"The request timed out after {timeout.TotalSeconds} s." : "Could not reach the Yesly API.", e);
            }
            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status is >= 200 and < 300) return text.Length == 0 ? "null" : text;
                var retryable = status == 408 || status == 429 || status >= 500;
                if (retryable && attempt < maxRetries)
                {
                    await Delay(RetryAfter(response) ?? Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                var headers = response.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => string.Join(",", x.Value));
                throw YeslyException.FromResponse(status, text, headers);
            }
        }
    }

    /// <summary>Options object to <c>a=1&amp;metadata[k]=v</c> pairs; nulls are left out.</summary>
    internal static List<KeyValuePair<string, string>> Flatten(object? query)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (query is null) return pairs;
        var root = JsonSerializer.SerializeToElement(query, JsonOptions.GetTypeInfo(query.GetType()));
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.Null:
                    break;
                case JsonValueKind.Object:
                    foreach (var s in p.Value.EnumerateObject())
                        if (s.Value.ValueKind != JsonValueKind.Null) pairs.Add(new($"{p.Name}[{s.Name}]", Scalar(s.Value)));
                    break;
                case JsonValueKind.Array:
                    foreach (var x in p.Value.EnumerateArray()) pairs.Add(new(p.Name, Scalar(x)));
                    break;
                default:
                    pairs.Add(new(p.Name, Scalar(p.Value)));
                    break;
            }
        }
        return pairs;
    }

    private static string Scalar(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => v.GetRawText(),
    };

    private static string QueryString(List<KeyValuePair<string, string>> pairs)
        => pairs.Count == 0 ? "" : "?" + string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values) && double.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s is >= 0 and <= 60)
            return TimeSpan.FromSeconds(s);
        return null;
    }

    private static TimeSpan Backoff(int attempt)
    {
        var b = Math.Min(0.5 * Math.Pow(2, attempt), 8.0);
        return TimeSpan.FromSeconds(b / 2 + Random.Shared.NextDouble() * b / 2);
    }

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? null : v;
    }
}
