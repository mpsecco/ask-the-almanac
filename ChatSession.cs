using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using StardewModdingAPI;

namespace AskTheAlmanac;

internal enum Speaker { You, Almanac, Error, Info }

internal sealed record ChatLine(Speaker Speaker, string Text);

/// <summary>The conversation shown in the chat window. Lives across menu openings so history and in-flight answers aren't lost.</summary>
internal sealed class ChatSession
{
    /// <summary>Number of most recent exchanges never compacted, so follow-ups like "and the big one?" still work.</summary>
    private const int KeepExchanges = 2;

    private const string SummaryPrompt =
        """
        You maintain the memory of a chat between a Stardew Valley player and their in-game guide, the Almanac.
        Merge the existing summary and the new exchanges into one updated summary of at most 150 words, written as short "- " bullet points.
        Keep the player's goals, plans and decisions, and specific facts already given (prices, dates, names, quantities). Drop small talk and anything superseded.
        Reply with only the summary.
        """;

    private const string SystemPrompt =
        """
        You are the Almanac, a warm, well-read guide to Stardew Valley (version 1.6). The player reads your answers in a small in-game window while their game is paused, so every sentence should earn its place.

        Getting facts right:
        - Look up any specific fact (prices, materials, recipes, gift tastes, schedules, growth times, drop rates, locations) with search_wiki and read_wiki_page before answering. Never guess numbers.
        - Only state things you read on the wiki or are certain of. If you're unsure about a side detail, leave it out rather than risk a wrong tip.
        - Wiki search needs every word to match, so search short topic names like "Coop", "Blueberry" or "Abigail", not whole questions.

        How to write:
        - Answer the actual question in your first sentence. Keep the whole answer under about 60 words (two or three short sentences, or a sentence and a short list), unless the player asks for more detail.
        - Write in natural, friendly sentences, the way a villager would tell you over the counter. Headings, tables, bold and other markdown show up as raw text in the window, so never use them.
        - A short list is fine when it genuinely reads better than a sentence, such as step-by-step instructions, a schedule with several stops, or comparing a few options. Use "- " for each item, keep each to one line, and don't repeat the list's contents in the surrounding sentences. For two or three items, a sentence is better ("She loves Amethyst, Pufferfish and Spicy Eel").
        - When there are many items, pick the ones that matter most rather than reciting every one.
        - Say each fact once. Don't restate the answer, summarize what you just said, or repeat things the player already knows from earlier in the chat.
        - Skip extra tips, trivia and "worth knowing" asides. If something is a natural next question, offer it as a follow-up instead of answering it unasked.
        - Use the player's game state when it changes the advice, e.g. what they can afford, today's date and weather, or where they are. Don't recite it back to them otherwise.
        - Write money like 4,000g.

        Example of a good answer to "how much is a coop?" when the player has 3,200g:
        A Coop costs 4,000g plus 300 Wood and 100 Stone, and Robin will have it ready in three days. You're about 800g short, so a couple of days of foraging or fishing should get you there.
        >> How much do chickens cost?
        >> Where can I get lots of wood?
        >> What does the Big Coop add?

        Follow-ups (always required):
        Every reply must end with 2 to 4 short questions the player might want to ask next, each on its own line starting with ">> ". The game turns these lines into buttons, so this is the one place the line format is expected. Keep each under 8 words, phrase it as the player would ask it, and don't suggest anything you already answered.
        """;

    private const int MaxSuggestions = 4;

    private readonly OllamaClient client;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly Action saveConfig;

    /// <summary>Cancels the in-flight request on <see cref="Clear"/>.</summary>
    private CancellationTokenSource? requestCancel;

    /// <summary>Recent exchanges sent to the model word-for-word, as alternating user/assistant messages.</summary>
    private readonly List<(string Role, string Content)> history = new();

    /// <summary>Summary of older exchanges that were compacted out of <see cref="history"/>.</summary>
    private string? summary;

    /// <summary>Whether a background summary is in progress.</summary>
    private bool compacting;

    /// <summary>Incremented on <see cref="Clear"/>, so requests and compactions started before it are discarded.</summary>
    private int generation;

    /// <summary>Updates from background requests, applied on the game thread by <see cref="Update"/>.</summary>
    private readonly ConcurrentQueue<Action> pending = new();

    public List<ChatLine> Lines { get; } = new();
    public bool IsBusy { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Suggested follow-up questions for the latest answer.</summary>
    public List<string> Suggestions { get; } = new();

    /// <summary>Incremented whenever anything visible changes, so the menu knows to re-layout.</summary>
    public int Version { get; private set; }

    public ChatSession(OllamaClient client, IMonitor monitor, ModConfig config, Action saveConfig)
    {
        this.client = client;
        this.monitor = monitor;
        this.config = config;
        this.saveConfig = saveConfig;
    }

    /// <summary>How much past conversation to keep word-for-word before compacting.</summary>
    private int HistoryTokens => Math.Min(config.HistoryTokens, client.ContextTokens / 2);

    /// <summary>Send a question. Must be called on the game thread.</summary>
    public void Ask(string question, string? gameContext)
    {
        if (IsBusy)
            return;

        Lines.Add(new ChatLine(Speaker.You, question));
        Suggestions.Clear();
        IsBusy = true;
        Status = "Thinking";
        Version++;

        JsonArray messages = BuildMessages(question, gameContext, summary, history);
        int startGeneration = generation;
        requestCancel = new CancellationTokenSource();
        CancellationToken ct = requestCancel.Token;
        Task.Run(async () =>
        {
            // drop updates from a request that was cleared while it was running
            void Post(Action action) => pending.Enqueue(() =>
            {
                if (generation == startGeneration)
                    action();
            });

            try
            {
                string reply = await client.ChatAsync(messages, status => Post(() => Status = status), ct);
                var (answer, suggestions) = ParseReply(reply);
                Post(() =>
                {
                    Suggestions.AddRange(suggestions);
                    history.Add(("user", question));
                    history.Add(("assistant", answer));
                    Finish(new ChatLine(Speaker.Almanac, answer));
                    CompactIfNeeded();
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // cleared by the player
            }
            catch (Exception ex)
            {
                monitor.Log($"Request failed: {ex}", LogLevel.Warn);
                Post(() => Finish(new ChatLine(Speaker.Error, DescribeError(ex))));
            }
        });
    }

    /// <summary>Run a slash command like "/clear". Returns the command name, or null if the input isn't a command.</summary>
    public string? RunCommand(string input)
    {
        if (!input.StartsWith('/'))
            return null;

        string[] parts = input[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string name = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        string arg = parts.Length > 1 ? parts[1] : "";

        switch (name)
        {
            case "clear":
                Clear();
                Info("Fresh page! I've forgotten our chat, so the next question starts with an empty context.");
                break;

            case "model":
                if (arg.Length == 0)
                    Info($"Using {config.Model}. Switch with /model <name>, e.g. /model glm-5.3:cloud.");
                else
                {
                    config.Model = arg;
                    saveConfig();
                    Info($"Switched to {arg}. Our chat history carries over.");
                }
                break;

            case "context":
                int historyUsed = history.Sum(h => OllamaClient.EstimateTokens(h.Content));
                Info(
                    $"Model: {config.Model}\n"
                    + $"Remembered word-for-word: {history.Count / 2} exchanges, about {historyUsed:N0} of {HistoryTokens:N0} tokens\n"
                    + $"Older chat: {(summary is null ? "nothing summarized yet" : $"summarized in about {OllamaClient.EstimateTokens(summary):N0} tokens")}\n"
                    + $"Context window: {client.ContextTokens:N0} tokens ({(client.IsCloud ? "cloud" : "local")})"
                );
                break;

            case "help":
                Info(
                    "Commands:\n"
                    + "/clear - forget the chat and start fresh\n"
                    + "/model [name] - show or switch the AI model\n"
                    + "/context - show how much of the conversation I'm remembering\n"
                    + "/help - show this list"
                );
                break;

            default:
                Info($"I don't know /{name}. Type /help for the list of commands.");
                break;
        }
        return name;
    }

    /// <summary>Apply background updates. Must be called on the game thread each tick.</summary>
    public void Update()
    {
        while (pending.TryDequeue(out var action))
        {
            action();
            Version++;
        }
    }

    public void Clear()
    {
        requestCancel?.Cancel();
        requestCancel = null;
        Lines.Clear();
        history.Clear();
        Suggestions.Clear();
        summary = null;
        IsBusy = false;
        Status = null;
        generation++;
        Version++;
    }

    private void Info(string text)
    {
        Lines.Add(new ChatLine(Speaker.Info, text));
        Version++;
    }

    /// <summary>Build the message list for a question: system prompt (with game state and summary), recent exchanges, then the question.</summary>
    public static JsonArray BuildMessages(string question, string? gameContext, string? summary, IEnumerable<(string Role, string Content)> history)
    {
        string system = SystemPrompt;
        if (gameContext != null)
            system += $"\nPlayer's current game state:\n{gameContext}";
        if (summary != null)
            system += $"\nSummary of earlier conversation with this player:\n{summary}\n";

        var messages = new JsonArray { Message("system", system) };
        foreach (var (role, content) in history)
            messages.Add(Message(role, content));
        // The reminder only goes in the outgoing message, not the stored history, so it doesn't pile up.
        messages.Add(Message("user", $"{question}\n\n(Remember to end with 2-4 \">> \" follow-up questions.)"));
        return messages;
    }

    /// <summary>Split the model's reply into the answer and its "&gt;&gt; " follow-up question lines.</summary>
    public static (string Answer, List<string> Suggestions) ParseReply(string reply)
    {
        var answer = new List<string>();
        var suggestions = new List<string>();
        // models sometimes glue the first ">> " onto the end of the answer, so split on it anywhere
        reply = Regex.Replace(reply, @"[ \t]*>>", "\n>>");
        foreach (string line in reply.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(">>"))
            {
                string suggestion = trimmed[2..].Trim().Trim('"', '*', ' ');
                if (suggestion.Length > 0 && suggestions.Count < MaxSuggestions)
                    suggestions.Add(suggestion);
            }
            else
                answer.Add(line);
        }

        string text = string.Join('\n', answer).Trim();
        return (text.Length > 0 ? text : reply.Trim(), suggestions);
    }

    /// <summary>
    /// If the history is over budget, summarize the oldest exchanges in the background, then replace them with the summary.
    /// The chat window still shows everything; this only affects what's sent to the model.
    /// </summary>
    private void CompactIfNeeded()
    {
        int total = history.Sum(h => OllamaClient.EstimateTokens(h.Content));
        if (compacting || total <= HistoryTokens)
            return;

        // drop the oldest exchanges until the rest fits in half the budget, leaving room to grow before the next compaction
        int drop = 0;
        while (drop + KeepExchanges * 2 < history.Count && total > HistoryTokens / 2)
        {
            total -= OllamaClient.EstimateTokens(history[drop].Content) + OllamaClient.EstimateTokens(history[drop + 1].Content);
            drop += 2;
        }
        if (drop == 0)
            return;

        var transcript = new StringBuilder();
        foreach (var (role, content) in history.Take(drop))
            transcript.Append(role == "user" ? "Player: " : "Almanac: ").AppendLine(content);
        var messages = new JsonArray
        {
            Message("system", SummaryPrompt),
            Message("user", $"Existing summary:\n{summary ?? "(none)"}\n\nNew exchanges:\n{transcript}"),
        };

        string? oldSummary = summary;
        int startGeneration = generation;
        compacting = true;
        Task.Run(async () =>
        {
            string? newSummary;
            try
            {
                newSummary = await client.CompleteAsync(messages);
            }
            catch (Exception ex)
            {
                // fall back to plain pruning: the exchanges are dropped without being summarized
                monitor.Log($"Couldn't summarize older chat, dropping it instead: {ex.Message}", LogLevel.Warn);
                newSummary = oldSummary;
            }

            pending.Enqueue(() =>
            {
                compacting = false;
                if (generation != startGeneration)
                    return;

                // only appends happen while compacting, so the dropped exchanges are still at the front
                history.RemoveRange(0, drop);
                summary = string.IsNullOrWhiteSpace(newSummary) ? oldSummary : newSummary;
                monitor.Log($"Compacted {drop / 2} older exchanges into a summary:\n{summary}", LogLevel.Trace);
            });
        });
    }

    private void Finish(ChatLine line)
    {
        Lines.Add(line);
        IsBusy = false;
        Status = null;
    }

    private string DescribeError(Exception ex) => ex switch
    {
        OllamaException => ex.Message,
        HttpRequestException => $"I couldn't reach Ollama at {client.Url}. Is it running?",
        TaskCanceledException => "That took too long, so I gave up. Try again?",
        _ => $"Something went wrong: {ex.Message}",
    };

    private static JsonObject Message(string role, string content) => new() { ["role"] = role, ["content"] = content };
}
