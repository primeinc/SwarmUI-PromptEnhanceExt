using System.Text.Json.Serialization;

namespace PromptEnhance.WebAPI.Models;

// DTOs for the single OpenAI-compatible backend this extension owns.
// Wire shapes follow the canonical OpenAI schema (openai-openapi/openapi.yaml):
//   GET  {base}/v1/models          -> ListModelsResponse
//   POST {base}/v1/chat/completions -> CreateChatCompletionResponse
// Request bodies are built as plain objects in BackendSchema and serialized with System.Text.Json.
// The SwarmUI API boundary itself speaks Newtonsoft JObject; these DTOs are only for the HTTP wire.

/// <summary>A model entry surfaced to the UI dropdown. <see cref="Id"/> is the value sent back as the chat model.</summary>
public class ModelData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>Human-friendly label. Defaults to <see cref="Id"/> when the server provides nothing better.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

/// <summary>Response of <c>GET {base}/v1/models</c> — <c>{ "object": "list", "data": [ { "id": ... } ] }</c>.</summary>
public class ModelsListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; }

    [JsonPropertyName("data")]
    public List<ModelEntry> Data { get; set; }
}

/// <summary>A single entry in the <c>/v1/models</c> <c>data</c> array.</summary>
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

/// <summary>Response of <c>POST {base}/v1/chat/completions</c>. Enhanced text is at <c>choices[0].message.content</c>.</summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("choices")]
    public List<ChatChoice> Choices { get; set; }
}

/// <summary>A single choice in a chat completion response.</summary>
public class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; }

    [JsonPropertyName("message")]
    public ChatResponseMessage Message { get; set; }
}

/// <summary>The assistant message inside a chat completion choice. <see cref="Content"/> may be null per the spec.</summary>
public class ChatResponseMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
}

/// <summary>OpenAI-style error envelope: <c>{ "error": { "message": ..., "type": ..., "code": ... } }</c>.</summary>
public class ChatErrorResponse
{
    [JsonPropertyName("error")]
    public ChatError Error { get; set; }
}

/// <summary>The error body of an OpenAI-style error response.</summary>
public class ChatError
{
    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }
}
