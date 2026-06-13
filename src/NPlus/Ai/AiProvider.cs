using System;
using System.Collections.Generic;

namespace NPlus.Ai;

/// <summary>The chat backends the user can pick from.</summary>
public enum AiProvider
{
    OpenAI,
    AzureOpenAI,
    Gemini,
    Claude,
    Ollama,
    Perplexity,
}

/// <summary>
/// The on-the-wire request/response family a provider belongs to. Several providers
/// speak the same OpenAI "chat completions" dialect, so the client groups them.
/// </summary>
public enum AiWireFormat
{
    OpenAiChat, // OpenAI, Azure OpenAI, Perplexity
    Anthropic,  // Claude
    Gemini,     // Google Generative Language
    Ollama,     // local Ollama /api/chat
}

/// <summary>Static, per-provider defaults and capability flags used by the UI and client.</summary>
public sealed class AiProviderInfo
{
    public required AiProvider Provider { get; init; }
    public required string DisplayName { get; init; }
    public required AiWireFormat Wire { get; init; }
    public required string DefaultEndpoint { get; init; }
    public required string DefaultModel { get; init; }

    /// <summary>True when an API key is required (false for a local Ollama server).</summary>
    public bool NeedsApiKey { get; init; } = true;
    /// <summary>True when the "model" field is really a deployment name (Azure).</summary>
    public bool ModelIsDeployment { get; init; }
    /// <summary>True when an API version is part of the request (Azure).</summary>
    public bool NeedsApiVersion { get; init; }
    public string DefaultApiVersion { get; init; } = "";
    /// <summary>Hint shown under the endpoint field in Settings.</summary>
    public string EndpointHint { get; init; } = "";
}

public static class AiProviders
{
    private static readonly Dictionary<AiProvider, AiProviderInfo> Map = new()
    {
        [AiProvider.OpenAI] = new AiProviderInfo
        {
            Provider = AiProvider.OpenAI, DisplayName = "OpenAI (ChatGPT)", Wire = AiWireFormat.OpenAiChat,
            DefaultEndpoint = "https://api.openai.com/v1", DefaultModel = "gpt-4o-mini",
        },
        [AiProvider.AzureOpenAI] = new AiProviderInfo
        {
            Provider = AiProvider.AzureOpenAI, DisplayName = "Azure OpenAI", Wire = AiWireFormat.OpenAiChat,
            DefaultEndpoint = "", DefaultModel = "", ModelIsDeployment = true,
            NeedsApiVersion = true, DefaultApiVersion = "2024-06-01",
            EndpointHint = "Resource endpoint, e.g. https://my-resource.openai.azure.com",
        },
        [AiProvider.Gemini] = new AiProviderInfo
        {
            Provider = AiProvider.Gemini, DisplayName = "Google Gemini", Wire = AiWireFormat.Gemini,
            DefaultEndpoint = "https://generativelanguage.googleapis.com", DefaultModel = "gemini-1.5-flash",
        },
        [AiProvider.Claude] = new AiProviderInfo
        {
            Provider = AiProvider.Claude, DisplayName = "Anthropic Claude", Wire = AiWireFormat.Anthropic,
            DefaultEndpoint = "https://api.anthropic.com", DefaultModel = "claude-sonnet-4-6",
        },
        [AiProvider.Ollama] = new AiProviderInfo
        {
            Provider = AiProvider.Ollama, DisplayName = "Ollama (local)", Wire = AiWireFormat.Ollama,
            DefaultEndpoint = "http://localhost:11434", DefaultModel = "llama3.2", NeedsApiKey = false,
            EndpointHint = "Local Ollama server, default http://localhost:11434",
        },
        [AiProvider.Perplexity] = new AiProviderInfo
        {
            Provider = AiProvider.Perplexity, DisplayName = "Perplexity", Wire = AiWireFormat.OpenAiChat,
            DefaultEndpoint = "https://api.perplexity.ai", DefaultModel = "sonar",
        },
    };

    public static AiProviderInfo Info(AiProvider p) => Map[p];

    public static IEnumerable<AiProviderInfo> All
    {
        get { foreach (AiProvider p in Enum.GetValues<AiProvider>()) yield return Map[p]; }
    }

    public static bool TryParse(string? name, out AiProvider provider) =>
        Enum.TryParse(name, ignoreCase: true, out provider);
}
