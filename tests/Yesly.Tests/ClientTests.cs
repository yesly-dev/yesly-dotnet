using System.Text.Json;
using Xunit;

namespace Yesly.Tests;

public class ClientTests
{
    private const string Cnv = """{"id":"cnv_7Qp4Ns2Xk9Lm3D","object":"conversation","status":"scheduled","cost":0.0,"context":{"order":"8841"},"unknown_field":1}""";

    private static YeslyClient Client(FakeHandler h)
        => new(new YeslyClientOptions { ApiKey = "ysk_test_123", BaseUrl = "https://api.test/v1", HttpClient = new HttpClient(h) }) { Delay = (_, _) => Task.CompletedTask };

    [Fact]
    public async Task CreatesAConversationAsInTheReadme()
    {
        var h = new FakeHandler().Answer(201, Cnv);
        var yesly = Client(h);

        var cnv = await yesly.Conversations.CreateAsync(new ConversationCreateOptions
        {
            Agent = "agt_8Hd2kQ4mZ1LpXy",
            Contact = YeslyJson.Of(new ContactInput { ExternalId = "customer-3391", Phone = "+34600000001", Name = "Marta" }),
            Channels = new() { "whatsapp", "call" },
            StartAt = "2026-12-01T10:00:00+01:00",
            Context = new() { ["order"] = YeslyJson.Of("8841"), ["delivery_window"] = YeslyJson.Of("2026-12-02T09:00/13:00") },
        });

        Assert.Equal("cnv_7Qp4Ns2Xk9Lm3D", cnv.Id);
        Assert.Equal("scheduled", cnv.Status);
        Assert.Equal("8841", cnv.Context!["order"].GetString());
        var c = h.Calls[0];
        Assert.Equal("POST", c.Method);
        Assert.Equal("https://api.test/v1/conversations", c.Url);
        Assert.Equal("Bearer ysk_test_123", c.Headers["Authorization"]);
        Assert.Equal(ApiVersion.Current, c.Headers["Yesly-Version"]);
        Assert.StartsWith($"yesly-dotnet/{YeslyVersion.Current} ", c.Headers["User-Agent"]);
        Assert.Matches("^[0-9a-f-]{36}$", c.Headers["Idempotency-Key"]);
        using var body = JsonDocument.Parse(c.Body!);
        var contact = body.RootElement.GetProperty("contact");
        Assert.Equal("Marta", contact.GetProperty("name").GetString());
        Assert.False(contact.TryGetProperty("email", out _));
        Assert.Equal("2026-12-02T09:00/13:00", body.RootElement.GetProperty("context").GetProperty("delivery_window").GetString());
        Assert.Equal("call", body.RootElement.GetProperty("channels")[1].GetString());
    }

    [Fact]
    public async Task TakesAContactId()
    {
        var h = new FakeHandler().Answer(201, Cnv);
        await Client(h).Conversations.CreateAsync(new ConversationCreateOptions { Agent = "agt_8Hd2kQ4mZ1LpXy", Contact = YeslyJson.Of("ctc_3Kf9Lq2Wm7Rt4B"), Channels = new() { "call" } });
        Assert.Equal("""{"agent":"agt_8Hd2kQ4mZ1LpXy","contact":"ctc_3Kf9Lq2Wm7Rt4B","channels":["call"]}""", h.Calls[0].Body);
    }

    [Fact]
    public async Task EncodesIdsAndQuery()
    {
        var h = new FakeHandler().Answer(200, Cnv).Answer(200, """{"object":"list","url":"/v1/conversations","has_more":false,"data":[]}""").Answer(200, """{"id":"mem_1","deleted":true}""");
        var y = Client(h);
        await y.Conversations.RetrieveAsync("cnv/1");
        await y.Conversations.ListAsync(new ConversationListOptions { Status = "no_reply", Agent = "agt_8Hd2kQ4mZ1LpXy", Limit = 5 });
        await y.Contacts.DeleteMemoryAsync("ctc_3Kf9Lq2Wm7Rt4B", "mem/1");
        Assert.Equal("https://api.test/v1/conversations/cnv%2F1", h.Calls[0].Url);
        Assert.Equal("https://api.test/v1/conversations?agent=agt_8Hd2kQ4mZ1LpXy&status=no_reply&limit=5", h.Calls[1].Url);
        Assert.Equal("DELETE", h.Calls[2].Method);
        Assert.Equal("https://api.test/v1/contacts/ctc_3Kf9Lq2Wm7Rt4B/memory/mem%2F1", h.Calls[2].Url);
    }

    [Fact]
    public async Task OneIdempotencyKeyAcrossRetries()
    {
        var h = new FakeHandler()
            .Answer(500, """{"error":{"type":"api_error","message":"boom"}}""")
            .Answer(503, "{}", new() { ["Retry-After"] = "0" })
            .Answer(200, """{"id":"ctc_1","object":"contact"}""");
        await Client(h).Contacts.CreateAsync(new ContactCreateOptions { ExternalId = "customer-3391", Phone = "+34600000001" });
        Assert.Equal(3, h.Calls.Count);
        var keys = h.Calls.Select(c => c.Headers["Idempotency-Key"]).Distinct().ToList();
        Assert.Single(keys);
        Assert.Matches("^[0-9a-f-]{36}$", keys[0]);
    }

    [Fact]
    public async Task UsesTheGivenIdempotencyKeyAndSendsAnEmptyObject()
    {
        var h = new FakeHandler().Answer(200, Cnv);
        await Client(h).Conversations.CancelAsync("cnv_7Qp4Ns2Xk9Lm3D", new RequestOptions { IdempotencyKey = "cancel-order-8841" });
        Assert.Equal("https://api.test/v1/conversations/cnv_7Qp4Ns2Xk9Lm3D/cancel", h.Calls[0].Url);
        Assert.Equal("cancel-order-8841", h.Calls[0].Headers["Idempotency-Key"]);
        Assert.Equal("{}", h.Calls[0].Body);
    }

    [Fact]
    public async Task RetriesThenGivesUpOnNetworkErrors()
    {
        var h = new FakeHandler().Answer(429, "{}", new() { ["Retry-After"] = "0" }).Fail(new HttpRequestException("down")).Fail(new HttpRequestException("down"));
        await Assert.ThrowsAsync<ApiConnectionException>(() => Client(h).Balance.RetrieveAsync());
        Assert.Equal(3, h.Calls.Count);
    }

    [Fact]
    public async Task MapsErrorsAndDoesNotRetry4xx()
    {
        var h = new FakeHandler().Answer(422, """{"error":{"type":"invalid_request","code":"context_invalid","param":"context.order","message":"Missing","request_id":"req_1"}}""");
        var e = await Assert.ThrowsAsync<InvalidRequestException>(() => Client(h).Conversations.CreateAsync(new ConversationCreateOptions { Agent = "agt_1", Contact = YeslyJson.Of("ctc_1"), Channels = new() { "call" } }));
        Assert.Equal((422, "context_invalid", "context.order", "req_1", "Missing"), (e.Status, e.Code, e.Param, e.RequestId, e.Message));
        Assert.Single(h.Calls);
    }

    [Fact]
    public async Task StatusClasses()
    {
        const string err = """{"error":{"type":"x","message":"m"}}""";
        var y = Client(new FakeHandler().Answer(401, err).Answer(404, err).Answer(409, err));
        await Assert.ThrowsAsync<AuthenticationException>(() => y.Events.RetrieveAsync("evt_1"));
        await Assert.ThrowsAsync<NotFoundException>(() => y.Events.RetrieveAsync("evt_1"));
        await Assert.ThrowsAsync<ConflictException>(() => y.Events.RetrieveAsync("evt_1"));
    }

    [Fact]
    public async Task AutoPagingWalksEveryPage()
    {
        var h = new FakeHandler()
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":true,"data":[{"id":"evt_3","type":"conversation.created","data":{"conversation":{"id":"cnv_1"}}},{"id":"evt_2"}]}""")
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":false,"data":[{"id":"evt_1"}]}""");
        var y = Client(h);
        var ids = new List<string>();
        await foreach (var e in y.Events.ListAutoPagingAsync(new EventListOptions { Limit = 2 })) ids.Add(e.Id!);
        Assert.Equal(new[] { "evt_3", "evt_2", "evt_1" }, ids);
        Assert.Equal("https://api.test/v1/events?limit=2&starting_after=evt_2", h.Calls[1].Url);
    }

    [Fact]
    public async Task FirstPageAndNextPage()
    {
        var h = new FakeHandler()
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":true,"data":[{"id":"evt_2","type":"conversation.ended","data":{"conversation":{"id":"cnv_1","outcome":{"code":"confirmed","data":{}}}}}]}""")
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":false,"data":[{"id":"evt_1"}]}""");
        var page = await Client(h).Events.ListAsync();
        Assert.Equal("confirmed", page.Data[0].Data!.Conversation!.Outcome!.Code);
        Assert.True(page.HasNextPage);
        var next = await page.NextPageAsync();
        Assert.False(next.HasNextPage);
    }

    [Fact]
    public async Task ReadsTurnsWithoutIdsInOnePage()
    {
        var h = new FakeHandler().Answer(200, """{"object":"list","url":"/v1/conversations/cnv_1/messages","has_more":false,"data":[{"object":"turn","role":"agent","channel":"whatsapp","message":"msg_1","text":"Hola Marta","at":"2026-12-01T10:00:01+01:00"},{"object":"turn","role":"contact","channel":"whatsapp","message":"msg_2","text":"Vale","at":"2026-12-01T10:03:41+01:00"}]}""");
        var texts = new List<string>();
        await foreach (var t in Client(h).Conversations.ListMessagesAutoPagingAsync("cnv_1")) texts.Add(t.Text!);
        Assert.Equal(new[] { "Hola Marta", "Vale" }, texts);
        Assert.Single(h.Calls);
    }

    [Fact]
    public async Task NeedsAKey()
    {
        Assert.Equal("live", new YeslyClient("ysk_live_1").Mode);
        if (Environment.GetEnvironmentVariable("YESLY_API_KEY") is null)
        {
            var e = await Assert.ThrowsAsync<YeslyException>(() => new YeslyClient().Balance.RetrieveAsync());
            Assert.Contains("No API key", e.Message);
        }
    }

    [Fact]
    public void BaseUrlMustBeHttpsExceptForLocalhost()
    {
        foreach (var url in new[] { "http://localhost:4010", "http://127.0.0.1:4010/v1", "http://[::1]:4010" })
            Assert.Equal(url, new YeslyClient(new YeslyClientOptions { ApiKey = "ysk_test_1", BaseUrl = url }).BaseUrl);
        var e = Assert.Throws<YeslyException>(() => new YeslyClient(new YeslyClientOptions { ApiKey = "ysk_test_1", BaseUrl = "http://api.yesly.dev/v1" }));
        Assert.Contains("must use https", e.Message);
    }
}
