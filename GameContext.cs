using System.Text;
using StardewValley;

namespace AskTheAlmanac;

/// <summary>Summarizes the current save for the model's system prompt.</summary>
internal static class GameContext
{
    /// <summary>Build the summary. Must be called on the game thread with a save loaded.</summary>
    public static string Build()
    {
        Farmer player = Game1.player;
        var sb = new StringBuilder();

        sb.AppendLine($"- Date: {Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)} {Game1.currentSeason} {Game1.dayOfMonth}, year {Game1.year}, {Game1.getTimeOfDayString(Game1.timeOfDay)}");
        sb.AppendLine($"- Weather: {Weather()}");
        sb.AppendLine($"- Money: {player.Money:N0}g");
        sb.AppendLine($"- Farm: {player.farmName.Value} Farm");
        sb.AppendLine($"- Current location: {Game1.currentLocation?.DisplayName ?? "unknown"}");
        sb.AppendLine($"- Skill levels: Farming {player.FarmingLevel}, Mining {player.MiningLevel}, Foraging {player.ForagingLevel}, Fishing {player.FishingLevel}, Combat {player.CombatLevel}");
        sb.AppendLine($"- Today's luck: {Luck(player.DailyLuck)}");

        return sb.ToString();
    }

    private static string Weather()
    {
        if (Game1.isLightning)
            return "stormy";
        if (Game1.isRaining)
            return "rainy";
        if (Game1.isSnowing)
            return "snowy";
        if (Game1.isDebrisWeather)
            return "windy";
        return "sunny";
    }

    private static string Luck(double luck) => luck switch
    {
        > 0.07 => "very good",
        > 0.02 => "good",
        >= -0.02 => "neutral",
        >= -0.07 => "bad",
        _ => "very bad",
    };
}
