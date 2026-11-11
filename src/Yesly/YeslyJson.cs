using System.Text.Json;

namespace Yesly;

/// <summary>
/// Builds the <see cref="JsonElement"/> values that free-form fields take: a conversation's
/// <c>contact</c> (an id or a <see cref="ContactInput"/>) and the values of its <c>context</c>.
/// Uses source-generated metadata, so it works with trimming and Native AOT.
/// </summary>
public static class YeslyJson
{
    public static JsonElement Of(string value) => JsonSerializer.SerializeToElement(value, YeslyJsonContext.Default.String);

    public static JsonElement Of(long value) => JsonSerializer.SerializeToElement(value, YeslyJsonContext.Default.Int64);

    public static JsonElement Of(double value) => JsonSerializer.SerializeToElement(value, YeslyJsonContext.Default.Double);

    public static JsonElement Of(bool value) => JsonSerializer.SerializeToElement(value, YeslyJsonContext.Default.Boolean);

    /// <summary>The data to find or create a contact, for <c>contact</c> on a new conversation or message.</summary>
    public static JsonElement Of(ContactInput contact) => JsonSerializer.SerializeToElement(contact, YeslyJsonContext.Default.ContactInput);
}
