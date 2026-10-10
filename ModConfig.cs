using StardewModdingAPI.Utilities;

namespace AskTheAlmanac;

internal sealed class ModConfig
{
    /// <summary>Key that opens and closes the chat window.</summary>
    public KeybindList ToggleKey { get; set; } = KeybindList.Parse("F2");

    /// <summary>Ollama server. Use https://ollama.com (with <see cref="ApiKey"/>) to call Ollama Cloud directly.</summary>
    public string OllamaUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model name, e.g. "glm-5.3-flash:cloud" or "glm-5.3:cloud" through the local daemon, or "qwen3:14b" for a local model.</summary>
    public string Model { get; set; } = "glm-5.3-flash:cloud";

    /// <summary>Bearer token, only needed when <see cref="OllamaUrl"/> points at ollama.com.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Whether to tell the model the current date, weather, money, location, etc.</summary>
    public bool IncludeGameContext { get; set; } = true;

    /// <summary>Maximum number of wiki lookup rounds before the model must answer.</summary>
    public int MaxToolRounds { get; set; } = 4;

    /// <summary>Prompt budget for cloud models; wiki results are pruned to fit. GLM 5.3 supports 1,048,576 tokens.</summary>
    public int ContextTokens { get; set; } = 1_048_576;

    /// <summary>Context window for local models. Ollama allocates memory for all of it, so it's capped separately.</summary>
    public int LocalContextTokens { get; set; } = 32768;

    /// <summary>How much past conversation to keep word-for-word before older exchanges are compacted into a summary.</summary>
    public int HistoryTokens { get; set; } = 32768;
}
