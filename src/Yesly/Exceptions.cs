using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Yesly;

/// <summary>Base class of every exception this library throws.</summary>
public class YeslyException : Exception
{
    public YeslyException(string message, Exception? inner = null) : base(message, inner) { }

    /// <summary>HTTP status, when the API answered.</summary>
    public int? Status { get; private set; }

    /// <summary>Error class from the API, such as <c>invalid_request</c>.</summary>
    public string? Type { get; private set; }

    /// <summary>Machine-readable reason, such as <c>phone_invalid</c>.</summary>
    public string? Code { get; private set; }

    /// <summary>The parameter at fault, such as <c>customer.phone</c>.</summary>
    public string? Param { get; private set; }

    /// <summary><c>Request-Id</c> of the failed request; include it when you contact support.</summary>
    public string? RequestId { get; private set; }

    internal static YeslyException FromResponse(int status, string body, IReadOnlyDictionary<string, string> headers)
    {
        string? type = null, code = null, param = null, message = null, requestId = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                string? S(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                (type, code, param, message, requestId) = (S("type"), S("code"), S("param"), S("message"), S("request_id"));
            }
        }
        catch (JsonException)
        {
        }
        requestId ??= headers.TryGetValue("request-id", out var h) ? h : null;
        message ??= $"The Yesly API answered {status}.";
        YeslyException ex = status switch
        {
            401 => new AuthenticationException(message),
            403 => new PermissionDeniedException(message),
            404 => new NotFoundException(message),
            409 => new ConflictException(message),
            429 => new RateLimitException(message),
            >= 500 => new ApiException(message),
            _ => new InvalidRequestException(message),
        };
        return ex.With(status, type, code, param, requestId);
    }

    private YeslyException With(int status, string? type, string? code, string? param, string? requestId)
    {
        Status = status;
        Type = type;
        Code = code;
        Param = param;
        RequestId = requestId;
        return this;
    }
}

/// <summary>400 and other 4xx answers: something in the request needs fixing.</summary>
public class InvalidRequestException(string message) : YeslyException(message);

/// <summary>401: missing or revoked API key.</summary>
public class AuthenticationException(string message) : YeslyException(message);

/// <summary>403: the key cannot reach this resource, or the IP is not allowed.</summary>
public class PermissionDeniedException(string message) : YeslyException(message);

/// <summary>404: no such object in this account and mode.</summary>
public class NotFoundException(string message) : YeslyException(message);

/// <summary>409: such as an <c>external_id</c> already used with different data.</summary>
public class ConflictException(string message) : YeslyException(message);

/// <summary>429: too many requests. Retried automatically before this is thrown.</summary>
public class RateLimitException(string message) : YeslyException(message);

/// <summary>5xx: something went wrong on Yesly's side. Retried automatically before this is thrown.</summary>
public class ApiException(string message) : YeslyException(message);

/// <summary>The request did not get an answer: network failure or timeout.</summary>
public class ApiConnectionException(string message, Exception? inner = null) : YeslyException(message, inner);

/// <summary>A webhook delivery whose <c>Yesly-Signature</c> does not match, or is too old.</summary>
public class SignatureVerificationException(string message) : YeslyException(message);
