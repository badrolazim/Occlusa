using System.Text.Json.Serialization;

namespace Occlusa.Api.Models;

/// <summary>
/// Standard Meta WhatsApp Cloud API webhook payload models.
/// </summary>
public sealed class MetaWebhookPayload
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("entry")]
    public List<MetaWebhookEntry> Entry { get; set; } = new();
}

public sealed class MetaWebhookEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("changes")]
    public List<MetaWebhookChange> Changes { get; set; } = new();
}

public sealed class MetaWebhookChange
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public MetaWebhookValue? Value { get; set; }
}

public sealed class MetaWebhookValue
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public MetaWebhookMetadata? Metadata { get; set; }

    [JsonPropertyName("contacts")]
    public List<MetaWebhookContact> Contacts { get; set; } = new();

    [JsonPropertyName("messages")]
    public List<MetaWebhookMessage> Messages { get; set; } = new();
}

public sealed class MetaWebhookMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("phone_number_id")]
    public string PhoneNumberId { get; set; } = string.Empty;
}

public sealed class MetaWebhookContact
{
    [JsonPropertyName("wa_id")]
    public string WaId { get; set; } = string.Empty;

    [JsonPropertyName("profile")]
    public MetaWebhookProfile? Profile { get; set; }
}

public sealed class MetaWebhookProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class MetaWebhookMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public MetaWebhookText? Text { get; set; }
}

public sealed class MetaWebhookText
{
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}
