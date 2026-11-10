using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Newtonsoft.Json.Linq;

namespace HatsOff;

/// <summary>
/// Minimal, dependency-free binding of the Glamourer IPC calls we need.
/// Labels and signatures copied from Glamourer.Api (IpcSubscribers/PluginState.cs and Items.cs).
/// No polling: availability is checked once at startup, then kept up to date through
/// Glamourer's own Initialized / Disposed events.
/// </summary>
internal sealed class GlamourerIpc : IDisposable
{
    // Glamourer.Api.Enums.MetaFlag.HatState
    private const ulong MetaFlagHatState = 0x02;

    // Glamourer.Api.Enums.ApplyFlag.Once : don't force anything into automation.
    private const ulong ApplyFlagOnce = 0x01;

    // Glamourer.Api.Enums.GlamourerApiEc
    public enum ApiEc
    {
        Success       = 0,
        NothingDone   = 1,
        ActorNotFound = 2,
        ActorNotHuman = 3,
        InvalidKey    = 6,
        InvalidState  = 7,
    }

    private readonly ICallGateSubscriber<(int, int)>                          _apiVersion;
    private readonly ICallGateSubscriber<int, ulong, bool, uint, ulong, int> _setMetaState;
    private readonly ICallGateSubscriber<string, ulong, bool, uint, ulong, int> _setMetaStateName;
    private readonly ICallGateSubscriber<object>                              _initialized;
    private readonly ICallGateSubscriber<object>                              _disposed;
    private readonly ICallGateSubscriber<int, uint, (int, JObject?)>          _getState;

    // Glamourer.Api registers this event under a "Penumbra." label (a quirk of its source).
    // Listen to both spellings so a future fix on their side doesn't break us.
    private readonly ICallGateSubscriber<nint, object>[] _stateChanged;

    /// <summary> Raised (on the framework thread) when Glamourer becomes available or unavailable. </summary>
    public event Action<bool>? AvailabilityChanged;

    /// <summary> Raised when Glamourer changed the state of the actor at this address (hat toggled, design applied...). </summary>
    public event Action<nint>? ActorStateChanged;

    public GlamourerIpc(IDalamudPluginInterface pi)
    {
        _apiVersion   = pi.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2");
        _setMetaState = pi.GetIpcSubscriber<int, ulong, bool, uint, ulong, int>("Glamourer.SetMetaState");
        _setMetaStateName = pi.GetIpcSubscriber<string, ulong, bool, uint, ulong, int>("Glamourer.SetMetaStateName");
        _initialized  = pi.GetIpcSubscriber<object>("Glamourer.Initialized");
        _disposed     = pi.GetIpcSubscriber<object>("Glamourer.Disposed");
        _getState     = pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
        _stateChanged =
        [
            pi.GetIpcSubscriber<nint, object>("Penumbra.StateChanged.V2"),
            pi.GetIpcSubscriber<nint, object>("Glamourer.StateChanged.V2"),
        ];

        _initialized.Subscribe(OnInitialized);
        _disposed.Subscribe(OnDisposed);

        Available = CheckAvailable();
    }

    private bool _watchingStateChanges;

    /// <summary>
    /// Glamourer reports every state change of every actor (other players included, very often with sync plugins).
    /// We only care while we hide the hat, so we only listen during that time.
    /// </summary>
    public void WatchStateChanges(bool watch)
    {
        if (watch == _watchingStateChanges)
            return;

        _watchingStateChanges = watch;
        foreach (var gate in _stateChanged)
        {
            if (watch)
                gate.Subscribe(OnStateChanged);
            else
                gate.Unsubscribe(OnStateChanged);
        }
    }

    /// <summary> Set while we call Glamourer ourselves: the change notification it sends back is our own doing. </summary>
    private bool _selfChange;

    private unsafe void OnStateChanged(nint address)
    {
        // Only our own character matters, and not our own changes. Checked before anything else (no allocation).
        if (_selfChange || address != (nint)FFXIVClientStructs.FFXIV.Client.Game.Control.Control.GetLocalPlayer())
            return;

        Plugin.Framework.RunOnFrameworkThread(() => ActorStateChanged?.Invoke(address));
    }

    public bool Available { get; private set; }

    /// <summary> Human-readable status for the settings window. </summary>
    public string Status { get; private set; } = "Not checked yet.";

    // Glamourer can raise its events from its own loading thread: always handle them on the framework thread.
    private void OnInitialized()
        => Plugin.Framework.RunOnFrameworkThread(() =>
        {
            Available = CheckAvailable();
            AvailabilityChanged?.Invoke(Available);
        });

    private void OnDisposed()
        => Plugin.Framework.RunOnFrameworkThread(() =>
        {
            Available = false;
            Status    = "Glamourer is not installed or not enabled.";
            AvailabilityChanged?.Invoke(false);
        });

    private bool CheckAvailable()
    {
        try
        {
            var (major, minor) = _apiVersion.InvokeFunc();
            if (major != 1)
            {
                Status = $"Your Glamourer version (API {major}.{minor}) is not supported yet. Please check for a Hats Off update.";
                return false;
            }

            Status = $"Glamourer connected (API {major}.{minor}).";
            return true;
        }
        catch (IpcNotReadyError)
        {
            Status = "Glamourer is not installed or not enabled.";
            return false;
        }
        catch (Exception ex)
        {
            Status = $"Could not talk to Glamourer: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Whether Glamourer currently wants the hat of this actor shown. This is the source of truth:
    /// the game's own hat flag can say "visible" while Glamourer hides the hat another way.
    /// Returns null if Glamourer couldn't tell us.
    /// </summary>
    public bool? IsHatVisible(int objectIndex)
    {
        try
        {
            var (ec, data) = _getState.InvokeFunc(objectIndex, 0u);
            if (ec != (int)ApiEc.Success || data is null)
                return null;

            var show = data["Equipment"]?["Hat"]?["Show"];
            return show is { Type: JTokenType.Boolean } ? show.Value<bool>() : null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Glamourer.GetState failed.");
            return null;
        }
    }

    /// <summary> Set hat visibility for the actor at the given object index (and every cutscene copy sharing its identity). </summary>
    public ApiEc? SetHatVisible(int objectIndex, bool visible)
    {
        try
        {
            _selfChange = true;
            return (ApiEc)_setMetaState.InvokeFunc(objectIndex, MetaFlagHatState, visible, 0u, ApplyFlagOnce);
        }
        catch (IpcNotReadyError)
        {
            // Glamourer vanished without us getting the Disposed event (shouldn't happen, but be safe).
            OnDisposed();
            return null;
        }
        catch (Exception ex)
        {
            Status = $"Could not talk to Glamourer: {ex.Message}";
            Plugin.Log.Warning(ex, "Glamourer.SetMetaState failed.");
            return null;
        }
        finally
        {
            _selfChange = false;
        }
    }

    /// <summary> Same as <see cref="SetHatVisible"/>, by character name: works even when the character isn't spawned (loading screens). </summary>
    public ApiEc? SetHatVisibleByName(string playerName, bool visible)
    {
        try
        {
            return (ApiEc)_setMetaStateName.InvokeFunc(playerName, MetaFlagHatState, visible, 0u, ApplyFlagOnce);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Glamourer.SetMetaStateName failed.");
            return null;
        }
    }

    public void Dispose()
    {
        _initialized.Unsubscribe(OnInitialized);
        _disposed.Unsubscribe(OnDisposed);
        WatchStateChanges(false);
    }
}
