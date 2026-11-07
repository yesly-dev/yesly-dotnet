using System;
using System.Net.Http;

namespace Yesly;

/// <summary>How a client connects. Every value has a default.</summary>
public sealed class YeslyClientOptions
{
    /// <summary>Defaults to the <c>YESLY_API_KEY</c> environment variable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Defaults to <c>https://api.yesly.dev/v1</c>, or <c>YESLY_BASE_URL</c> when set. Must be HTTPS, except for localhost.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Yesly-Version</c>. Defaults to the version this library was generated for.</summary>
    public string? ApiVersion { get; set; }

    /// <summary>Per attempt. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Retries after a network error, timeout, 408, 429 or 5xx. Default 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Your own <see cref="System.Net.Http.HttpClient"/>, for proxies or tests.</summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>Options for one request.</summary>
public sealed class RequestOptions
{
    /// <summary>For POSTs that take one. Generated once per call when not given, and reused on retries.</summary>
    public string? IdempotencyKey { get; set; }

    public TimeSpan? Timeout { get; set; }

    public int? MaxRetries { get; set; }
}
