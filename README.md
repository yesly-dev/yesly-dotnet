# yesly-dotnet

The official .NET library for the Yesly API.

> Status: 0.1.0, not published yet. Covers API version `2026-10-01`.

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

## Integration checklist

The usual integration is two calls: create an appointment as soon as it is
booked, and handle the customer's answer on a webhook. These are the points an
integration most often gets wrong; check them before going live. The whole API
reference is also in Markdown, for agents and other tools:
[llms-full.txt](https://yesly.dev/llms-full.txt) (index in
[llms.txt](https://yesly.dev/llms.txt), OpenAPI in
[openapi.yaml](https://yesly.dev/openapi.yaml)).

1. **Set up the appointment types first**
   (`yesly.AppointmentTypes.CreateAsync`, or in the dashboard). A type sets
   how the time reads (`time_format`: `window`, "between 9:00 and 11:00", or
   `exact`, "at 17:30"), when the first message goes out (`first_contact`) and
   how far the customer may move it (`reschedule`). With a `window` type,
   `slot.end` is required (`slot_end_required`). Send `type` with every
   appointment when the account has more than one type.
2. **Use your own id as `external_id`**: the order, booking or visit number.
   Creating is idempotent by it, so sending the same appointment again returns
   the existing one, and the same id with different data is a `409`
   `external_id_conflict`. To change the time or the customer, update the
   appointment (`yesly.Appointments.UpdateAsync`) instead of creating a new
   one.
3. **Send times with an offset** (`2026-11-13T09:00:00+01:00`) and phones in
   E.164. Yesly contacts Spanish numbers only, and no premium or special-rate
   ones (`phone_not_allowed`).
4. **Handle the answer on a webhook.** Verify the signature over the raw body,
   answer `2xx` within 10 seconds before doing slow work, and skip event ids
   you have already handled: a delivery can arrive more than once. The
   outcomes are `appointment.confirmed`, `appointment.cancelled`,
   `appointment.rescheduled`, `appointment.reschedule_requested` and
   `appointment.no_reply`; each carries your `external_id` and `metadata`.
5. **Build and test with a `ysk_test_` key.** The test phone numbers choose
   the outcome (`+34600000001` confirms on WhatsApp, `+34600000009` never
   answers; the full table is in the reference) and the chain runs in seconds.
   A real number with a test key is refused (`test_number_required`).
6. **Retry only what can succeed.** The library already retries network
   errors, `408`, `429` and `5xx`, with the same idempotency key. A `4xx`
   fails the same way again: fix the request using the error's `code` and
   `param`.
7. **Keep keys on the server.** A read-only key can list and retrieve but not
   write (`api_key_read_only`).

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
A delivery can arrive more than once, and a captured one stays valid for
those five minutes, so skip events whose `id` you have already handled.

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
YESLY_API_KEY=ysk_test_… dotnet run scripts/smoke.cs
```

## Releasing

Bump `<Version>` in `src/Yesly/Yesly.csproj` and `YeslyVersion.Current`, then
push a tag `v<version>`.
