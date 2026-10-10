using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AskTheAlmanac;

/// <summary>Tools the model can call to look things up on the official Stardew Valley wiki.</summary>
internal sealed class WikiTools
{
    private const string ApiUrl = "https://stardewvalleywiki.com/mediawiki/api.php";
    private const int MaxPageChars = 8000;

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>Tool definitions in the format Ollama's /api/chat expects.</summary>
    public static JsonArray Definitions() => new()
    {
        Tool(
            "search_wiki",
            "Search the Stardew Valley wiki and get matching page titles. The search requires ALL words to match, "
            + "so use a short topic name such as \"Coop\", \"Blueberry\", \"Abigail\" or \"Iridium Sprinkler\" rather than a question.",
            new JsonObject
            {
                ["query"] = Param("Short topic name to search for, e.g. \"Coop\"."),
            },
            "query"
        ),
        Tool(
            "read_wiki_page",
            "Read a Stardew Valley wiki page by its exact title (as returned by search_wiki). "
            + "Long pages are truncated; pass a section name to read a specific part, e.g. \"Gifts\" or \"Schedule\".",
            new JsonObject
            {
                ["title"] = Param("Exact page title, e.g. \"Coop\"."),
                ["section"] = Param("Optional section heading to read, e.g. \"Gifts\"."),
            },
            "title"
        ),
    };

    /// <summary>Run a tool call and return the text to send back to the model.</summary>
    public async Task<string> RunAsync(string name, JsonObject args, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "search_wiki" => await SearchAsync(GetString(args, "query"), ct),
                "read_wiki_page" => await ReadPageAsync(GetString(args, "title"), GetString(args, "section"), ct),
                _ => $"Error: unknown tool '{name}'.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"Error: couldn't reach the wiki ({ex.Message}).";
        }
    }

    /// <summary>Human-readable description of a tool call, shown in the chat window while it runs.</summary>
    public static string Describe(string name, JsonObject args) => name switch
    {
        "search_wiki" => $"Searching the wiki for \"{GetString(args, "query")}\"",
        "read_wiki_page" => $"Reading the wiki page \"{GetString(args, "title")}\"",
        _ => "Checking the wiki",
    };

    private static async Task<string> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query is required.";

        var results = await SearchOnceAsync(query, ct);

        // The wiki ANDs search terms, so a multi-word miss often hits on the individual words.
        if (results.Count == 0)
        {
            foreach (string word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).OrderByDescending(w => w.Length))
            {
                foreach (var hit in await SearchOnceAsync(word, ct))
                {
                    if (results.All(r => r.Title != hit.Title))
                        results.Add(hit);
                }
                if (results.Count >= 5)
                    break;
            }
        }

        if (results.Count == 0)
            return $"No wiki pages found for \"{query}\". Try a different or shorter name.";

        var sb = new StringBuilder();
        foreach (var (title, snippet) in results.Take(5))
            sb.Append("- ").Append(title).Append(": ").AppendLine(snippet);
        return sb.ToString();
    }

    private static async Task<List<(string Title, string Snippet)>> SearchOnceAsync(string query, CancellationToken ct)
    {
        string url = $"{ApiUrl}?action=query&list=search&srlimit=5&format=json&srsearch={Uri.EscapeDataString(query)}";
        var json = JsonNode.Parse(await Http.GetStringAsync(url, ct));

        var results = new List<(string, string)>();
        if (json?["query"]?["search"] is JsonArray hits)
        {
            foreach (var hit in hits)
            {
                string title = hit?["title"]?.GetValue<string>() ?? "";
                string snippet = hit?["snippet"]?.GetValue<string>() ?? "";
                snippet = WebUtility.HtmlDecode(Regex.Replace(snippet, "<.*?>", ""));
                snippet = Regex.Replace(CleanWikitext(snippet), @"\s+", " ").Trim();
                if (title.Length > 0)
                    results.Add((title, snippet));
            }
        }
        return results;
    }

    private static async Task<string> ReadPageAsync(string title, string section, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Error: title is required.";

        string url = $"{ApiUrl}?action=parse&prop=wikitext&redirects=1&format=json&formatversion=2&page={Uri.EscapeDataString(title)}";
        var json = JsonNode.Parse(await Http.GetStringAsync(url, ct));

        string? wikitext = json?["parse"]?["wikitext"]?.GetValue<string>();
        if (wikitext is null)
            return $"No wiki page titled \"{title}\". Use search_wiki to find the exact title.";

        string text = CleanWikitext(wikitext);
        var headings = Regex.Matches(text, @"^(={2,})\s*(.+?)\s*\1\s*$", RegexOptions.Multiline);

        if (!string.IsNullOrWhiteSpace(section))
        {
            var match = headings.FirstOrDefault(h => h.Groups[2].Value.Equals(section, StringComparison.OrdinalIgnoreCase))
                ?? headings.FirstOrDefault(h => h.Groups[2].Value.Contains(section, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return $"Page \"{title}\" has no section \"{section}\". Sections: {string.Join(", ", headings.Select(h => h.Groups[2].Value))}";

            int level = match.Groups[1].Length;
            var next = headings.FirstOrDefault(h => h.Index > match.Index && h.Groups[1].Length <= level);
            text = text[match.Index..(next?.Index ?? text.Length)];
        }

        if (text.Length > MaxPageChars)
        {
            text = text[..MaxPageChars] + "\n[...truncated]";
            if (string.IsNullOrWhiteSpace(section) && headings.Count > 0)
                text += $"\nThis page has more sections, readable with the section parameter: {string.Join(", ", headings.Select(h => h.Groups[2].Value))}";
        }

        return text;
    }

    /// <summary>Strip wikitext markup that only costs tokens, and expand the templates that carry useful data.</summary>
    private static string CleanWikitext(string text)
    {
        text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
        text = Regex.Replace(text, "<ref[^>]*?/>|<ref.*?</ref>", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"\{\{Price\|([\d,]+)[^}]*\}\}", m => $"{FormatNumber(m.Groups[1].Value)}g");
        text = Regex.Replace(text, @"\{\{Name\|([^|}]+)\|([^|}]+)[^}]*\}\}", "$1 x$2");
        text = Regex.Replace(text, @"\{\{(?:Name|Season|Villager|NPC)\|([^|}]+)\}\}", "$1");
        text = Regex.Replace(text, @"\[\[(?:File|Image):[^\]]*\]\]", "");
        text = Regex.Replace(text, @"\[\[[^\]|]*\|([^\]]*)\]\]", "$1");
        text = Regex.Replace(text, @"\[\[([^\]]*)\]\]", "$1");
        text = Regex.Replace(text, @"\s*(?:style|id|class|colspan|rowspan|width|align)=""[^""]*""", "");
        text = Regex.Replace(text, "</?(?:div|span|br|center|small|big)[^>]*>", " ");
        text = text.Replace("'''", "").Replace("''", "");
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"(\s*\n){3,}", "\n\n");
        return text.Trim();
    }

    private static string FormatNumber(string digits) =>
        long.TryParse(digits.Replace(",", ""), out long n) ? n.ToString("N0") : digits;

    private static string GetString(JsonObject args, string key) =>
        args[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(required.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            },
        },
    };

    private static JsonObject Param(string description) => new() { ["type"] = "string", ["description"] = description };

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // MediaWiki etiquette asks for a user agent with contact details.
        var version = typeof(WikiTools).Assembly.GetName().Version;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AskTheAlmanac/{version?.ToString(3) ?? "1.0"} (+https://github.com/mpsecco/ask-the-almanac; Stardew Valley SMAPI mod)");
        return client;
    }
}
