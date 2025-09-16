using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.IO;

#nullable enable

namespace Gemini.Api
{
    /// <summary>
    /// Provides a client for accessing the Google Gemini API.
    /// </summary>
    public sealed class GenAI
    {
        private readonly string _apiKey;

        /// <summary>
        /// Initializes a new instance of the GenAI client with the specified API key.
        /// </summary>
        /// <param name="apiKey">The API key obtained from Google AI Studio.</param>
        /// <exception cref="ArgumentNullException">Thrown if apiKey is null or empty.</exception>
        public GenAI(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentNullException(nameof(apiKey));
            }
            _apiKey = apiKey;
        }

        /// <summary>
        /// Gets a generative model instance for the specified model name.
        /// </summary>
        /// <param name="modelName">The name of the model to use (e.g., "gemini-1.5-flash").</param>
        /// <returns>An instance of GenerativeModel for content generation.</returns>
        public GenerativeModel GetModel(string modelName = "gemini-1.5-flash")
        {
            return new GenerativeModel(_apiKey, modelName);
        }
    }

    /// <summary>
    /// Represents a generative model that provides content generation and chat functionalities.
    /// </summary>
    public sealed class GenerativeModel
    {
        private const string ApiVersion = "v1beta";
        private const string BaseUrl = "https://generativelanguage.googleapis.com";

        private static readonly HttpClient _httpClient = new();
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
        };
        private readonly string _modelName;
        private readonly string _apiKey;

        internal GenerativeModel(string apiKey, string modelName)
        {
            _apiKey = apiKey;
            _modelName = modelName;
        }

        private HttpRequestMessage CreateRequestMessage(HttpMethod method, string url, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("x-goog-api-key", _apiKey);
            request.Content = content;
            return request;
        }

        /// <summary>
        /// Generates content based on the provided request.
        /// </summary>
        /// <param name="request">The request containing content, tools, and generation configuration.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous generation operation. The task result contains the generated response.</returns>
        /// <exception cref="GeminiApiException">Thrown if the API request fails.</exception>
        public async Task<GenerateContentResponse> GenerateContentAsync(
            GenerateContentRequest request,
            CancellationToken cancellationToken = default)
        {
            var endpointUrl = $"{BaseUrl}/{ApiVersion}/models/{_modelName}:generateContent";
            var jsonRequest = JsonSerializer.Serialize(request, _jsonSerializerOptions);
            using var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            using var httpRequest = CreateRequestMessage(HttpMethod.Post, endpointUrl, content);
            
            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GeminiApiException($"API request failed with status code {response.StatusCode}: {jsonResponse}");
            }

            var result = JsonSerializer.Deserialize<GenerateContentResponse>(jsonResponse, _jsonSerializerOptions);
            return result ?? throw new GeminiApiException("Failed to deserialize API response.");
        }
    }

    #region Request/Response Models

    /// <summary>
    /// Request to generate content from the model.
    /// </summary>
    public record GenerateContentRequest
    {
        /// <summary>
        /// Required. The content of the current conversation with the model.
        /// </summary>
        [JsonPropertyName("contents")]
        public IEnumerable<Content> Contents { get; init; } = Enumerable.Empty<Content>();

        /// <summary>
        /// Optional. A list of Tools the model may use to generate the next response.
        /// </summary>
        [JsonPropertyName("tools")]
        public IEnumerable<Tool>? Tools { get; init; }

        /// <summary>
        /// Optional. Tool configuration for any Tool specified in the request.
        /// </summary>
        [JsonPropertyName("toolConfig")]
        public ToolConfig? ToolConfig { get; init; }

        /// <summary>
        /// Optional. A list of unique SafetySetting instances for blocking unsafe content.
        /// </summary>
        [JsonPropertyName("safetySettings")]
        public IEnumerable<SafetySetting>? SafetySettings { get; init; }

        /// <summary>
        /// Optional. Developer set system instruction(s). Currently, text only.
        /// </summary>
        [JsonPropertyName("systemInstruction")]
        public Content? SystemInstruction { get; init; }

        /// <summary>
        /// Optional. Configuration options for model generation and outputs.
        /// </summary>
        [JsonPropertyName("generationConfig")]
        public GenerationConfig? GenerationConfig { get; init; }
    }

    /// <summary>
    /// Response from the model supporting multiple candidate responses.
    /// </summary>
    public record GenerateContentResponse
    {
        /// <summary>
        /// Candidate responses from the model.
        /// </summary>
        [JsonPropertyName("candidates")]
        public IEnumerable<Candidate>? Candidates { get; init; }

        /// <summary>
        /// Returns the prompt's feedback related to the content filters.
        /// </summary>
        [JsonPropertyName("promptFeedback")]
        public PromptFeedback? PromptFeedback { get; init; }
        
        /// <summary>
        /// Metadata on the generation requests' token usage.
        /// </summary>
        [JsonPropertyName("usageMetadata")]
        public UsageMetadata? UsageMetadata { get; init; }

        /// <summary>
        /// Extracts the text from the first part of the first candidate, if available.
        /// </summary>
        /// <returns>The generated text, or null if not available.</returns>
        public string? Text() => Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault(p => p.Text != null)?.Text;
    }
    
    /// <summary>
    /// The base structured data type containing multi-part content of a message.
    /// </summary>
    public record Content
    {
        /// <summary>
        /// Ordered collection of Parts that constitute a single message.
        /// </summary>
        [JsonPropertyName("parts")]
        public IEnumerable<Part>? Parts { get; init; }

        /// <summary>
        /// The producer of the content. Must be either "user" or "model".
        /// </summary>
        [JsonPropertyName("role")]
        public string? Role { get; init; }

        public static Content ForUser(string text) => new() { Role = "user", Parts = new[] { new Part { Text = text } } };
        public static Content ForModel(string text) => new() { Role = "model", Parts = new[] { new Part { Text = text } } };
        public static Content ForTool(string functionName, object responseData) =>
            new() {
                Role = "tool",
                Parts = new[] {
                    new Part {
                        FunctionResponse = new FunctionResponse {
                            Name = functionName,
                            Response = new { content = responseData }
                        }
                    }
                }
            };
    }

    /// <summary>
    /// A datatype containing media that is part of a multi-part Content message.
    /// </summary>
    public record Part
    {
        /// <summary>
        /// Inline text.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; init; }
        
        /// <summary>
        /// Inline media bytes.
        /// </summary>
        [JsonPropertyName("inline_data")]
        public InlineData? InlineData { get; init; }
        
        /// <summary>
        /// A predicted function call generated from the model.
        /// </summary>
        [JsonPropertyName("functionCall")]
        public FunctionCall? FunctionCall { get; init; }
        
        /// <summary>
        /// The result of a function call.
        /// </summary>
        [JsonPropertyName("functionResponse")]
        public FunctionResponse? FunctionResponse { get; init; }
    }
    
    /// <summary>
    /// Raw media bytes.
    /// </summary>
    public record InlineData
    {
        /// <summary>
        /// The IANA standard MIME type of the source data.
        /// </summary>
        [JsonPropertyName("mime_type")]
        public string MimeType { get; init; } = string.Empty;
        
        /// <summary>
        /// Base64-encoded data.
        /// </summary>
        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;

        public static async Task<InlineData> FromFileAsync(string filePath)
        {
            var mimeType = GetMimeType(filePath);
            var bytes = await File.ReadAllBytesAsync(filePath);
            var base64 = Convert.ToBase64String(bytes);
            return new InlineData { MimeType = mimeType, Data = base64 };
        }

        private static string GetMimeType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// A response candidate generated from the model.
    /// </summary>
    public record Candidate
    {
        /// <summary>
        /// Generated content returned from the model.
        /// </summary>
        [JsonPropertyName("content")]
        public Content? Content { get; init; }

        /// <summary>
        /// The reason why the model stopped generating tokens.
        /// </summary>
        [JsonPropertyName("finishReason")]
        public FinishReason? FinishReason { get; init; }

        /// <summary>
        /// List of ratings for the safety of a response candidate.
        /// </summary>
        [JsonPropertyName("safetyRatings")]
        public IEnumerable<SafetyRating>? SafetyRatings { get; init; }

        /// <summary>
        /// Citation information for model-generated candidate.
        /// </summary>
        [JsonPropertyName("citationMetadata")]
        public CitationMetadata? CitationMetadata { get; init; }

        /// <summary>
        /// Token count for this candidate.
        /// </summary>
        [JsonPropertyName("tokenCount")]
        public int? TokenCount { get; init; }

        /// <summary>
        /// Index of the candidate in the list of response candidates.
        /// </summary>
        [JsonPropertyName("index")]
        public int? Index { get; init; }
    }

    /// <summary>
    /// Configuration options for model generation and outputs.
    /// </summary>
    public record GenerationConfig
    {
        /// <summary>
        /// Controls the randomness of the output.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }

        /// <summary>
        /// The maximum number of tokens to consider when sampling.
        /// </summary>
        [JsonPropertyName("topK")]
        public int? TopK { get; init; }

        /// <summary>
        /// The maximum cumulative probability of tokens to consider when sampling.
        /// </summary>
        [JsonPropertyName("topP")]
        public double? TopP { get; init; }

        /// <summary>
        /// Number of generated responses to return.
        /// </summary>
        [JsonPropertyName("candidateCount")]
        public int? CandidateCount { get; init; }
        
        /// <summary>
        /// The maximum number of tokens to include in a candidate.
        /// </summary>
        [JsonPropertyName("maxOutputTokens")]
        public int? MaxOutputTokens { get; init; }

        /// <summary>
        /// The set of character sequences that will stop output generation.
        /// </summary>
        [JsonPropertyName("stopSequences")]
        public IEnumerable<string>? StopSequences { get; init; }

        /// <summary>
        /// MIME type of the generated candidate text.
        /// </summary>
        [JsonPropertyName("responseMimeType")]
        public string? ResponseMimeType { get; init; }

        /// <summary>
        /// Output schema of the generated candidate text.
        /// </summary>
        [JsonPropertyName("responseSchema")]
        public Schema? ResponseSchema { get; init; }
    }

    #endregion

    #region Safety and Feedback Models
    
    /// <summary>
    /// A set of the feedback metadata the prompt specified in a request.
    /// </summary>
    public record PromptFeedback
    {
        /// <summary>
        /// If set, the prompt was blocked and no candidates are returned.
        /// </summary>
        [JsonPropertyName("blockReason")]
        public BlockReason? BlockReason { get; init; }

        /// <summary>
        /// Ratings for safety of the prompt.
        /// </summary>
        [JsonPropertyName("safetyRatings")]
        public IEnumerable<SafetyRating>? SafetyRatings { get; init; }
    }

    /// <summary>
    /// Safety rating for a piece of content.
    /// </summary>
    public record SafetyRating
    {
        /// <summary>
        /// The category for this rating.
        /// </summary>
        [JsonPropertyName("category")]
        public HarmCategory Category { get; init; }

        /// <summary>
        /// The probability of harm for this content.
        /// </summary>
        [JsonPropertyName("probability")]
        public string? Probability { get; init; }
    }
    
    /// <summary>
    /// Safety setting, affecting the safety-blocking behavior.
    /// </summary>
    public record SafetySetting
    {
        /// <summary>
        /// The category for this setting.
        /// </summary>
        [JsonPropertyName("category")]
        public HarmCategory Category { get; init; }

        /// <summary>
        /// Controls the probability threshold at which harm is blocked.
        /// </summary>
        [JsonPropertyName("threshold")]
        public HarmBlockThreshold Threshold { get; init; }
    }
    
    /// <summary>
    /// Metadata on the generation request's token usage.
    /// </summary>
    public record UsageMetadata
    {
        /// <summary>
        /// Number of tokens in the prompt.
        /// </summary>
        [JsonPropertyName("promptTokenCount")]
        public int? PromptTokenCount { get; init; }

        /// <summary>
        /// Total number of tokens across all the generated response candidates.
        /// </summary>
        [JsonPropertyName("candidatesTokenCount")]
        public int? CandidatesTokenCount { get; init; }

        /// <summary>
        /// Total token count for the generation request.
        /// </summary>
        [JsonPropertyName("totalTokenCount")]
        public int? TotalTokenCount { get; init; }
    }
    
    /// <summary>
    /// A collection of source attributions for a piece of content.
    /// </summary>
    public record CitationMetadata
    {
        /// <summary>
        /// Citations to sources for a specific response.
        /// </summary>
        [JsonPropertyName("citationSources")]
        public IEnumerable<CitationSource>? CitationSources { get; init; }
    }

    /// <summary>
    /// A citation to a source for a portion of a specific response.
    /// </summary>
    public record CitationSource
    {
        /// <summary>
        /// Start of segment of the response that is attributed to this source.
        /// </summary>
        [JsonPropertyName("startIndex")]
        public int? StartIndex { get; init; }

        /// <summary>
        /// End of the attributed segment, exclusive.
        /// </summary>
        [JsonPropertyName("endIndex")]
        public int? EndIndex { get; init; }

        /// <summary>
        /// URI that is attributed as a source for a portion of the text.
        /// </summary>
        [JsonPropertyName("uri")]
        public string? Uri { get; init; }

        /// <summary>
        /// License for the project that is attributed as a source for segment.
        /// </summary>
        [JsonPropertyName("license")]
        public string? License { get; init; }
    }
    
    public enum HarmCategory { HARM_CATEGORY_UNSPECIFIED, HARM_CATEGORY_HATE_SPEECH, HARM_CATEGORY_SEXUALLY_EXPLICIT, HARM_CATEGORY_DANGEROUS_CONTENT, HARM_CATEGORY_HARASSMENT, HARM_CATEGORY_CIVIC_INTEGRITY }
    public enum HarmBlockThreshold { HARM_BLOCK_THRESHOLD_UNSPECIFIED, BLOCK_LOW_AND_ABOVE, BLOCK_MEDIUM_AND_ABOVE, BLOCK_ONLY_HIGH, BLOCK_NONE }
    public enum BlockReason { BLOCK_REASON_UNSPECIFIED, SAFETY, OTHER, BLOCKLIST, PROHIBITED_CONTENT }
    public enum FinishReason { FINISH_REASON_UNSPECIFIED, STOP, MAX_TOKENS, SAFETY, RECITATION, LANGUAGE, OTHER, BLOCKLIST, PROHIBITED_CONTENT, SPII, MALFORMED_FUNCTION_CALL, UNEXPECTED_TOOL_CALL, TOO_MANY_TOOL_CALLS }
    
    #endregion
    
    #region Tool and Schema Models
    
    /// <summary>
    /// A predicted function call returned from the model.
    /// </summary>
    public record FunctionCall
    {
        /// <summary>
        /// The name of the function to call.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
        
        /// <summary>
        /// The arguments to the function call.
        /// </summary>
        [JsonPropertyName("args")]
        public JsonElement Args { get; set; }
    }

    /// <summary>
    /// The response from a function call.
    /// </summary>
    public record FunctionResponse
    {
        /// <summary>
        /// The name of the function that was called.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
        
        /// <summary>
        /// The function response content.
        /// </summary>
        [JsonPropertyName("response")]
        public object? Response { get; init; }
    }

    /// <summary>
    /// A collection of function declarations.
    /// </summary>
    public record Tool { [JsonPropertyName("functionDeclarations")] public IEnumerable<FunctionDeclaration>? FunctionDeclarations { get; init; } }
    
    /// <summary>
    /// The declaration of a function that can be called by the model.
    /// </summary>
    public record FunctionDeclaration { [JsonPropertyName("name")] public string Name { get; init; } = string.Empty; [JsonPropertyName("description")] public string Description { get; init; } = string.Empty; [JsonPropertyName("parameters")] public Schema? Parameters { get; init; } }
    
    /// <summary>
    /// The schema for a function's parameters.
    /// </summary>
    public record Schema { [JsonPropertyName("type")] public SchemaType Type { get; init; } [JsonPropertyName("properties")] public Dictionary<string, Schema>? Properties { get; init; } [JsonPropertyName("description")] public string? Description { get; init; } [JsonPropertyName("items")] public Schema? Items { get; init; } [JsonPropertyName("required")] public IEnumerable<string>? Required { get; init; } }
    
    /// <summary>
    /// The type of a schema.
    /// </summary>
    public enum SchemaType { STRING, NUMBER, INTEGER, BOOLEAN, ARRAY, OBJECT }

    /// <summary>
    /// Configuration for tool usage.
    /// </summary>
    public record ToolConfig
    {
        /// <summary>
        /// Configuration for function calling.
        /// </summary>
        [JsonPropertyName("function_calling_config")]
        public FunctionCallingConfig? FunctionCallingConfig { get; init; }
    }

    /// <summary>
    /// Configuration for function calling behavior.
    /// </summary>
    public record FunctionCallingConfig
    {
        /// <summary>
        /// The mode of function calling.
        /// </summary>
        [JsonPropertyName("mode")]
        public FunctionCallingMode? Mode { get; init; }

        /// <summary>
        /// A list of function names that are allowed to be called.
        /// </summary>
        [JsonPropertyName("allowed_function_names")]
        public IEnumerable<string>? AllowedFunctionNames { get; init; }
    }

    /// <summary>
    /// The mode for function calling.
    /// </summary>
    public enum FunctionCallingMode
    {
        AUTO,
        ANY,
        NONE
    }

    #endregion

    #region Exception
    
    /// <summary>
    /// Represents errors that occur during Gemini API calls.
    /// </summary>
    public class GeminiApiException : Exception
    {
        public GeminiApiException(string message) : base(message) { }
        public GeminiApiException(string message, Exception innerException) : base(message, innerException) { }
    }
    
    #endregion
}