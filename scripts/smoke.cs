// Calls the real API with a test key: one call per resource, then cleans up.
//
//   YESLY_API_KEY=ysk_test_… dotnet run scripts/smoke.cs
#:project ../src/Yesly/Yesly.csproj

using Yesly;

var yesly = new YeslyClient();
if (yesly.Mode != "test") throw new InvalidOperationException("Use a ysk_test_ key.");
var externalId = $"SDK-DOTNET-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
void Step(string name, object? value) => Console.WriteLine($"ok  {name}: {value}");

Step("Balance.RetrieveAsync", (await yesly.Balance.RetrieveAsync()).Object);
var agents = await yesly.Agents.ListAsync();
Step("Agents.ListAsync", string.Join(",", agents.Data.Select(a => a.Id)));
Step("Senders.ListAsync", (await yesly.Senders.ListAsync()).Data.Count);
Step("Templates.ListAsync", (await yesly.Templates.ListAsync()).Data.Count);

var contact = await yesly.Contacts.CreateAsync(new ContactCreateOptions { ExternalId = externalId, Name = "SDK smoke", Phone = "+34600000001", Metadata = new() { ["source"] = "sdk-smoke" } });
Step("Contacts.CreateAsync", contact.Id);
Step("Contacts.CreateAsync again (same contact)", (await yesly.Contacts.CreateAsync(new ContactCreateOptions { ExternalId = externalId, Phone = "+34600000001" })).Id == contact.Id);
Step("Contacts.RetrieveAsync", (await yesly.Contacts.RetrieveAsync(contact.Id!)).ExternalId);
Step("Contacts.UpdateAsync", (await yesly.Contacts.UpdateAsync(contact.Id!, new ContactUpdateOptions { Metadata = new() { ["note"] = "updated" } })).Metadata!["note"]);
var n = 0;
await foreach (var c in yesly.Contacts.ListAutoPagingAsync(new ContactListOptions { Limit = 1 }))
    if (++n >= 3) break;
Step("Contacts.ListAutoPagingAsync (limit 1)", n);
Step("Contacts.ListMemoryAsync", (await yesly.Contacts.ListMemoryAsync(contact.Id!)).Data.Count);

var agent = agents.Data.FirstOrDefault(a => a.Status == "active");
if (agent is not null)
{
    var create = new ConversationCreateOptions { Agent = agent.Id, Contact = YeslyJson.Of(contact.Id!), Channels = new() { agent.Channels![0] } };
    var key = new RequestOptions { IdempotencyKey = $"{externalId}-cnv" };
    var cnv = await yesly.Conversations.CreateAsync(create, key);
    Step("Conversations.CreateAsync", $"{cnv.Id} {cnv.Status}");
    Step("Conversations.CreateAsync again (replayed)", (await yesly.Conversations.CreateAsync(create, key)).Id == cnv.Id);
    Step("Conversations.RetrieveAsync", (await yesly.Conversations.RetrieveAsync(cnv.Id!)).Status);
    Step("Conversations.ListAsync", (await yesly.Conversations.ListAsync(new ConversationListOptions { Contact = contact.Id })).Data.Count);
    Step("Conversations.ListMessagesAsync", (await yesly.Conversations.ListMessagesAsync(cnv.Id!)).Data.Count);
    Step("Messages.ListAsync", (await yesly.Messages.ListAsync(new MessageListOptions { Conversation = cnv.Id })).Data.Count);
}
else
{
    Step("Conversations.*", "skipped: the account has no active agent");
}

var optOut = await yesly.OptOuts.CreateAsync(new OptOutCreateOptions { Channel = "sms", Address = "+34600000099" });
Step("OptOuts.CreateAsync", optOut.Id);
Step("OptOuts.DeleteAsync", (await yesly.OptOuts.DeleteAsync(optOut.Id!)).Deleted);

var we = await yesly.WebhookEndpoints.CreateAsync(new WebhookEndpointCreateOptions { Url = "https://example.com/yesly-sdk-smoke", EnabledEvents = new() { "conversation.ended" } });
Step("WebhookEndpoints.CreateAsync", we.Id);
Step("WebhookEndpoints.RollSecretAsync", (await yesly.WebhookEndpoints.RollSecretAsync(we.Id!)).Id);
Step("WebhookEndpoints.DeleteAsync", (await yesly.WebhookEndpoints.DeleteAsync(we.Id!)).Deleted);

var events = await yesly.Events.ListAsync(new EventListOptions { Limit = 5 });
Step("Events.ListAsync", string.Join(",", events.Data.Select(e => e.Type)));
if (events.Data.Count > 0) Step("Events.RetrieveAsync", (await yesly.Events.RetrieveAsync(events.Data[0].Id!)).Id);
Step("Balance.ListTransactionsAsync", (await yesly.Balance.ListTransactionsAsync(new BalanceListTransactionsOptions { Limit = 5 })).Data.Count);
Step("Analytics.SummaryAsync", (await yesly.Analytics.SummaryAsync(new AnalyticsSummaryOptions { Range = "7d" })).Conversations);

try
{
    await yesly.Contacts.CreateAsync(new ContactCreateOptions { ExternalId = $"{externalId}-bad", Phone = "612" });
}
catch (InvalidRequestException e)
{
    Step("validation error", $"{e.GetType().Name} {e.Status} {e.Code} {e.Param} {(e.RequestId is null ? "NO REQUEST ID" : "with request id")}");
}
Step("Contacts.DeleteAsync", (await yesly.Contacts.DeleteAsync(contact.Id!)).Deleted);
