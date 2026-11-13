using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace HatsOff;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/hatsoff";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    /// <summary>Troubleshooting details: in the normal log when "Detailed log" is on (always in a dev build), debug level otherwise.</summary>
    internal static bool DetailedLog => PluginInterface.IsDev || _detailedLog?.Invoke() == true;
    private static Func<bool>? _detailedLog;

    internal static void Debug(string message)
    {
        if (DetailedLog) Log.Information(message);
        else Log.Debug(message);
    }

    internal static void Debug(Exception ex, string message)
    {
        if (DetailedLog) Log.Information(ex, message);
        else Log.Debug(ex, message);
    }
    [PluginService] internal static ICommandManager         Commands        { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;
    [PluginService] internal static ICondition              Condition       { get; private set; } = null!;
    [PluginService] internal static IObjectTable            ObjectTable     { get; private set; } = null!;
    [PluginService] internal static IChatGui                Chat            { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle         AddonLifecycle  { get; private set; } = null!;
    [PluginService] internal static ITargetManager          TargetManager   { get; private set; } = null!;
    [PluginService] internal static IGameGui                GameGui         { get; private set; } = null!;
    [PluginService] internal static IDataManager            Data            { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;

    internal Configuration Configuration { get; }
    internal GlamourerIpc  Glamourer     { get; }
    internal HatController Controller    { get; }

    private readonly WindowSystem _windows = new("HatsOff");
    private readonly ConfigWindow _configWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _detailedLog  = () => Configuration.DetailedLog;
        Glamourer     = new GlamourerIpc(PluginInterface);
        Controller    = new HatController(Configuration, Glamourer);

        _configWindow = new ConfigWindow(this);
        _windows.AddWindow(_configWindow);

        PluginInterface.UiBuilder.Draw         += _windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi   += ToggleConfig;

        Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the settings.\n/hatsoff on|off|toggle → Turn Hats Off on or off.\n/hatsoff test → Hide your hat for 15 seconds to check it works.",
        });
    }

    internal void SaveConfig()
    {
        PluginInterface.SavePluginConfig(Configuration);
        Controller.OnConfigChanged(); // e.g. turned back on in the middle of a cutscene
    }

    private void ToggleConfig()
        => _configWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":     SetEnabled(true); break;
            case "off":    SetEnabled(false); break;
            case "toggle": SetEnabled(!Configuration.Enabled); break;
            case "test":
                if (Glamourer.Available)
                    Controller.StartTest(15);
                else
                    Chat.PrintError($"[Hats Off] {Glamourer.Status}");
                break;
            default: ToggleConfig(); break;
        }
    }

    private void SetEnabled(bool value)
    {
        Configuration.Enabled = value;
        SaveConfig();
        Chat.Print($"[Hats Off] {(value ? "On: your hat will be hidden during cutscenes." : "Off.")}");
    }

    public void Dispose()
    {
        Commands.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw         -= _windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi   -= ToggleConfig;
        _windows.RemoveAllWindows();
        Controller.Dispose();
        Glamourer.Dispose();
    }
}
