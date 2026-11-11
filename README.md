# yesly-dotnet

The official .NET library for the Yesly API.

> Status: 0.2.0, not published yet. Covers API version `2026-11-11`.

## Install

```bash
dotnet add package Yesly
```

.NET 8 or later. No dependencies. Works with trimming and Native AOT: JSON
uses source-generated `System.Text.Json` metadata.

## Usage

Yesly runs agents built for your company. The usual integration is two calls:
ask an agent to contact someone, and receive the outcome on a webhook.

```csharp
using Yesly;

var yesly = new YeslyClient(Environment.GetEnvironmentVariable("YESLY_API_KEY"));

// Create the contact, or update it by your own id.
var contact = await yesly.Contacts.CreateAsync(new ContactCreateOptions
{
    ExternalId = "customer-3391",
    Name = "Marta",
    Phone = "+34600000001",
});

// Ask the delivery agent to contact her: WhatsApp first, then a call.
var conversation = await yesly.Conversations.CreateAsync(new ConversationCreateOptions
{
    Agent = "agt_8Hd2kQ4mZ1LpXy",
    Contact = YeslyJson.Of(contact.Id!),
    Channels = new() { "whatsapp", "call" },
    StartAt = "2026-12-01T10:00:00+01:00",
    Context = new()
    {
        ["order"] = YeslyJson.Of("8841"),
        ["delivery_window"] = YeslyJson.Of("2026-12-02T09:00/13:00"),
    },
}, new RequestOptions { IdempotencyKey = "order-8841-delivery" });
// conversation.Status == "scheduled"
```

`Contact` and the `Context` values are free-form JSON (`JsonElement`);
`YeslyJson.Of` builds them without reflection. `Contact` also takes the data to
find or create one, `YeslyJson.Of(new ContactInput { ExternalId = …, Phone = … })`,
so the first call is optional.

`new YeslyClient()` reads `YESLY_API_KEY`. Every resource of the API reference
is a property: `Agents`, `Senders`, `Contacts`, `Conversations`, `Messages`,
`Templates`, `OptOuts`, `Analytics`, `Balance`, `WebhookEndpoints` and
`Events`. Parameters are `…Options` classes, and responses are model classes;
unknown fields are ignored. Every method takes an optional `RequestOptions`
and `CancellationToken`.

## Integration checklist

These are the points an integration most often gets wrong; check them before
going live. The whole API reference is also in Markdown, for agents and other
tools: [llms-full.txt](https://yesly.dev/llms-full.txt) (index in
[llms.txt](https://yesly.dev/llms.txt), OpenAPI in
[openapi.yaml](https://yesly.dev/openapi.yaml)).

1. **Read your agent first** (`yesly.Agents.RetrieveAsync`). It declares the
   `outcomes` it can end with and the `context` it expects; a context that
   does not fit is a `422` `context_invalid` with the field in `param`.
2. **Use your own id as the contact's `external_id`.** Contacts are matched
   only by exact identifiers (`external_id`, then a phone, then an email), and
   `Contacts.CreateAsync` adds what is new to the one it finds. An identifier
   that belongs to another contact is a `409` `contact_identifier_taken`:
   merge the two first (`yesly.Contacts.MergeAsync`).
3. **Send an `Idempotency-Key` with every new conversation**, derived from your
   own data (`order-8841-delivery`). The library adds a random one to each
   call; your own makes a retry after a crash safe too.
4. **Send times with an offset** (`2026-12-01T10:00:00+01:00`) and phones in
   E.164. Live contacts must be in Spain, and not premium or special-rate
   numbers (`phone_not_allowed`).
5. **Handle the outcome on a webhook.** Verify the signature over the raw
   body, answer `2xx` within 10 seconds before doing slow work, and skip event
   ids you have already handled: a delivery can arrive more than once.
   `conversation.ended` carries the conversation with its `outcome`
   (`{ code, data }`); `conversation.no_reply`, `conversation.cancelled` and
   `conversation.failed` close the others.
6. **Build and test with a `ysk_test_` key.** The test phone numbers choose
   what happens (`+34600000001` answers and the agent ends with its first
   outcome, `+34600000009` never answers; the full table is in the reference)
   and waits last seconds.
7. **Retry only what can succeed.** The library already retries network
   errors, `408`, `429` and `5xx`, with the same idempotency key. A `4xx`
   fails the same way again: fix the request using the error's `code` and
   `param`.
8. **Keep keys on the server.** A read-only key can list and retrieve but not
   write (`api_key_read_only`); a key limited to one agent gets `403`
   `api_key_agent_scope` on any other.

## Webhooks

```csharp
app.MapPost("/yesly/webhook", async (HttpRequest request) =>
{
    var body = await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        var e = Webhook.ConstructEvent(body, request.Headers["Yesly-Signature"], secret);
        if (e.Type == "conversation.ended")
        {
            var conversation = e.Data!.Conversation!;
            var outcome = conversation.Outcome; // Code "rescheduled", Data["delivery_window"]
        }
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

await foreach (var c in yesly.Conversations.ListAutoPagingAsync(new ConversationListOptions { Agent = "agt_8Hd2kQ4mZ1LpXy", Status = "no_reply" }))
    Console.WriteLine(c.Id);
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
reused on every retry; pass your own with `RequestOptions`.

```csharp
var yesly = new YeslyClient(new YeslyClientOptions { ApiKey = key, Timeout = TimeSpan.FromSeconds(10), MaxRetries = 4 });
await yesly.Conversations.CancelAsync("cnv_7Qp4Ns2Xk9Lm3D", new RequestOptions { IdempotencyKey = "cancel-order-8841" });
```

## Test mode

A `ysk_test_` key runs everything in test mode; `yesly.Mode` says which. Use
the test phone numbers from the API reference (`+34600000001` answers on the
first channel and the agent ends with its first outcome, and so on).

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
