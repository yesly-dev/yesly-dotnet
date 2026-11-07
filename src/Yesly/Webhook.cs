using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Yesly;

/// <summary>Webhook signature checks. They need the endpoint's signing secret (<c>whsec_…</c>), not an API key.</summary>
public static class Webhook
{
    /// <summary>Seconds a delivery's timestamp may differ from the local clock.</summary>
    public const long DefaultTolerance = 300;

    /// <summary>Verifies a delivery and returns its event. Pass the raw body, before any JSON parsing.</summary>
    public static Event ConstructEvent(string payload, string? header, string secret, long tolerance = DefaultTolerance, long? now = null)
    {
        VerifySignature(payload, header, secret, tolerance, now);
        try
        {
            return JsonSerializer.Deserialize(payload, YeslyClient.TypeInfo<Event>())
                ?? throw new SignatureVerificationException("The payload is not a JSON object.");
        }
        catch (JsonException)
        {
            throw new SignatureVerificationException("The payload is not a JSON object.");
        }
    }

    /// <summary>Checks <c>Yesly-Signature</c> against the raw body. A tolerance of 0 turns the clock check off.</summary>
    public static void VerifySignature(string payload, string? header, string secret, long tolerance = DefaultTolerance, long? now = null)
    {
        if (string.IsNullOrEmpty(header)) throw new SignatureVerificationException("The Yesly-Signature header is missing.");
        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t" && long.TryParse(kv[1], out var t)) timestamp = t;
            else if (kv[0] == "v1") signatures.Add(kv[1]);
        }
        if (timestamp is null || signatures.Count == 0) throw new SignatureVerificationException("The Yesly-Signature header is malformed.");
        var current = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (tolerance > 0 && Math.Abs(current - timestamp.Value) > tolerance)
            throw new SignatureVerificationException("The Yesly-Signature timestamp is outside the tolerance.");
        var expected = Encoding.ASCII.GetBytes(Hmac(secret, $"{timestamp}.{payload}"));
        foreach (var s in signatures)
        {
            if (CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(s))) return;
        }
        throw new SignatureVerificationException("No signature in Yesly-Signature matches the payload.");
    }

    /// <summary>Builds a <c>Yesly-Signature</c> value, to test your own webhook handler.</summary>
    public static string GenerateTestHeader(string payload, string secret, long? timestamp = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"t={t},v1={Hmac(secret, $"{t}.{payload}")}";
    }

    private static string Hmac(string secret, string message)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
}
