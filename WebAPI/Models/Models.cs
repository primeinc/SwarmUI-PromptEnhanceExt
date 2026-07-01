using System.Text.Json.Serialization;

namespace PromptEnhance.WebAPI.Models;

/// <summary>A model entry as the extension's API returns it to the frontend (id + display name).</summary>
public class ModelData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }
}

/// <summary>Wire shape of an OpenAI-compatible `GET /v1/models` response envelope.</summary>
public class ModelsListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; }

    [JsonPropertyName("data")]
    public List<ModelEntry> Data { get; set; }
}

/// <summary>One entry in the `/v1/models` data array. Only `id` is load-bearing; the rest exists to match the wire schema.</summary>
public class ModelEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; set; }
}

/// <summary>Wire shape of an OpenAI-compatible `POST /v1/chat/completions` success response.</summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; }

    [JsonPropertyName("message")]
    public ChatResponseMessage Message { get; set; }
}

public class ChatResponseMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
}

/// <summary>Wire shape of an OpenAI-style error envelope (`{"error":{"message":...}}`), used to extract readable detail from non-success bodies.</summary>
public class ChatErrorResponse
{
    [JsonPropertyName("error")]
    public ChatError Error { get; set; }
}

public class ChatError
{
    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>Typed as string: servers send both string and null codes; a numeric code would be a schema violation worth surfacing as invalid shape.</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; }
}
