using Xunit;

namespace Yesly.Tests;

public class WebhookTests
{
    private const string Secret = "whsec_test";
    private const long Now = 1794474252;
    private const string Body = """{"id":"evt_1","object":"event","type":"conversation.ended","data":{"conversation":{"id":"cnv_7Qp4Ns2Xk9Lm3D","outcome":{"code":"rescheduled","data":{"delivery_window":"2026-12-03T09:00/13:00"}}}}}""";

    [Fact]
    public void MatchesAnHmacComputedElsewhere()
    {
        // HMAC-SHA256 of "1794474252." + Body with "whsec_test", computed with Python's hmac module.
        Assert.Equal("t=1794474252,v1=f60bd9a59d63d6f2d01cbdcd9aec4781af68e4d1e915aaff424817ce69921f83", Webhook.GenerateTestHeader(Body, Secret, Now));
    }

    [Fact]
    public void ValidSignature()
    {
        var e = Webhook.ConstructEvent(Body, Webhook.GenerateTestHeader(Body, Secret, Now), Secret, now: Now);
        Assert.Equal("conversation.ended", e.Type);
        var outcome = e.Data!.Conversation!.Outcome!;
        Assert.Equal("rescheduled", outcome.Code);
        Assert.Equal("2026-12-03T09:00/13:00", outcome.Data!["delivery_window"].GetString());
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
