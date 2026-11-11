using Lumina.Excel.Sheets;

namespace HatsOff;

/// <summary> Looks up animation (ActionTimeline) names from the game data, cached: only used when an animation changes. </summary>
internal static class Animations
{
    private static readonly Dictionary<ushort, string> Keys = new();

    public static string Key(ushort id)
    {
        if (Keys.TryGetValue(id, out var key))
            return key;

        try
        {
            key = Plugin.Data.GetExcelSheet<ActionTimeline>().GetRowOrDefault(id) is { } row ? row.Key.ExtractText() : string.Empty;
        }
        catch (Exception ex)
        {
            Plugin.Debug(ex, "Could not read animation data.");
            key = string.Empty;
        }

        Keys[id] = key;
        return key;
    }

    /// <summary>
    /// Animations a character plays on its own while standing still: idle variants ("normal/idle_inactive1"),
    /// turning on the spot ("normal/turn_loop_l"), idle stances from /cpose ("emote/pose06_loop"),
    /// and weapon draw/sheathe ("battle/...").
    /// </summary>
    public static bool IsIdle(ushort id)
    {
        var key = Key(id);
        return key.StartsWith("normal/idle", StringComparison.Ordinal)
            || key.StartsWith("normal/turn", StringComparison.Ordinal)
            || key.StartsWith("emote/pose", StringComparison.Ordinal)
            || key.StartsWith("battle/", StringComparison.Ordinal); // e.g. the game sheathing your weapon
    }

    /// <summary> A real facial expression ("facial/pose/smile"), not the neutral face ("facial/pose/base"). </summary>
    public static bool IsFacialExpression(string key)
        => key.StartsWith("facial/", StringComparison.Ordinal) && !key.EndsWith("/base", StringComparison.Ordinal);

    public static string Describe(ushort id)
    {
        var key = Key(id);
        return key.Length > 0 ? $"{id} {key}" : id.ToString();
    }
}
