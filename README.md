# yesly-dotnet

The official .NET library for the Yesly API.

> Status: 0.1.0, not published yet. Covers API version `2026-10-01`. How to
> publish it: [docs/publishing.md](docs/publishing.md).

## Install

```bash
dotnet add package Yesly
```

.NET 8 or later. No dependencies. Works with trimming and Native AOT: JSON
uses source-generated `System.Text.Json` metadata.

## Usage

```csharp
using Yesly;

var yesly = new YeslyClient(Environment.GetEnvironmentVariable("YESLY_API_KEY"));

await yesly.Appointments.CreateAsync(new AppointmentCreateOptions
{
    ExternalId = "ORD-48213",
    Type = "delivery",
    Customer = new() { Name = "Martha", Phone = "+34612345678" },
    Slot = new() { Start = "2026-11-13T09:00:00+01:00", End = "2026-11-13T11:00:00+01:00" },
    Metadata = new() { ["order_id"] = "PV-18342", ["route_id"] = "BIO-07" },
});
```

`new YeslyClient()` reads `YESLY_API_KEY`. Every resource of the API reference
is a property: `Appointments`, `Imports`, `Slots`, `AppointmentTypes`,
`Locations`, `WebhookEndpoints`, `Events`, `Balance` and `Analytics`.
Parameters are `…Options` classes, and responses are model classes; unknown
fields are ignored. Every method takes an optional `RequestOptions` and
`CancellationToken`.

## Webhooks

```csharp
app.MapPost("/yesly/webhook", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        var e = Webhook.ConstructEvent(body, request.Headers["Yesly-Signature"], secret);
        if (e.Type == "appointment.confirmed") { var externalId = e.Data!.Appointment!.ExternalId; }
        return Results.Ok();
    }
    catch (SignatureVerificationException)
    {
        return Results.BadRequest();
    }
});
```

Pass the raw body. Deliveries more than five minutes old are rejected, and
either signature is accepted during a secret roll. `Webhook.GenerateTestHeader`
signs a payload to test your handler.

## Lists

`ListAsync` returns one `Page`; `ListAutoPagingAsync` walks every page:

```csharp
var page = await yesly.Events.ListAsync(new EventListOptions { Limit = 50 });
page.Data; page.HasMore; await page.NextPageAsync();

await foreach (var apt in yesly.Appointments.ListAutoPagingAsync(new AppointmentListOptions { Status = "no_reply" }))
    Console.WriteLine(apt.ExternalId);
```

## Errors

Failed requests throw a `YeslyException` subclass: `InvalidRequestException`
(400, 422), `AuthenticationException` (401), `PermissionDeniedException` (403),
`NotFoundException` (404), `ConflictException` (409), `RateLimitException`
(429), `ApiException` (5xx) or `ApiConnectionException` (no answer). Each has
`Status`, `Type`, `Code`, `Param` and `RequestId`.

## Retries, timeouts and idempotency

Network errors, timeouts, 408, 429 and 5xx are retried twice with backoff,
honouring `Retry-After`. POSTs that take an `Idempotency-Key` get one per call,
reused on every retry; pass your own with `RequestOptions`. Creating an
appointment is idempotent by `external_id` instead.

```csharp
var yesly = new YeslyClient(new YeslyClientOptions { ApiKey = key, Timeout = TimeSpan.FromSeconds(10), MaxRetries = 4 });
await yesly.Appointments.CancelAsync("apt_…", new AppointmentCancelOptions { Reason = "Shop closed" },
    new RequestOptions { IdempotencyKey = "cancel-ORD-48213" });
```

## Test mode

A `ysk_test_` key runs everything in test mode; `yesly.Mode` says which. Use
the test phone numbers from the API reference.

## Development

```bash
dotnet test
```

`src/Yesly/Generated/` is generated from the OpenAPI specification. Do not edit
it: change `yesly-openapi`, then, with both repos side by side,

```bash
(cd ../yesly-openapi && npm install)
node ../yesly-openapi/sdk/generate.mjs dotnet .
```

Smoke test against the real API with a test key:

```bash
YESLY_API_KEY=ysk_test_… YESLY_BASE_URL=https://api.dev.yesly.dev/v1 dotnet run scripts/smoke.cs
```

## Releasing

Bump `<Version>` in `src/Yesly/Yesly.csproj` and `YeslyVersion.Current`, then
push a tag `v<version>`. First-time registration on NuGet:
[docs/publishing.md](docs/publishing.md).
