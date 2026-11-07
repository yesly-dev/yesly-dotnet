using System.Text.Json;
using Xunit;

namespace Yesly.Tests;

public class ClientTests
{
    private const string Apt = """{"id":"apt_1","object":"appointment","external_id":"ORD-48213","status":"scheduled","cost":0.0,"unknown_field":1}""";

    private static YeslyClient Client(FakeHandler h)
        => new(new YeslyClientOptions { ApiKey = "ysk_test_123", BaseUrl = "https://api.test/v1", HttpClient = new HttpClient(h) }) { Delay = (_, _) => Task.CompletedTask };

    [Fact]
    public async Task CreatesAnAppointmentAsOnTheWebsite()
    {
        var h = new FakeHandler().Answer(201, Apt);
        var yesly = Client(h);

        var apt = await yesly.Appointments.CreateAsync(new AppointmentCreateOptions
        {
            ExternalId = "ORD-48213",
            Type = "delivery",
            Customer = new() { Name = "Martha", Phone = "+34612345678" },
            Slot = new() { Start = "2026-11-13T09:00:00+01:00", End = "2026-11-13T11:00:00+01:00" },
            Metadata = new() { ["order_id"] = "PV-18342", ["route_id"] = "BIO-07" },
        });

        Assert.Equal("apt_1", apt.Id);
        Assert.Equal("scheduled", apt.Status);
        var c = h.Calls[0];
        Assert.Equal("POST", c.Method);
        Assert.Equal("https://api.test/v1/appointments", c.Url);
        Assert.Equal("Bearer ysk_test_123", c.Headers["Authorization"]);
        Assert.Equal(ApiVersion.Current, c.Headers["Yesly-Version"]);
        Assert.StartsWith($"yesly-dotnet/{YeslyVersion.Current} ", c.Headers["User-Agent"]);
        Assert.False(c.Headers.ContainsKey("Idempotency-Key"));
        using var body = JsonDocument.Parse(c.Body!);
        Assert.Equal("Martha", body.RootElement.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal("BIO-07", body.RootElement.GetProperty("metadata").GetProperty("route_id").GetString());
        Assert.False(body.RootElement.TryGetProperty("location", out _));
    }

    [Fact]
    public async Task EncodesIdsAndQuery()
    {
        var h = new FakeHandler().Answer(200, Apt).Answer(200, """{"object":"list","url":"/v1/appointments","has_more":false,"data":[]}""").Answer(200, """{"object":"list","data":[]}""");
        var y = Client(h);
        await y.Appointments.RetrieveAsync("apt/1");
        await y.Appointments.ListAsync(new AppointmentListOptions { Status = "no_reply", Metadata = new() { ["route_id"] = "BIO-07" }, Limit = 5 });
        await y.Slots.ListAsync(new SlotListOptions { Available = true });
        Assert.Equal("https://api.test/v1/appointments/apt%2F1", h.Calls[0].Url);
        Assert.Equal("https://api.test/v1/appointments?status=no_reply&metadata%5Broute_id%5D=BIO-07&limit=5", h.Calls[1].Url);
        Assert.Equal("https://api.test/v1/slots?available=true", h.Calls[2].Url);
    }

    [Fact]
    public async Task OneIdempotencyKeyAcrossRetries()
    {
        var h = new FakeHandler()
            .Answer(500, """{"error":{"type":"api_error","message":"boom"}}""")
            .Answer(503, "{}", new() { ["Retry-After"] = "0" })
            .Answer(200, """{"object":"list","data":[]}""");
        await Client(h).Slots.CreateAsync(new SlotCreateOptions { Slots = new() { new SlotCreate { Start = "2026-11-13T09:00:00+01:00", End = "2026-11-13T11:00:00+01:00", Type = "delivery" } } });
        Assert.Equal(3, h.Calls.Count);
        var keys = h.Calls.Select(c => c.Headers["Idempotency-Key"]).Distinct().ToList();
        Assert.Single(keys);
        Assert.Matches("^[0-9a-f-]{36}$", keys[0]);
    }

    [Fact]
    public async Task UsesTheGivenIdempotencyKeyAndSendsAnEmptyObject()
    {
        var h = new FakeHandler().Answer(200, Apt).Answer(200, Apt);
        var y = Client(h);
        await y.Appointments.CancelAsync("apt_1", new AppointmentCancelOptions { Reason = "Shop closed" }, new RequestOptions { IdempotencyKey = "cancel-apt_1" });
        await y.Appointments.CancelAsync("apt_2");
        Assert.Equal("cancel-apt_1", h.Calls[0].Headers["Idempotency-Key"]);
        Assert.Equal("""{"reason":"Shop closed"}""", h.Calls[0].Body);
        Assert.Equal("{}", h.Calls[1].Body);
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
        var h = new FakeHandler().Answer(422, """{"error":{"type":"invalid_request","code":"phone_invalid","param":"customer.phone","message":"Not E.164","request_id":"req_1"}}""");
        var e = await Assert.ThrowsAsync<InvalidRequestException>(() => Client(h).Appointments.RetrieveAsync("apt_1"));
        Assert.Equal((422, "phone_invalid", "customer.phone", "req_1", "Not E.164"), (e.Status, e.Code, e.Param, e.RequestId, e.Message));
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
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":true,"data":[{"id":"evt_3","type":"appointment.created","data":{"appointment":{"id":"apt_1"}}},{"id":"evt_2"}]}""")
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
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":true,"data":[{"id":"evt_2","type":"appointment.created","data":{"appointment":{"id":"apt_1"}}}]}""")
            .Answer(200, """{"object":"list","url":"/v1/events","has_more":false,"data":[{"id":"evt_1"}]}""");
        var page = await Client(h).Events.ListAsync();
        Assert.Equal("apt_1", page.Data[0].Data!.Appointment!.Id);
        Assert.True(page.HasNextPage);
        var next = await page.NextPageAsync();
        Assert.False(next.HasNextPage);
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
}
