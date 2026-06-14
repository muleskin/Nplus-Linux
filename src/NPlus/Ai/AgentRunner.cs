using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NPlus.Scripting;

namespace NPlus.Ai;

/// <summary>One tool action the model asked for, e.g. {op:"replace_selection", text:"…"}.</summary>
public sealed class AgentAction
{
    public string Op { get; set; } = "";
    public string? Text { get; set; }
    public List<string>? Lines { get; set; }

    public bool IsRead => Op is "get_text" or "get_selection" or "get_info";
    public bool IsWrite => Op is "set_text" or "replace_selection" or "set_lines" or "insert";
}

/// <summary>
/// Drives a provider-agnostic agent loop over the active editor tab. The model replies with a single
/// JSON object (<c>{message, actions[], done}</c>); read actions run automatically and their results
/// are fed back as an observation, while write actions are handed to a confirm-and-apply callback so
/// the user previews every change before it touches the buffer. No provider tool-calling API is used,
/// so it behaves identically across all backends.
/// </summary>
public sealed class AgentRunner
{
    public const string SystemPrompt =
        "You are an agent operating inside the n+ text editor. You can inspect and edit the text in the " +
        "user's ACTIVE tab by replying with a SINGLE JSON object and nothing else (no markdown, no prose " +
        "outside the JSON).\n\n" +
        "Shape: {\"message\": \"<short note for the user>\", \"actions\": [ {\"op\": \"...\", ...} ], \"done\": <true|false>}\n\n" +
        "Read actions (results are returned to you as an OBSERVATION, then you continue):\n" +
        "  {\"op\":\"get_text\"}        full document text\n" +
        "  {\"op\":\"get_selection\"}   currently selected text\n" +
        "  {\"op\":\"get_info\"}        file path, language, line count, caret line, selection length\n\n" +
        "Write actions (shown to the user for confirmation before they are applied):\n" +
        "  {\"op\":\"set_text\",\"text\":\"...\"}            replace the ENTIRE document\n" +
        "  {\"op\":\"replace_selection\",\"text\":\"...\"}   replace the selection (insert at caret if none)\n" +
        "  {\"op\":\"set_lines\",\"lines\":[\"...\"]}        replace the whole document with these lines\n" +
        "  {\"op\":\"insert\",\"text\":\"...\"}              insert at the caret\n\n" +
        "Rules:\n" +
        "- Read what you need first; act only on what the document actually contains.\n" +
        "- Keep actions minimal. You may return several actions in one turn.\n" +
        "- When the task is finished, reply with \"done\": true, a summary in \"message\", and an empty actions array.\n" +
        "- Reply with the JSON object only.";

    private const int DefaultMaxSteps = 8;

    private readonly AiClient _client;
    private readonly ScriptContext _editor;
    private readonly Func<IReadOnlyList<AgentAction>, Task<bool>> _applyWrites;
    private readonly Action<string> _onAssistantMessage;
    private readonly Action<string> _onStatus;
    private readonly int _maxSteps;

    public AgentRunner(
        AiClient client,
        ScriptContext editor,
        Func<IReadOnlyList<AgentAction>, Task<bool>> applyWrites,
        Action<string> onAssistantMessage,
        Action<string> onStatus,
        int maxSteps = DefaultMaxSteps)
    {
        _client = client;
        _editor = editor;
        _applyWrites = applyWrites;
        _onAssistantMessage = onAssistantMessage;
        _onStatus = onStatus;
        _maxSteps = maxSteps;
    }

    /// <summary>Runs the loop. <paramref name="conversation"/> must already end with the user's task.</summary>
    /// <returns>The final assistant message (for adding to the visible chat history).</returns>
    public async Task<string> RunAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken ct)
    {
        var messages = new List<ChatMessage> { new(ChatMessage.System, SystemPrompt) };
        messages.AddRange(conversation);

        string lastMessage = "";
        bool nudged = false;

        for (int step = 0; step < _maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            string raw = await _client.CompleteAsync(messages, ct);
            messages.Add(new ChatMessage(ChatMessage.Assistant, raw));

            if (!TryParseTurn(raw, out var turn))
            {
                if (nudged) { _onAssistantMessage(raw); return raw; } // give up parsing; show whatever it said
                nudged = true;
                messages.Add(new ChatMessage(ChatMessage.User,
                    "That was not a single valid JSON object. Reply again with ONLY the JSON object described in the system message."));
                continue;
            }
            nudged = false;

            if (!string.IsNullOrWhiteSpace(turn.Message))
            {
                lastMessage = turn.Message!;
                _onAssistantMessage(turn.Message!);
            }

            var actions = turn.Actions ?? new List<AgentAction>();
            if (actions.Count == 0)
                return lastMessage; // done (with or without an explicit done flag)

            var writes = actions.Where(a => a.IsWrite).ToList();
            var reads = actions.Where(a => a.IsRead).ToList();

            var applied = new List<string>();
            var declined = new List<string>();
            if (writes.Count > 0)
            {
                _onStatus($"Proposing {writes.Count} edit(s)…");
                bool ok = await _applyWrites(writes);
                if (ok) applied.AddRange(writes.Select(w => w.Op));
                else declined.AddRange(writes.Select(w => w.Op));
                _onStatus(ok ? "Applied edits." : "Edits declined by user.");
            }

            var observations = new List<object>();
            foreach (var r in reads)
            {
                _onStatus($"Reading: {r.Op}");
                observations.Add(new { op = r.Op, result = ExecuteRead(r.Op) });
            }

            // Continue only when the model has something new to react to: results from a read,
            // a rejected edit it should revise, or an unfinished task. A done turn whose edits
            // were all applied is finished — no extra round-trip.
            bool needFollowup = reads.Count > 0 || declined.Count > 0 || !turn.Done;
            if (!needFollowup)
                return lastMessage;

            var feedback = JsonSerializer.Serialize(new { observations, applied, declined });
            messages.Add(new ChatMessage(ChatMessage.User, "OBSERVATION:\n" + feedback));
        }

        _onStatus($"Reached the {_maxSteps}-step limit.");
        return lastMessage;
    }

    private object ExecuteRead(string op) => op switch
    {
        "get_text" => _editor.GetText(),
        "get_selection" => _editor.GetSelection(),
        "get_info" => new
        {
            file_path = _editor.FilePath(),
            language = _editor.Language(),
            line_count = _editor.LineCount(),
            caret_line = _editor.CaretLine(),
        },
        _ => "",
    };

    // ---- Parsing ----

    private sealed class TurnDto
    {
        public string? message { get; set; }
        public bool done { get; set; }
        public List<ActionDto>? actions { get; set; }
    }

    private sealed class ActionDto
    {
        public string? op { get; set; }
        public string? text { get; set; }
        public List<string>? lines { get; set; }
    }

    private static readonly JsonSerializerOptions ParseOpts = new() { PropertyNameCaseInsensitive = true };

    private static bool TryParseTurn(string raw, out AgentTurn turn)
    {
        turn = new AgentTurn();
        string? json = ExtractJsonObject(raw);
        if (json == null) return false;
        try
        {
            var dto = JsonSerializer.Deserialize<TurnDto>(json, ParseOpts);
            if (dto == null) return false;
            turn = new AgentTurn
            {
                Message = dto.message,
                Done = dto.done,
                Actions = (dto.actions ?? new List<ActionDto>())
                    .Where(a => !string.IsNullOrWhiteSpace(a.op))
                    .Select(a => new AgentAction { Op = a.op!.Trim(), Text = a.text, Lines = a.lines })
                    .ToList(),
            };
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Pulls the JSON object out of a reply that may be fenced or wrapped in prose.</summary>
    private static string? ExtractJsonObject(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        // Strip a leading ``` / ```json fence if present.
        int fence = s.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            int nl = s.IndexOf('\n', fence);
            int close = nl >= 0 ? s.IndexOf("```", nl + 1, StringComparison.Ordinal) : -1;
            if (nl >= 0 && close > nl) s = s.Substring(nl + 1, close - nl - 1);
        }

        int open = s.IndexOf('{');
        int last = s.LastIndexOf('}');
        if (open >= 0 && last > open) return s.Substring(open, last - open + 1);
        return null;
    }

    private sealed class AgentTurn
    {
        public string? Message { get; set; }
        public bool Done { get; set; }
        public List<AgentAction> Actions { get; set; } = new();
    }
}
