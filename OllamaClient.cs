using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace AskTheAlmanac;

/// <summary>An error reported by the Ollama server, worded for the player.</summary>
internal sealed class OllamaException : Exception
{
    public OllamaException(string message) : base(message) { }
}

/// <summary>Talks to Ollama's /api/chat endpoint, running wiki tool calls until the model gives an answer.</summary>
internal sealed class OllamaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>Tokens kept free for the model's reply and the tool definitions when pruning the prompt.</summary>
    private const int ReservedTokens = 3000;

    private const string PrunedToolResult = "[Earlier wiki result removed to save space. Look it up again if needed.]";

    private readonly ModConfig config;
    private readonly WikiTools wiki = new();
    private readonly Action<string> log;

    public OllamaClient(ModConfig config, Action<string> log)
    {
        this.config = config;
        this.log = log;
    }

    public string Url => config.OllamaUrl;

    /// <summary>Whether the model runs on Ollama Cloud, either through the local daemon or directly.</summary>
    public bool IsCloud =>
        config.Model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase)
        || config.OllamaUrl.Contains("ollama.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>The context window in effect for the current model.</summary>
    public int ContextTokens => IsCloud ? config.ContextTokens : Math.Min(config.ContextTokens, config.LocalContextTokens);

    /// <summary>Get an answer for a conversation. Tool call messages are appended to <paramref name="messages"/>.</summary>
    /// <param name="messages">System, history and user messages.</param>
    /// <param name="onStatus">Called (from a background thread) with a short description of what's happening.</param>
    public async Task<string> ChatAsync(JsonArray messages, Action<string> onStatus, CancellationToken ct = default)
    {
        for (int round = 0; ; round++)
        {
            // After the last allowed lookup round, ask again without tools so the model has to answer.
            bool allowTools = round < config.MaxToolRounds;
            PruneToolResults(messages);
            JsonObject message = await SendAsync(messages, allowTools, ct);

            if (!allowTools || message["tool_calls"] is not JsonArray calls || calls.Count == 0)
            {
                string content = GetContent(message);
                if (content.Length == 0)
                {
                    // Models occasionally reply with only reasoning; nudge once before giving up.
                    log("Model returned an empty answer; asking again.");
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = "Please give your answer now, based on what you've found so far." });
                    content = GetContent(await SendAsync(messages, allowTools: false, ct));
                }
                return content.Length > 0 ? content : throw new OllamaException("Hmm, I couldn't come up with an answer to that one. Try asking again?");
            }

            messages.Add(message);
            foreach (var call in calls)
            {
                string name = call?["function"]?["name"]?.GetValue<string>() ?? "";
                JsonObject args = ParseArguments(call?["function"]?["arguments"]);

                onStatus(WikiTools.Describe(name, args));
                string result = await wiki.RunAsync(name, args, ct);
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_name"] = name, ["content"] = result });
            }
            onStatus("Thinking");
        }
    }

    /// <summary>Get a single reply without tools, e.g. for summarizing.</summary>
    public async Task<string> CompleteAsync(JsonArray messages, CancellationToken ct = default) =>
        GetContent(await SendAsync(messages, allowTools: false, ct));

    /// <summary>Rough token count for budgeting. Errs high, since wikitext tokenizes worse than plain English.</summary>
    public static int EstimateTokens(string text) => text.Length * 3 / 10 + 4;

    /// <summary>If the prompt would overflow the context window, replace the oldest wiki results with a placeholder.</summary>
    private void PruneToolResults(JsonArray messages)
    {
        int budget = ContextTokens - ReservedTokens;
        int total = messages.Sum(m => EstimateTokens(m?["content"]?.GetValue<string>() ?? ""));

        // never prune the newest tool results, which the model hasn't seen yet
        int lastAssistant = messages.Select((m, i) => (m, i)).LastOrDefault(x => x.m?["role"]?.GetValue<string>() == "assistant").i;
        for (int i = 0; i < lastAssistant && total > budget; i++)
        {
            if (messages[i] is not JsonObject message || message["role"]?.GetValue<string>() != "tool")
                continue;

            string content = message["content"]?.GetValue<string>() ?? "";
            if (content == PrunedToolResult)
                continue;

            total -= EstimateTokens(content) - EstimateTokens(PrunedToolResult);
            message["content"] = PrunedToolResult;
            log($"Pruned a {EstimateTokens(content)}-token wiki result to fit the {ContextTokens}-token context.");
        }
    }

    private static string GetContent(JsonObject message)
    {
        string content = message["content"]?.GetValue<string>() ?? "";

        // Some models leak their reasoning into the content, ending it with </think>.
        int thinkEnd = content.LastIndexOf("</think>", StringComparison.Ordinal);
        if (thinkEnd >= 0)
            content = content[(thinkEnd + "</think>".Length)..];
        return content.Trim();
    }

    private async Task<JsonObject> SendAsync(JsonArray messages, bool allowTools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["stream"] = false,
        };

        // Local models allocate memory for the whole window; cloud models ignore num_ctx and use their full context.
        if (!IsCloud)
            body["options"] = new JsonObject { ["num_ctx"] = ContextTokens };
        if (allowTools)
            body["tools"] = WikiTools.Definitions();

        // Attach the shared message list only while serializing, since a JSON node can only have one parent.
        body["messages"] = messages;
        string json = body.ToJsonString();
        body.Remove("messages");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.OllamaUrl.TrimEnd('/')}/api/chat")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = await Http.SendAsync(request, ct);
        string text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            string error = TryParse(text)?["error"]?.GetValue<string>() ?? text;
            throw new OllamaException(DescribeError((int)response.StatusCode, error));
        }

        var reply = TryParse(text);
        if (reply?["message"] is not JsonObject message)
            throw new OllamaException("Ollama sent back a reply I couldn't read.");
        if (reply["prompt_eval_count"] is JsonValue promptTokens)
            log($"Prompt used {promptTokens} tokens (estimated {EstimateTokens(json)}).");
        reply.Remove("message");
        return message;
    }

    private string DescribeError(int status, string error)
    {
        if (status == 404 || error.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return $"Model \"{config.Model}\" isn't available. Try `ollama pull {config.Model}`.";
        if (status is 401 or 403)
            return "Ollama Cloud refused the request. Run `ollama signin`, or check ApiKey in config.json.";
        return $"Ollama error ({status}): {error}";
    }

    private static JsonObject ParseArguments(JsonNode? node) => node switch
    {
        JsonObject obj => obj,
        JsonValue value when value.TryGetValue(out string? s) => TryParse(s) ?? new JsonObject(),
        _ => new JsonObject(),
    };

    private static JsonObject? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
