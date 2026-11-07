// Calls the real API with a test key: one call per resource, then cleans up.
//
//   YESLY_API_KEY=ysk_test_… YESLY_BASE_URL=https://api.dev.yesly.dev/v1 dotnet run scripts/smoke.cs
#:project ../src/Yesly/Yesly.csproj

using Yesly;

var yesly = new YeslyClient();
if (yesly.Mode != "test") throw new InvalidOperationException("Use a ysk_test_ key.");
var tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
var externalId = $"SDK-DOTNET-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
void Step(string name, object? value) => Console.WriteLine($"ok  {name}: {value}");

Step("Balance.RetrieveAsync", (await yesly.Balance.RetrieveAsync()).Object);
var types = await yesly.AppointmentTypes.ListAsync();
Step("AppointmentTypes.ListAsync", types.Data[0].Id);
Step("Locations.ListAsync", (await yesly.Locations.ListAsync()).Data.Count);

var apt = await yesly.Appointments.CreateAsync(new AppointmentCreateOptions
{
    ExternalId = externalId,
    Type = types.Data[0].Id,
    Customer = new() { Name = "SDK smoke", Phone = "+34600000001" },
    Slot = new() { Start = $"{tomorrow}T10:00:00+01:00", End = $"{tomorrow}T12:00:00+01:00" },
    Metadata = new() { ["source"] = "sdk-smoke" },
});
Step("Appointments.CreateAsync", $"{apt.Id} {apt.Status}");
Step("Appointments.RetrieveAsync", (await yesly.Appointments.RetrieveAsync(apt.Id!)).ExternalId);
var n = 0;
await foreach (var a in yesly.Appointments.ListAutoPagingAsync(new AppointmentListOptions { Metadata = new() { ["source"] = "sdk-smoke" }, Limit = 1 }))
    if (++n >= 3) break;
Step("Appointments.ListAutoPagingAsync (limit 1)", n);
Step("Appointments.UpdateAsync", (await yesly.Appointments.UpdateAsync(apt.Id!, new AppointmentUpdateOptions { Metadata = new() { ["note"] = "updated" } })).Metadata!["note"]);
Step("Appointments.CancelAsync", (await yesly.Appointments.CancelAsync(apt.Id!, new AppointmentCancelOptions { Reason = "SDK smoke test" })).Status);

var we = await yesly.WebhookEndpoints.CreateAsync(new WebhookEndpointCreateOptions { Url = "https://example.com/yesly-sdk-smoke", EnabledEvents = new() { "appointment.confirmed" } });
Step("WebhookEndpoints.CreateAsync", we.Id);
Step("WebhookEndpoints.RollSecretAsync", (await yesly.WebhookEndpoints.RollSecretAsync(we.Id!)).Id);
Step("WebhookEndpoints.DeleteAsync", (await yesly.WebhookEndpoints.DeleteAsync(we.Id!)).Deleted);

var events = await yesly.Events.ListAsync(new EventListOptions { Limit = 5 });
Step("Events.ListAsync", string.Join(",", events.Data.Select(e => e.Type)));
Step("Events.RetrieveAsync", (await yesly.Events.RetrieveAsync(events.Data[0].Id!)).Id);
Step("Balance.ListTransactionsAsync", (await yesly.Balance.ListTransactionsAsync(new BalanceListTransactionsOptions { Limit = 5 })).Data.Count);
Step("Analytics.RetrieveSummaryAsync", (await yesly.Analytics.RetrieveSummaryAsync(new AnalyticsRetrieveSummaryOptions { Range = "7d" })).Appointments);

try
{
    await yesly.Appointments.CreateAsync(new AppointmentCreateOptions { ExternalId = $"{externalId}-bad", Customer = new() { Name = "x", Phone = "612" }, Slot = new() { Start = $"{tomorrow}T10:00:00+01:00" } });
}
catch (InvalidRequestException e)
{
    Step("validation error", $"{e.GetType().Name} {e.Status} {e.Code} {e.Param} {(e.RequestId is null ? "NO REQUEST ID" : "with request id")}");
}
