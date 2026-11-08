using Dalamud.Configuration;

namespace HatsOff;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary> Master switch. </summary>
    public bool Enabled { get; set; } = true;

    /// <summary> Cutscenes outside of duties: MSQ, side quests, Inn replays... </summary>
    public bool StoryCutscenes { get; set; } = true;

    /// <summary> Cutscenes inside duties: dungeons, trials, raids... </summary>
    public bool DutyCutscenes { get; set; } = true;

    /// <summary> Also hide while talking to an NPC (dialogue box), outside of cutscenes. </summary>
    public bool NpcDialogue { get; set; } = false;

    /// <summary> Also hide when a quest makes your character react (bow, kneel...) without a cutscene or dialogue box. </summary>
    public bool QuestAnimations { get; set; } = false;

    /// <summary> Keep the hat hidden for this long after a cutscene ends (covers the fade-out and short gaps between scenes). </summary>
    public int RestoreDelayMs { get; set; } = 500;

    /// <summary> Keep the hat hidden for this long after an NPC conversation or quest reaction (bridges gaps between parts). </summary>
    public int ConversationRestoreDelayMs { get; set; } = 2000;

    /// <summary> Print a chat line every time the hat is hidden/restored. </summary>
    public bool ChatFeedback { get; set; } = false;
}
