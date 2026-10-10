using System.Diagnostics;
using System.Reflection;
using StardewModdingAPI;

namespace AskTheAlmanac;

/// <summary>Stand-in for the mod's config (the real one uses SMAPI's KeybindList, which needs the game).</summary>
internal sealed class ModConfig
{
    public string OllamaUrl { get; set; } = Env("OLLAMA_URL", "http://localhost:11434");
    public string Model { get; set; } = Env("MODEL", "glm-5.3-flash:cloud");
    public string ApiKey { get; set; } = Env("OLLAMA_API_KEY", "");
    public int MaxToolRounds { get; set; } = int.Parse(Env("TOOL_ROUNDS", "4"));
    public int ContextTokens { get; set; } = int.Parse(Env("CTX", "1048576"));
    public int LocalContextTokens { get; set; } = int.Parse(Env("LOCAL_CTX", "32768"));
    public int HistoryTokens { get; set; } = int.Parse(Env("HIST", "32768"));

    private static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;
}

/// <summary>Prints SMAPI log calls to the console.</summary>
public class ConsoleMonitor : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "Log")
            Console.WriteLine($"  LOG[{args![1]}] {args[0]}");
        return method.ReturnType == typeof(bool) ? false : null;
    }
}

/// <summary>
/// Asks each argument in turn as one conversation. Arguments starting with "/" run slash commands.
/// Example: dotnet run -- "how much is a coop" "and the big one?" /context
/// </summary>
internal static class Program
{
    private const string GameContext = "- Date: Mon spring 8, year 1, 9:00 am\n- Weather: sunny\n- Money: 3,200g\n- Current location: Farm";

    private static async Task Main(string[] questions)
    {
        var config = new ModConfig();
        var client = new OllamaClient(config, message => Console.WriteLine($"  TRACE {message}"));
        var session = new ChatSession(client, DispatchProxy.Create<IMonitor, ConsoleMonitor>(), config, () => Console.WriteLine("  (config saved)"));
        Console.WriteLine($"Model: {config.Model}");

        foreach (string question in questions)
        {
            Console.WriteLine($"\n>>> {question}");
            if (session.RunCommand(question) != null)
            {
                Console.WriteLine(session.Lines[^1].Text);
                continue;
            }

            var timer = Stopwatch.StartNew();
            session.Ask(question, GameContext);
            while (session.IsBusy)
            {
                session.Update();
                await Task.Delay(50);
            }

            ChatLine answer = session.Lines[^1];
            Console.WriteLine($"[{answer.Speaker}, {timer.Elapsed.TotalSeconds:F1}s] {answer.Text}");
            foreach (string suggestion in session.Suggestions)
                Console.WriteLine($"  >> {suggestion}");

            await Drain(session, TimeSpan.FromSeconds(2)); // let a background compaction land
        }

        await Drain(session, TimeSpan.FromSeconds(20));
    }

    private static async Task Drain(ChatSession session, TimeSpan time)
    {
        for (var end = DateTime.UtcNow + time; DateTime.UtcNow < end;)
        {
            session.Update();
            await Task.Delay(50);
        }
    }
}
