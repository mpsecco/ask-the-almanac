using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace AskTheAlmanac;

internal sealed class ModEntry : Mod
{
    private ModConfig config = null!;
    private OllamaClient client = null!;
    private ChatSession session = null!;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        client = new OllamaClient(config, message => Monitor.Log(message, LogLevel.Trace));
        session = new ChatSession(client, Monitor, config, () => helper.WriteConfig(config));

        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        helper.Events.GameLoop.UpdateTicked += (_, _) => session.Update();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => session.Clear();

        helper.ConsoleCommands.Add("almanac_ask", "Ask the Almanac a question from the SMAPI console.\n\nUsage: almanac_ask <question>", OnAskCommand);
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!config.ToggleKey.JustPressed())
            return;

        if (Game1.activeClickableMenu is ChatMenu menu)
            menu.exitThisMenu();
        else if (Context.IsPlayerFree)
            Game1.activeClickableMenu = new ChatMenu(session, GetGameContext);
    }

    private string? GetGameContext() =>
        config.IncludeGameContext && Context.IsWorldReady ? GameContext.Build() : null;

    private void OnAskCommand(string name, string[] args)
    {
        string question = string.Join(" ", args).Trim();
        if (question.Length == 0)
        {
            Monitor.Log("Usage: almanac_ask <question>", LogLevel.Info);
            return;
        }

        var messages = ChatSession.BuildMessages(question, GetGameContext(), null, Array.Empty<(string, string)>());
        Task.Run(async () =>
        {
            try
            {
                string reply = await client.ChatAsync(messages, status => Monitor.Log($"[{status}]", LogLevel.Info));
                var (answer, suggestions) = ChatSession.ParseReply(reply);
                Monitor.Log(answer, LogLevel.Info);
                foreach (string suggestion in suggestions)
                    Monitor.Log($"  > {suggestion}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Request failed: {ex}", LogLevel.Error);
            }
        });
    }
}
