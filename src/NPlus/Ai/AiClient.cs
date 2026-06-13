using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NPlus.Core;

namespace NPlus.Ai;

/// <summary>An AI request failed — carries a user-readable message (status + server detail).</summary>
public sealed class AiException : Exception
{
    public AiException(string message) : base(message) { }
}

/// <summary>
/// Talks to whichever provider the user selected. One class handles every backend by
/// switching on the provider's <see cref="AiWireFormat"/> when it builds the request URL,
/// headers and JSON body, and again when it parses the (streamed or whole) response.
///
///   • OpenAI / Azure OpenAI / Perplexity → OpenAI "chat completions" dialect
///   • Claude                              → Anthropic Messages API
///   • Gemini                              → Google Generative Language generateContent
///   • Ollama                              → local /api/chat (NDJSON stream)
/// </summary>
public sealed class AiClient
{
    private const int ChatMaxTokens = 1024;

    // Long timeout so streamed replies aren't cut off; streaming uses ResponseHeadersRead anyway.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };

    private readonly AiProvider _provider;
    private readonly AiProviderConfig _cfg;

    public AiClient(AiProvider provider, AiProviderConfig cfg)
    {
        _provider = provider;
        _cfg = cfg;
    }

    // ---- Public API ----

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default, int maxTokens = ChatMaxTokens)
    {
        using var req = BuildRequest(messages, stream: false, maxTokens);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        await EnsureSuccess(resp, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ExtractContent(AiProviders.Info(_provider).Wire, body);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default,
        int maxTokens = ChatMaxTokens)
    {
        var wire = AiProviders.Info(_provider).Wire;
        using var req = BuildRequest(messages, stream: true, maxTokens);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccess(resp, ct).ConfigureAwait(false);

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (line.Length == 0) continue;

            string payload;
            if (wire == AiWireFormat.Ollama)
            {
                payload = line; // NDJSON: each line is a complete JSON object
            }
            else
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue; // skip SSE "event:" / comments
                payload = line.Substring(5).Trim();
                if (payload == "[DONE]") yield break;
            }

            // Parse inside try (can't yield from a try/catch); yield the result afterwards.
            bool done = false;
            string? token = null;
            try { token = ExtractStreamToken(wire, payload, ref done); }
            catch { token = null; /* tolerate a malformed/partial chunk */ }

            if (!string.IsNullOrEmpty(token)) yield return token;
            if (done) yield break;
        }
    }

    public async Task<(bool ok, string message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var reply = (await CompleteAsync(
                new[] { new ChatMessage(ChatMessage.User, "Reply with just: OK") }, ct, maxTokens: 16)).Trim();
            if (reply.Length > 80) reply = reply.Substring(0, 80) + "…";
            return (true, string.IsNullOrEmpty(reply) ? "Connected (empty reply)." : $"Connected. Model replied: {reply}");
        }
        catch (OperationCanceledException) { return (false, "Cancelled."); }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ---- Request building ----

    private HttpRequestMessage BuildRequest(IReadOnlyList<ChatMessage> messages, bool stream, int maxTokens)
    {
        var info = AiProviders.Info(_provider);
        string endpoint = (string.IsNullOrWhiteSpace(_cfg.Endpoint) ? info.DefaultEndpoint : _cfg.Endpoint).TrimEnd('/');
        string model = string.IsNullOrWhiteSpace(_cfg.Model) ? info.DefaultModel : _cfg.Model;
        string apiVersion = string.IsNullOrWhiteSpace(_cfg.ApiVersion) ? info.DefaultApiVersion : _cfg.ApiVersion;
        string key = (_cfg.ApiKey ?? "").Trim();

        string url;
        object body;

        switch (info.Wire)
        {
            case AiWireFormat.OpenAiChat:
            {
                var b = new Dictionary<string, object?>
                {
                    ["messages"] = messages.Select(m => new Dictionary<string, object?> { ["role"] = m.Role, ["content"] = m.Content }).ToList(),
                    ["max_tokens"] = maxTokens,
                    ["stream"] = stream,
                };
                if (_provider == AiProvider.AzureOpenAI)
                {
                    url = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(model)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
                }
                else
                {
                    b["model"] = model;
                    url = $"{endpoint}/chat/completions";
                }
                body = b;
                break;
            }

            case AiWireFormat.Anthropic:
            {
                string sys = string.Join("\n", messages.Where(m => m.Role == ChatMessage.System).Select(m => m.Content));
                var b = new Dictionary<string, object?>
                {
                    ["model"] = model,
                    ["max_tokens"] = maxTokens,
                    ["stream"] = stream,
                    ["messages"] = messages.Where(m => m.Role != ChatMessage.System)
                        .Select(m => new Dictionary<string, object?>
                        {
                            ["role"] = m.Role == ChatMessage.Assistant ? "assistant" : "user",
                            ["content"] = m.Content,
                        }).ToList(),
                };
                if (sys.Length > 0) b["system"] = sys;
                body = b;
                url = $"{endpoint}/v1/messages";
                break;
            }

            case AiWireFormat.Gemini:
            {
                var b = new Dictionary<string, object?>
                {
                    ["contents"] = messages.Where(m => m.Role != ChatMessage.System)
                        .Select(m => new Dictionary<string, object?>
                        {
                            ["role"] = m.Role == ChatMessage.Assistant ? "model" : "user",
                            ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = m.Content } },
                        }).ToList(),
                    ["generationConfig"] = new Dictionary<string, object?> { ["maxOutputTokens"] = maxTokens },
                };
                string sys = string.Join("\n", messages.Where(m => m.Role == ChatMessage.System).Select(m => m.Content));
                if (sys.Length > 0)
                    b["systemInstruction"] = new Dictionary<string, object?> { ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = sys } } };
                string method = stream ? "streamGenerateContent" : "generateContent";
                string q = stream ? "alt=sse&" : "";
                url = $"{endpoint}/v1beta/models/{Uri.EscapeDataString(model)}:{method}?{q}key={Uri.EscapeDataString(key)}";
                body = b;
                break;
            }

            case AiWireFormat.Ollama:
            default:
            {
                body = new Dictionary<string, object?>
                {
                    ["model"] = model,
                    ["messages"] = messages.Select(m => new Dictionary<string, object?> { ["role"] = m.Role, ["content"] = m.Content }).ToList(),
                    ["stream"] = stream,
                    ["options"] = new Dictionary<string, object?> { ["num_predict"] = maxTokens },
                };
                url = $"{endpoint}/api/chat";
                break;
            }
        }

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        switch (info.Wire)
        {
            case AiWireFormat.OpenAiChat:
                if (_provider == AiProvider.AzureOpenAI) req.Headers.TryAddWithoutValidation("api-key", key);
                else req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                break;
            case AiWireFormat.Anthropic:
                req.Headers.TryAddWithoutValidation("x-api-key", key);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                break;
            case AiWireFormat.Gemini:  // key travels in the query string
            case AiWireFormat.Ollama:  // local server, no auth
                break;
        }
        return req;
    }

    // ---- Response parsing ----

    private static async Task EnsureSuccess(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string detail = "";
        try { detail = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
        if (detail.Length > 600) detail = detail.Substring(0, 600) + "…";
        throw new AiException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}".Trim());
    }

    private static string ExtractContent(AiWireFormat wire, string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        switch (wire)
        {
            case AiWireFormat.OpenAiChat:
                return root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            case AiWireFormat.Anthropic:
                foreach (var block in root.GetProperty("content").EnumerateArray())
                    if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                        return block.GetProperty("text").GetString() ?? "";
                return "";
            case AiWireFormat.Gemini:
                return GeminiText(root) ?? "";
            case AiWireFormat.Ollama:
                return root.GetProperty("message").GetProperty("content").GetString() ?? "";
            default:
                return "";
        }
    }

    private static string? ExtractStreamToken(AiWireFormat wire, string payload, ref bool done)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        switch (wire)
        {
            case AiWireFormat.OpenAiChat:
                if (root.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0)
                {
                    var c0 = ch[0];
                    if (c0.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                        return content.GetString();
                }
                return null;

            case AiWireFormat.Anthropic:
                var type = root.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                if (type == "content_block_delta" && root.TryGetProperty("delta", out var d) &&
                    d.TryGetProperty("text", out var txt))
                    return txt.GetString();
                if (type == "message_stop") done = true;
                return null;

            case AiWireFormat.Gemini:
                return GeminiText(root);

            case AiWireFormat.Ollama:
                if (root.TryGetProperty("done", out var dn) && dn.ValueKind == JsonValueKind.True) done = true;
                if (root.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var mc))
                    return mc.GetString();
                return null;

            default:
                return null;
        }
    }

    private static string? GeminiText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0) return null;
        if (!cands[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
            return null;
        var sb = new StringBuilder();
        foreach (var p in parts.EnumerateArray())
            if (p.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        return sb.Length == 0 ? null : sb.ToString();
    }
}
