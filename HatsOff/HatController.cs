using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace HatsOff;

/// <summary>
/// Core logic. Purely event-driven: nothing runs every frame, ever. The plugin only wakes up when
/// the game or Glamourer tells it something changed, plus one-shot timers for the restore delay.
///
/// Events listened to:
/// - Dalamud condition changes: cutscene start/end, duty, loading screens, NPC events.
/// - Dialogue box ("Talk") opening/closing, and shop windows opening (ends the NPC-dialogue hiding).
/// - Glamourer: loaded/unloaded, and "an actor's state changed" (to notice something re-showing the hat).
///
/// Why it works well:
/// - Visible only to you: we never use /displayhead (server-side). Glamourer's SetMetaState only flips the
///   local draw flag, so other players never see anything change.
/// - Cutscene copies: MSQ cutscenes spawn a copy of your character. Glamourer resolves those copies to *your*
///   identity and applies your Glamourer state to them as they are created, so they spawn already hatless.
/// - Restoring: we remember the hat state from just before and put exactly that back.
/// </summary>
internal sealed unsafe class HatController : IDisposable
{
    public enum HideReason
    {
        None,
        Test,
        Cutscene,
        Dialogue,
        QuestAnimation,
    }

    private static readonly TimeSpan QuestCheckInterval = TimeSpan.FromMilliseconds(100);

    /// <summary> Why the hat was hidden most recently, to pick the right restore delay. </summary>
    private HideReason _hiddenFor;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary> Shop-like windows: opening one ends the NPC-dialogue hiding, so you can see (and try on) gear. </summary>
    private static readonly string[] ShopAddons =
    [
        "Shop", "ShopExchangeItem", "ShopExchangeCurrency", "ShopExchangeCoin", "InclusionShop", "FreeShop",
        "GrandCompanyExchange", "CollectablesShop", "ShopCard", "TripleTriadCoinExchange", "SkyIslandExchange",
    ];

    private readonly Configuration _config;
    private readonly GlamourerIpc  _glamourer;

    /// <summary> Hat visibility to put back afterwards. </summary>
    private bool _restoreVisible;

    /// <summary> Bumped whenever a scheduled restore must be cancelled; a timer only acts if its generation is current. </summary>
    private int _restoreGeneration;

    private bool      _retryPending;
    private bool      _recheckPending;
    private bool      _disposed;
    private DateTime? _testUntil;

    /// <summary> A conversation started with a dialogue box and isn't over yet. </summary>
    private bool _inDialogue;

    /// <summary> The dialogue box is currently on screen. </summary>
    private bool _talkOpen;

    /// <summary> A shop window was opened during the current NPC interaction. </summary>
    private bool _shopThisEvent;

    /// <summary> The current interaction is with an object (glowing quest spot, item...), not an NPC. </summary>
    private bool _objectThisEvent;

    /// <summary> Name shown as the speaker of the current dialogue line (empty for descriptions without a speaker). </summary>
    private string _talkSpeaker = string.Empty;

    // Quest interactions without dialogue: watch the character's animation only while such an interaction runs.
    private bool     _questWatching;
    private bool     _questReaction;
    private bool     _questCheckPending;

    /// <summary> Glamourer refused for a reason that won't fix itself (state locked by another plugin...): don't insist. </summary>
    private bool _refused;

    /// <summary> Glamourer was unloaded while we hid a hat that should come back. </summary>
    private bool _oweRestoreAfterReload;

    /// <summary> Name of the character whose hat we hid, to restore it even if the player object is gone at unload. </summary>
    private string? _playerName;

    public HatController(Configuration config, GlamourerIpc glamourer)
    {
        _config    = config;
        _glamourer = glamourer;

        Plugin.Condition.ConditionChange += OnConditionChange;
        _glamourer.AvailabilityChanged   += OnGlamourerAvailabilityChanged;
        _glamourer.ActorStateChanged     += OnActorStateChanged;

        UpdateWindowListeners();

        // Plugin (re)loaded in the middle of a cutscene. The constructor may run on a loader thread,
        // and everything touching the game or Glamourer must happen on the framework thread.
        Plugin.Framework.RunOnFrameworkThread(Evaluate);
    }

    private bool _isHidden;

    /// <summary> True while we have hidden the hat and still owe a restore. Glamourer notifications are only listened to meanwhile. </summary>
    public bool IsHidden
    {
        get => _isHidden;
        private set
        {
            _isHidden = value;
            _glamourer.WatchStateChanges(value);
        }
    }

    /// <summary> Why the hat is (or should be) hidden right now, for the settings window. </summary>
    public HideReason Reason { get; private set; }

    /// <summary> True while a test run (from the settings window or /hatsoff test) is active. </summary>
    public bool IsTesting
        => _testUntil is { } until && DateTime.UtcNow < until;

    /// <summary> Last thing that happened, for the settings window. </summary>
    public string LastAction { get; private set; } = "Nothing yet.";

    public static bool InCutscene
        => Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
         || Plugin.Condition[ConditionFlag.WatchingCutscene]
         || Plugin.Condition[ConditionFlag.WatchingCutscene78];

    public static bool InDuty
        => Plugin.Condition[ConditionFlag.BoundByDuty]
         || Plugin.Condition[ConditionFlag.BoundByDuty56]
         || Plugin.Condition[ConditionFlag.BoundByDuty95];

    private static bool InQuestEvent
        => Plugin.Condition[ConditionFlag.OccupiedInQuestEvent];

    /// <summary> The NPC we're talking to: interacting with an NPC targets it. 0 if unknown. </summary>
    /// <summary> Interacting with an object (glowing quest destination, item to examine...) rather than an NPC. </summary>
    private static bool TalkingToObject()
        => (Plugin.TargetManager.Target ?? Plugin.TargetManager.SoftTarget) is
            { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj };

    private static uint TalkingToBaseId()
    {
        var target = Plugin.TargetManager.Target ?? Plugin.TargetManager.SoftTarget;
        return target is { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc } ? target.BaseId : 0;
    }

    private static bool InNpcEvent
        => Plugin.Condition[ConditionFlag.OccupiedInEvent]
         || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent];

    /// <summary> Talking to your retainers: never a story moment, and their greetings would make the hat flicker. </summary>
    private static bool AtSummoningBell
        => Plugin.Condition[ConditionFlag.OccupiedSummoningBell];

    private static bool BetweenAreas
        => Plugin.Condition[ConditionFlag.BetweenAreas]
         || Plugin.Condition[ConditionFlag.BetweenAreas51];

    private bool _windowListeners;

    /// <summary>
    /// The dialogue box and shop windows only matter for the NPC conversation / quest animation options (both off by
    /// default): without them, Hats Off doesn't listen to any game window at all.
    /// </summary>
    private void UpdateWindowListeners()
    {
        var want = _config.NpcDialogue || _config.QuestAnimations;
        if (want == _windowListeners)
            return;

        _windowListeners = want;
        if (want)
        {
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   "Talk", OnTalkOpened);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostShow,    "Talk", OnTalkOpened);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "Talk", OnTalkUpdated);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "Talk", OnTalkUpdated);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreHide,     "Talk", OnTalkClosed);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Talk", OnTalkClosed);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   ShopAddons, OnShopOpened);
        }
        else
        {
            Plugin.AddonLifecycle.UnregisterListener(OnTalkOpened, OnTalkClosed, OnShopOpened, OnTalkUpdated);
            _talkOpen    = false;
            _talkSpeaker = string.Empty;
        }
    }

    /// <summary> Settings changed: (un)register what the options need, then re-decide. </summary>
    public void OnConfigChanged()
    {
        UpdateWindowListeners();
        Evaluate();
    }

    /// <summary> Hide the hat for a few seconds, exactly like a cutscene would, to check that everything works. </summary>
    public void StartTest(int seconds)
    {
        _testUntil = DateTime.UtcNow.AddSeconds(seconds);
        Evaluate();
        Plugin.Framework.RunOnTick(Evaluate, TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(50));
    }

    // ----- Event handlers -----

    /// <summary>
    /// Many conditions change all the time (jumping, combat, mounting...). Only these can change our decision,
    /// so everything else is ignored right away.
    /// </summary>
    private static bool IsRelevant(ConditionFlag flag)
        => flag is ConditionFlag.OccupiedInCutSceneEvent or ConditionFlag.WatchingCutscene or ConditionFlag.WatchingCutscene78
            or ConditionFlag.BoundByDuty or ConditionFlag.BoundByDuty56 or ConditionFlag.BoundByDuty95
            or ConditionFlag.OccupiedInEvent or ConditionFlag.OccupiedInQuestEvent or ConditionFlag.OccupiedSummoningBell
            or ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51;

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (!IsRelevant(flag))
            return;

        // A loading screen just ended while we hide the hat: make sure nothing undid it meanwhile
        // (e.g. Glamourer resetting manual changes on zone change inside a cutscene).
        if (!value && IsHidden && flag is ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51)
            ScheduleRecheck();

        Evaluate();
    }

    /// <summary> A new dialogue line: remember who speaks, since it decides whether an object interaction is a conversation. </summary>
    private void OnTalkUpdated(AddonEvent type, AddonArgs args)
    {
        // The speaker only matters to tell an object's text from someone speaking.
        if (!_objectThisEvent || _inDialogue)
            return;

        var speaker = ReadSpeaker(args);
        if (speaker == _talkSpeaker)
            return;

        _talkSpeaker = speaker;
        if (_objectThisEvent && !_inDialogue)
            Evaluate();
    }

    private static unsafe string ReadSpeaker(AddonArgs args)
    {
        try
        {
            var talk = (FFXIVClientStructs.FFXIV.Client.UI.AddonTalk*)args.Addon.Address;
            if (talk == null || talk->AtkTextNode220 == null)
                return string.Empty;

            return new Lumina.Text.ReadOnly.ReadOnlySeStringSpan(talk->AtkTextNode220->NodeText.AsSpan()).ExtractText().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary> Someone (not the object itself) speaks in the dialogue box: it's a real conversation. </summary>
    private bool SomeoneSpeaks()
    {
        if (_talkSpeaker.Length == 0)
            return false;

        var target = Plugin.TargetManager.Target ?? Plugin.TargetManager.SoftTarget;
        return target is null || !string.Equals(target.Name.TextValue, _talkSpeaker, StringComparison.OrdinalIgnoreCase);
    }

    private void OnTalkOpened(AddonEvent type, AddonArgs args)
    {
        _talkOpen    = true;
        _talkSpeaker = _objectThisEvent ? ReadSpeaker(args) : string.Empty;
        if (_config.Enabled && _config.NpcDialogue && !_inDialogue)
            Evaluate();
    }

    private void OnTalkClosed(AddonEvent type, AddonArgs args)
    {
        _talkOpen = false;
        if (_inDialogue)
            Evaluate();
    }

    private void OnShopOpened(AddonEvent type, AddonArgs args)
    {
        if (!InNpcEvent)
            return;

        // Shops are never story moments, and you may want to see (or try on) headgear there.
        // Stay out of the way until this NPC interaction is completely over.
        _shopThisEvent = true;
        if (_inDialogue)
            Evaluate();
    }

    private void OnGlamourerAvailabilityChanged(bool available)
    {
        if (!available)
        {
            // Glamourer unloaded while we were hiding a hat that should come back: remember it,
            // so we can give it back when Glamourer returns (its fresh state would think the hat is meant to be hidden).
            _oweRestoreAfterReload = IsHidden && _restoreVisible;
            IsHidden               = false;
            _restoreGeneration++;
            LastAction = "Glamourer was unloaded.";
            return;
        }

        if (_oweRestoreAfterReload && !WantHidden(DateTime.UtcNow) && Plugin.ObjectTable.LocalPlayer is { } player)
        {
            _oweRestoreAfterReload = false;
            var result = _glamourer.SetHatVisible(player.ObjectIndex, true);
            Report(Succeeded(result) ? "Glamourer is back, hat restored." : $"Could not restore the hat ({Describe(result)}).");
        }

        Evaluate();
    }

    /// <summary> Glamourer changed someone's state. If it's us while we hide the hat, check the hat is still hidden. </summary>
    private void OnActorStateChanged(nint address)
    {
        // GlamourerIpc already filtered on our own character.
        if (!IsHidden)
            return;

        Plugin.Log.Debug("Glamourer reported a change on your character, checking the hat.");
        ScheduleRecheck();
    }

    /// <summary> Check the hat on the next frame (the game applies a new hat flag right after Glamourer's notification). </summary>
    private void ScheduleRecheck()
    {
        if (_recheckPending)
            return;

        _recheckPending = true;
        Plugin.Framework.RunOnTick(_recheckTick ??= () =>
        {
            _recheckPending = false;
            RecheckHat();
        }, delayTicks: 1);
    }

    private Action? _recheckTick;
    private Action? _questCheckTick;

    // ----- Decision -----

    /// <summary> Called by every event: decides whether to hide, keep hidden, or (after the delay) restore. </summary>
    public void Evaluate()
    {
        if (_disposed || !_glamourer.Available)
            return;

        if (WantHidden(DateTime.UtcNow))
        {
            _hiddenFor = Reason;
            _restoreGeneration++; // cancel any pending restore
            if (!IsHidden && !_refused)
                TryHide();
        }
        else
        {
            _refused = false;
            if (IsHidden)
                ScheduleRestore();
        }
    }

    private bool WantHidden(DateTime now)
    {
        Reason = ComputeReason(now);
        return Reason is not HideReason.None;
    }

    private HideReason ComputeReason(DateTime now)
    {
        if (_testUntil is { } until)
        {
            if (now < until)
                return HideReason.Test;

            _testUntil = null;
        }

        if (!_config.Enabled)
        {
            _inDialogue = false;
            return HideReason.None;
        }

        if (InCutscene)
        {
            // Cutscenes are handled on their own: no need to watch animations meanwhile.
            _questWatching = false;
            _questReaction = false;

            if (InDuty ? _config.DutyCutscenes : _config.StoryCutscenes)
                return HideReason.Cutscene;

            // A cutscene type you turned off: respect that, even though it has dialogue boxes in it.
            _inDialogue = false;
            return HideReason.None;
        }

        // The interaction is over: forget what we learned about it.
        if (!InNpcEvent)
        {
            _shopThisEvent   = false;
            _objectThisEvent = false;
        }
        else if (!_objectThisEvent && (_config.NpcDialogue || _config.QuestAnimations) && TalkingToObject())
        {
            // An object (glowing quest spot, aether current, switch...): it only counts if the character reacts,
            // with or without a text box.
            _objectThisEvent = true;
            Plugin.Log.Information("Interacting with an object: the hat only hides if your character reacts.");
        }

        if (_config.NpcDialogue && !_shopThisEvent && !AtSummoningBell)
        {
            // Start only when a dialogue box is up AND the game says we're in an NPC conversation.
            // A dialogue box on its own (retainers, system messages...) would only make the hat flicker.
            if (!_inDialogue && _talkOpen && InNpcEvent)
            {
                // Quest conversations always count. Otherwise, skip vendors and service NPCs entirely
                // (their greetings aren't story moments, and you may want to see or try on gear there).
                var npcId = TalkingToBaseId();
                if (_objectThisEvent && !SomeoneSpeaks())
                {
                    // An object's own text: only counts if your character visibly reacts (animation watch below).
                }
                else if (_objectThisEvent)
                {
                    // The object started a conversation: dialogue wins.
                    _inDialogue = true;
                    Plugin.Log.Information($"Someone speaks ({_talkSpeaker}) after interacting with an object, hiding the hat.");
                }
                else if (!InQuestEvent && ServiceNpcs.IsServiceNpc(npcId))
                {
                    _shopThisEvent = true;
                    Plugin.Log.Information($"Conversation with a vendor/service NPC ({npcId}), leaving the hat alone.");
                }
                else
                {
                    _inDialogue = true;
                    Plugin.Log.Information(InQuestEvent
                        ? $"Quest conversation ({npcId}), hiding the hat."
                        : $"Conversation with a regular NPC ({npcId}), hiding the hat.");
                }
            }

            if (_shopThisEvent)
            {
                _inDialogue = false;
                return HideReason.None;
            }

            // Keep going for the whole conversation: choices and quest windows between lines included.
            if (_inDialogue && InNpcEvent)
                return HideReason.Dialogue;
        }

        _inDialogue = false;

        // Interactions without an NPC conversation (quest actions, objects): hide only if the character actually
        // reacts (bow, kneel...). Watching starts with the interaction, so a reaction is caught even before a text box.
        // Only watch where a reaction would actually count, and stop as soon as it no longer would.
        if (WatchUseful())
        {
            if (_questReaction)
                return HideReason.QuestAnimation;

            if (!_questWatching)
                StartQuestWatch();
        }
        else
        {
            _questWatching = false;
            _questReaction = false;
        }

        return HideReason.None;
    }

    // ----- Quest animations -----

    /// <summary> Is there any point watching the character's animation right now? </summary>
    private bool WatchUseful()
        => InNpcEvent && !InCutscene && !_inDialogue && !_shopThisEvent && !AtSummoningBell
         && ((_config.QuestAnimations && InQuestEvent) || _objectThisEvent);

    private const int AnimationLayers = 14;

    /// <summary> What the character is doing: every animation layer (body, upper body, face...), forced animation and mode. </summary>
    private sealed class AnimState
    {
        public readonly ushort[] Layers = new ushort[AnimationLayers];
        public ushort Override;
        public byte   Mode;
    }

    private readonly AnimState _animSnapshot = new();
    private readonly AnimState _animNow      = new();

    private static unsafe bool ReadAnimation(AnimState into)
    {
        // Direct pointer to our character: this runs 10 times a second during interactions, so no wrapper objects.
        var character = (Character*)FFXIVClientStructs.FFXIV.Client.Game.Control.Control.GetLocalPlayer();
        if (character == null)
            return false;

        var ids       = character->Timeline.TimelineSequencer.TimelineIds;
        for (var i = 0; i < AnimationLayers && i < ids.Length; i++)
            into.Layers[i] = ids[i];
        into.Override = character->Timeline.BaseOverride;
        into.Mode     = (byte)character->Mode;
        return true;
    }

    private static void Copy(AnimState from, AnimState to)
    {
        Array.Copy(from.Layers, to.Layers, AnimationLayers);
        to.Override = from.Override;
        to.Mode     = from.Mode;
    }

    /// <summary> Remember what the character looks like when the interaction starts, then watch for a change. </summary>
    private void StartQuestWatch()
    {
        if (!ReadAnimation(_animSnapshot))
            return;

        _questWatching = true;
        ScheduleQuestCheck();
    }

    /// <summary>
    /// The game has no "your character started an animation" event, so while (and only while) an interaction
    /// runs, look at the character's animation 10 times a second. It stops as soon as the interaction ends.
    /// </summary>
    private void ScheduleQuestCheck()
    {
        if (_questCheckPending)
            return;

        _questCheckPending = true;
        Plugin.Framework.RunOnTick(_questCheckTick ??= () =>
        {
            _questCheckPending = false;
            QuestCheck();
        }, QuestCheckInterval);
    }

    private void QuestCheck()
    {
        if (_disposed || !_questWatching || _questReaction)
            return;

        if (!WatchUseful())
        {
            // Interaction over, turned into a conversation or a shop, a cutscene started...: stop watching.
            _questWatching = false;
            Evaluate();
            return;
        }

        if (ReadAnimation(_animNow) && FindReaction() is { } reaction)
        {
            Plugin.Log.Information($"Your character reacts ({reaction}), hiding the hat.");
            _questReaction = true;
            Evaluate();
            return;
        }

        ScheduleQuestCheck();
    }

    /// <summary>
    /// Compares the current animation with the snapshot. Returns a description of a real reaction, or null.
    /// Changes the character makes on its own (idle variants, idle stance, an animation ending) update the snapshot.
    /// </summary>
    private string? FindReaction()
    {

        if (_animNow.Mode != _animSnapshot.Mode)
            return $"mode {_animSnapshot.Mode} -> {_animNow.Mode}";

        if (_animNow.Override != _animSnapshot.Override && _animNow.Override != 0)
            return $"forced animation {Animations.Describe(_animNow.Override)}";

        for (var i = 0; i < AnimationLayers; i++)
        {
            var before = _animSnapshot.Layers[i];
            var now    = _animNow.Layers[i];
            if (before == now || now == 0)
                continue;

            var key = Animations.Key(now);
            if (Animations.IsFacialExpression(key))
                return $"facial expression {Animations.Describe(now)}";

            if (Animations.IsIdle(now))
                continue;

            // Body animation, or an emote played on another layer.
            if (i == 0 || key.StartsWith("emote/", StringComparison.Ordinal) || key.StartsWith("event", StringComparison.Ordinal))
                return $"{Animations.Describe(before)} -> {Animations.Describe(now)}";

            Plugin.Log.Debug($"Animation layer {i} changed to {Animations.Describe(now)}, not treated as a reaction.");
        }

        // Nothing meaningful: take the current state as the new reference.
        Copy(_animNow, _animSnapshot);
        return null;
    }

    /// <summary>
    /// Restore after the configured delay. Each new event re-arms the timer, and any reason to hide again cancels it,
    /// so short gaps between scenes or dialogue lines never make the hat flash.
    /// </summary>
    private void ScheduleRestore()
    {
        var generation = ++_restoreGeneration;

        // Conversations often come in several parts with a short gap between them:
        // wait a bit longer there, so the hat doesn't flash back in between.
        var delayMs = _hiddenFor is HideReason.Dialogue or HideReason.QuestAnimation
            ? _config.ConversationRestoreDelayMs
            : _config.RestoreDelayMs;
        delayMs = Math.Max(0, delayMs);

        Plugin.Framework.RunOnTick(() =>
        {
            if (_disposed || generation != _restoreGeneration || !IsHidden || WantHidden(DateTime.UtcNow))
                return;

            TryRestore();
        }, TimeSpan.FromMilliseconds(delayMs));
    }

    /// <summary> Something went wrong (no player object yet, Glamourer busy...): try again once, a bit later. </summary>
    private void ScheduleRetry()
    {
        if (_retryPending)
            return;

        _retryPending = true;
        Plugin.Framework.RunOnTick(() =>
        {
            _retryPending = false;
            Evaluate();
        }, RetryDelay);
    }

    // ----- Actions -----

    /// <summary>
    /// Is the hat shown? Asks Glamourer first: it decides what is actually drawn, and the game's own flag can say
    /// "visible" while Glamourer hides the hat another way. The game flag is only a fallback if Glamourer can't answer.
    /// </summary>
    private bool IsHatShown(IPlayerCharacter player)
        => _glamourer.IsHatVisible(player.ObjectIndex) ?? !((Character*)player.Address)->DrawData.IsHatHidden;

    // Safety net against fighting another plugin over the hat: at most this many re-hides in this window.
    private const int ReHideLimit = 5;
    private static readonly TimeSpan ReHideWindow = TimeSpan.FromSeconds(3);
    private readonly Queue<DateTime> _reHides = new();

    private static bool Succeeded(GlamourerIpc.ApiEc? result)
        => result is GlamourerIpc.ApiEc.Success or GlamourerIpc.ApiEc.NothingDone;

    private static string Describe(GlamourerIpc.ApiEc? result)
        => result?.ToString() ?? "Glamourer did not answer";

    private void TryHide()
    {
        // During loading screens the player object isn't ready; the condition change at the end wakes us up again.
        if (BetweenAreas)
            return;

        if (Plugin.ObjectTable.LocalPlayer is not { } player)
        {
            ScheduleRetry();
            return;
        }

        _playerName = player.Name.TextValue;

        if (!IsHatShown(player))
        {
            // Already hidden: don't touch anything, so we never owe Glamourer an override afterwards.
            // We still keep watch, in case something shows it during the cutscene.
            // (Exception: Glamourer was reloaded while it hid a hat that should come back.)
            IsHidden               = true;
            _restoreVisible        = _oweRestoreAfterReload;
            _oweRestoreAfterReload = false;
            Report("Hat was already hidden, nothing to do.");
            return;
        }

        var result = _glamourer.SetHatVisible(player.ObjectIndex, false);
        if (Succeeded(result))
        {
            IsHidden               = true;
            _restoreVisible        = true;
            _oweRestoreAfterReload = false;
            Report("Hat hidden.");
        }
        else if (result is GlamourerIpc.ApiEc.InvalidKey or GlamourerIpc.ApiEc.ActorNotHuman or GlamourerIpc.ApiEc.InvalidState)
        {
            // Won't fix itself (state locked by another plugin, transformed into a non-human...): say it once, don't insist.
            _refused = true;
            Report($"Glamourer won't let us change the hat right now ({Describe(result)}), leaving it alone.");
        }
        else
        {
            LastAction = $"Could not hide the hat ({Describe(result)}), retrying.";
            Plugin.Log.Warning(LastAction);
            ScheduleRetry();
        }
    }

    private void TryRestore()
    {
        if (BetweenAreas)
            return; // The end of the loading screen triggers Evaluate, which schedules the restore again.

        if (Plugin.ObjectTable.LocalPlayer is not { } player)
        {
            ScheduleRetry();
            return;
        }

        IsHidden = false;

        // We only ever give back a hat we hid ourselves. We never force a hat *hidden* on the way out:
        // whatever the game says (your /displayhead setting) always wins.
        if (!_restoreVisible)
        {
            Report("Done, hat stays hidden like before.");
            return;
        }

        var result = _glamourer.SetHatVisible(player.ObjectIndex, true);
        Report(Succeeded(result)
            ? "Hat restored."
            : $"Could not restore the hat ({Describe(result)}). You can toggle it back in Glamourer.");
    }

    /// <summary> Something (you, another plugin, a gearset change...) may have shown the hat while we hide it. </summary>
    private void RecheckHat()
    {
        if (_disposed || !IsHidden)
            return;

        if (Plugin.ObjectTable.LocalPlayer is not { } player)
        {
            // Right after a loading screen the character may not be there yet: look again shortly.
            Plugin.Framework.RunOnTick(ScheduleRecheck, TimeSpan.FromMilliseconds(500));
            return;
        }

        if (!IsHatShown(player))
            return;

        // That's the newest intent: the hat should be visible once we're done.
        _restoreVisible = true;

        if (!WantHidden(DateTime.UtcNow))
        {
            // We were only waiting to restore, and it's already visible: nothing left to do.
            IsHidden = false;
            _restoreGeneration++;
            Report("Hat was shown by something else, nothing left to restore.");
            return;
        }

        // Never get into a tug-of-war: if something keeps showing the hat, let it win.
        var now = DateTime.UtcNow;
        while (_reHides.Count > 0 && now - _reHides.Peek() > ReHideWindow)
            _reHides.Dequeue();

        if (_reHides.Count >= ReHideLimit)
        {
            Report("Something keeps showing the hat, letting it be.");
            return;
        }

        _reHides.Enqueue(now);
        var result = _glamourer.SetHatVisible(player.ObjectIndex, false);
        Report(Succeeded(result)
            ? "Something showed the hat, hid it again."
            : $"Could not re-hide the hat ({Describe(result)}).");
    }

    private void Report(string message)
    {
        LastAction = message;
        Plugin.Log.Information(message);
        if (!_config.ChatFeedback)
            return;

        // Don't flood the chat when the same thing happens several times in a row.
        var now = DateTime.UtcNow;
        if (message == _lastChatMessage && now - _lastChatTime < TimeSpan.FromSeconds(5))
            return;

        _lastChatMessage = message;
        _lastChatTime    = now;
        Plugin.Chat.Print($"[Hats Off] {message}");
    }

    private string   _lastChatMessage = string.Empty;
    private DateTime _lastChatTime    = DateTime.MinValue;

    public void Dispose()
    {
        _disposed = true;
        Plugin.Condition.ConditionChange -= OnConditionChange;
        _glamourer.AvailabilityChanged   -= OnGlamourerAvailabilityChanged;
        _glamourer.ActorStateChanged     -= OnActorStateChanged;
        if (_windowListeners)
            Plugin.AddonLifecycle.UnregisterListener(OnTalkOpened, OnTalkClosed, OnShopOpened, OnTalkUpdated);
        _glamourer.WatchStateChanges(false);

        // Plugin unloaded / disabled mid-cutscene: give the hat back.
        if (!IsHidden || !_restoreVisible || !_glamourer.Available)
            return;

        if (Plugin.ObjectTable.LocalPlayer is { } player)
            _glamourer.SetHatVisible(player.ObjectIndex, true);
        else if (_playerName is not null)
            _glamourer.SetHatVisibleByName(_playerName, true); // e.g. unloaded during a loading screen
        else
            Plugin.Log.Warning("Unloaded while the hat was hidden and the character wasn't available: toggle it back in Glamourer if needed.");
    }
}
