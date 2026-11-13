using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace HatsOff;

internal sealed class ConfigWindow : Window
{
    private const float ContentWidth = 400;

    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin)
        : base("Hats Off###HatsOffConfig", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        _plugin = plugin;
    }

    private Configuration Config
        => _plugin.Configuration;

    public override void Draw()
    {
        using var wrap = new TextWrap(ContentWidth * ImGuiHelpers.GlobalScale);

        DrawStatus();
        ImGuiHelpers.ScaledDummy(4);

        DrawMainToggle();
        ImGuiHelpers.ScaledDummy(6);

        DrawWhen();
        ImGuiHelpers.ScaledDummy(6);

        DrawTestButton();
        ImGuiHelpers.ScaledDummy(6);

        DrawAdvanced();
    }

    // ----- Status banner -----

    private void DrawStatus()
    {
        var controller = _plugin.Controller;

        if (!_plugin.Glamourer.Available)
        {
            StatusLine(ImGuiColors.DalamudRed, FontAwesomeIcon.ExclamationTriangle, "Glamourer is required");
            ImGui.TextUnformatted("Hats Off uses Glamourer to hide your hat without sending anything to the game server. "
              + "Install and enable Glamourer (it also needs Penumbra) from the plugin installer, then come back here.");
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
                ImGui.TextUnformatted(_plugin.Glamourer.Status);
            return;
        }

        if (controller.IsTesting)
            StatusLine(ImGuiColors.DalamudYellow, FontAwesomeIcon.Vial, "Testing: your hat is hidden for a few seconds");
        else if (controller.IsHidden && controller.Reason is HatController.HideReason.QuestAnimation)
            StatusLine(ImGuiColors.DalamudYellow, FontAwesomeIcon.Running, "Quest animation: your hat is hidden");
        else if (controller.IsHidden && controller.Reason is HatController.HideReason.Dialogue)
            StatusLine(ImGuiColors.DalamudYellow, FontAwesomeIcon.Comments, "Talking to an NPC: your hat is hidden");
        else if (controller.IsHidden)
            StatusLine(ImGuiColors.DalamudYellow, FontAwesomeIcon.Film, "Cutscene playing: your hat is hidden");
        else if (!Config.Enabled)
            StatusLine(ImGuiColors.DalamudGrey, FontAwesomeIcon.PowerOff, "Turned off");
        else if (!Config.StoryCutscenes && !Config.DutyCutscenes && !Config.NpcDialogue && !Config.QuestAnimations)
            StatusLine(ImGuiColors.DalamudOrange, FontAwesomeIcon.ExclamationCircle, "On, but nothing is selected below");
        else
            StatusLine(ImGuiColors.HealerGreen, FontAwesomeIcon.CheckCircle,
                Config.NpcDialogue ? "Ready: waiting for a cutscene or a conversation" : "Ready: waiting for a cutscene");
    }

    private static void StatusLine(Vector4 color, FontAwesomeIcon icon, string text)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(color, icon.ToIconString());

        ImGui.SameLine();
        ImGui.TextColored(color, text);
    }

    // ----- Settings -----

    private void DrawMainToggle()
    {
        var enabled = Config.Enabled;
        if (ImGui.Checkbox("Hide my headgear automatically", ref enabled))
        {
            Config.Enabled = enabled;
            _plugin.SaveConfig();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            ImGui.TextUnformatted("Your hat comes back as soon as it's over. Nothing is sent to the game server: "
              + "other players keep seeing your hat. Friends synced through Lightless Sync or PlayerSync may see it briefly.");
        }
    }

    private void DrawWhen()
    {
        ImGui.TextUnformatted("When?");
        using var disabled = ImRaii.Disabled(!Config.Enabled);
        using var indent   = ImRaii.PushIndent();

        var story = Config.StoryCutscenes;
        if (ImGui.Checkbox("Story and quest cutscenes", ref story))
        {
            Config.StoryCutscenes = story;
            _plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Main Scenario, side quests, and cutscenes you replay from the Inn.");

        var duty = Config.DutyCutscenes;
        if (ImGui.Checkbox("Cutscenes inside duties", ref duty))
        {
            Config.DutyCutscenes = duty;
            _plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Dungeons, trials, raids and other instanced content.");

        var dialogue = Config.NpcDialogue;
        if (ImGui.Checkbox("Talking to NPCs", ref dialogue))
        {
            Config.NpcDialogue = dialogue;
            _plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Also hides your hat when a dialogue box opens outside of cutscenes, "
          + "for the whole conversation (including choices and quest windows).\n"
          + "Vendors, service NPCs and retainers are left alone, so you can see and try on gear.\n"
          + "Quest objects (glowing spots, items to examine) only hide it if your character visibly reacts.\n"
          + "Friends synced through Lightless Sync or PlayerSync will see it during these conversations.");

        var questAnims = Config.QuestAnimations;
        if (ImGui.Checkbox("Quest animations without dialogue (experimental)", ref questAnims))
        {
            Config.QuestAnimations = questAnims;
            _plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Also hides your hat when a quest makes your character do something (bow, kneel, hand over an item...) "
          + "outside of cutscenes and dialogue boxes.\n"
          + "Facial expressions count too. Simple interactions where your character doesn't react (like picking something up) are ignored.\n"
          + "Friends synced through Lightless Sync or PlayerSync will see it at those moments.");
    }

    private void DrawTestButton()
    {
        using (ImRaii.Disabled(!_plugin.Glamourer.Available || _plugin.Controller.IsTesting))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, "Try it: hide my hat for 15 seconds"))
                _plugin.Controller.StartTest(15);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Does exactly what happens during a cutscene, so you can check it works.");
    }

    private void DrawAdvanced()
    {
        if (!ImGui.CollapsingHeader("Advanced"))
            return;

        var delay = Config.RestoreDelayMs / 1000f;
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Keep hidden after a cutscene", ref delay, 0f, 3f, "%.1f s"))
            Config.RestoreDelayMs = (int)MathF.Round(delay * 1000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            _plugin.SaveConfig(); // once on release, not every frame while dragging

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Waits a little before giving your hat back, so it doesn't pop in during the fade-out "
          + "or between two scenes that follow each other. 0.5 s works well.");

        var talkDelay = Config.ConversationRestoreDelayMs / 1000f;
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Keep hidden after a conversation", ref talkDelay, 0f, 5f, "%.1f s"))
            Config.ConversationRestoreDelayMs = (int)MathF.Round(talkDelay * 1000);
        if (ImGui.IsItemDeactivatedAfterEdit())
            _plugin.SaveConfig();

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Same idea for NPC conversations, quest objects and quest animations. "
          + "Conversations often come in several parts with short gaps: 2 s keeps the hat from flashing back in between.");

        var chat = Config.ChatFeedback;
        if (ImGui.Checkbox("Show a chat message when the hat is hidden or restored", ref chat))
        {
            Config.ChatFeedback = chat;
            _plugin.SaveConfig();
        }

        var detailed = Config.DetailedLog;
        if (ImGui.Checkbox("Write troubleshooting details to the Dalamud log", ref detailed))
        {
            Config.DetailedLog = detailed;
            _plugin.SaveConfig();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("For troubleshooting: include the [HatsOff] lines from /xllog when you report a problem. Off by default.");

        ImGuiHelpers.ScaledDummy(2);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        {
            ImGui.TextUnformatted($"Last action: {_plugin.Controller.LastAction}");
            ImGui.TextUnformatted(_plugin.Glamourer.Status);
            ImGui.TextUnformatted("Chat command: /hatsoff (on, off, toggle, test)");
        }
    }

    /// <summary> Wraps all text at a fixed width so the auto-resizing window keeps a sensible size. </summary>
    private readonly ref struct TextWrap
    {
        public TextWrap(float width)
            => ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        public void Dispose()
            => ImGui.PopTextWrapPos();
    }
}
