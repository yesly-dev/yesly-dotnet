using Xunit;

namespace Yesly.Tests;

public class WebhookTests
{
    private const string Secret = "whsec_test";
    private const long Now = 1794474252;
    private const string Body = """{"id":"evt_1","object":"event","type":"appointment.confirmed","data":{"appointment":{"id":"apt_1","external_id":"ORD-48213"}}}""";

    [Fact]
    public void MatchesAnHmacComputedElsewhere()
    {
        // HMAC-SHA256 of "1794474252." + Body with "whsec_test", computed with Python's hmac module.
        Assert.Equal("t=1794474252,v1=62b8ce65d20d98e1d1d8f14ff76317b0bf9b166a3dc5c2d980022e54c62e0a67", Webhook.GenerateTestHeader(Body, Secret, Now));
    }

    [Fact]
    public void ValidSignature()
    {
        var e = Webhook.ConstructEvent(Body, Webhook.GenerateTestHeader(Body, Secret, Now), Secret, now: Now);
        Assert.Equal("appointment.confirmed", e.Type);
        Assert.Equal("ORD-48213", e.Data!.Appointment!.ExternalId);
    }

    [Fact]
    public void EitherSignatureDuringARoll()
    {
        var old = Webhook.GenerateTestHeader(Body, "whsec_old", Now).Split(',')[1];
        var current = Webhook.GenerateTestHeader(Body, Secret, Now).Split(',')[1];
        Assert.Equal("evt_1", Webhook.ConstructEvent(Body, $"t={Now},{old},{current}", Secret, now: Now).Id);
    }

    [Fact]
    public void Rejections()
    {
        var h = Webhook.GenerateTestHeader(Body, Secret, Now);
        Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(Body + " ", h, Secret, now: Now));
        Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(Body, h, "whsec_other", now: Now));
        Assert.Contains("tolerance", Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(Body, h, Secret, now: Now + 301)).Message);
        Assert.NotNull(Webhook.ConstructEvent(Body, h, Secret, tolerance: 0, now: Now + 3600));
        Assert.Contains("malformed", Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(Body, "nonsense", Secret, now: Now)).Message);
        Assert.Contains("missing", Assert.Throws<SignatureVerificationException>(() => Webhook.ConstructEvent(Body, null, Secret, now: Now)).Message);
    }
}
