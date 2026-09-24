using System.Text.Json.Serialization;

namespace PromptEnhance.WebAPI.Models;

// These are System.Text.Json wire shapes: properties, not fields, because System.Text.Json binds properties by default.

/// <summary>A model entry as the extension's API returns it to the frontend (id + display name).</summary>
public class ModelData
{
    /// <summary>The model id the backend expects in chat requests.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>The name shown in the model dropdown.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

/// <summary>Wire shape of an OpenAI-compatible `GET /v1/models` response envelope.</summary>
public class ModelsListResponse
{
    /// <summary>The envelope type, "list".</summary>
    [JsonPropertyName("object")]
    public string Object { get; set; }

    /// <summary>The models the backend offers.</summary>
    [JsonPropertyName("data")]
    public List<ModelEntry> Data { get; set; }
}

/// <summary>One entry in the `/v1/models` data array.</summary>
public class ModelEntry
{
    /// <summary>The model id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>The entry type, "model".</summary>
    [JsonPropertyName("object")]
    public string Object { get; set; }

    /// <summary>Creation time, Unix seconds.</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>The owning organization, as the backend reports it.</summary>
    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; set; }
}

/// <summary>Wire shape of an OpenAI-compatible `POST /v1/chat/completions` success response.</summary>
public class ChatCompletionResponse
{
    /// <summary>The completion id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>The model that answered.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; }

    /// <summary>The generated choices; the extension reads the first.</summary>
    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; }
}

/// <summary>One generated choice in a chat completion.</summary>
public class ChatChoice
{
    /// <summary>The choice's position in the list.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>Why generation stopped, e.g. "stop" or "length".</summary>
    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; }

    /// <summary>The assistant message.</summary>
    [JsonPropertyName("message")]
    public ChatResponseMessage Message { get; set; }
}

/// <summary>The assistant message inside a chat choice.</summary>
public class ChatResponseMessage
{
    /// <summary>The speaker, "assistant".</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; }

    /// <summary>The generated text: the enhanced prompt.</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; }
}

/// <summary>Wire shape of an OpenAI-style error envelope (`{"error":{"message":...}}`).</summary>
public class ChatErrorResponse
{
    /// <summary>The error details.</summary>
    [JsonPropertyName("error")]
    public ChatError Error { get; set; }
}

/// <summary>The details inside an OpenAI-style error envelope.</summary>
public class ChatError
{
    /// <summary>The backend's human-readable reason.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; }

    /// <summary>The error class, e.g. "invalid_request_error".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>The backend's error code, e.g. "invalid_api_key".</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; }
}
