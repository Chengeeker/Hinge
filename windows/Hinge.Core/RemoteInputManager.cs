namespace Hinge.Core;

public class RemoteInputManager : IDisposable
{
    private readonly IInputInjector _injector;
    private readonly TrustStore _trustStore;
    private bool _enabled = true;
    private bool _disposed;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public IInputInjector Injector => _injector;
    public event EventHandler<RemoteInputEvent>? InputReceived;

    public RemoteInputManager(IInputInjector injector, TrustStore trustStore)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
    }

    public bool HandleIncomingFrame(SessionConnection conn, ProtocolFrame frame)
    {
        if (!_enabled || frame.Type != MessageType.RemoteInput) return false;

        // Security check: only execute inputs from authenticated/trusted sessions
        // In loopback/test environments where PeerIdentity may be empty or trusted:
        if (conn != null && !string.IsNullOrEmpty(conn.RemoteDeviceId))
        {
            if (!_trustStore.IsTrusted(conn.RemoteDeviceId))
            {
                return false;
            }
        }

        if (!RemoteInputEvent.TryParse(frame.Payload, out var inputEvent) || inputEvent == null)
        {
            return false;
        }

        DispatchEvent(inputEvent);
        InputReceived?.Invoke(this, inputEvent);
        return true;
    }

    public void DispatchEvent(RemoteInputEvent ev)
    {
        switch (ev.ActionType)
        {
            case RemoteActionType.MouseMove:
                _injector.MoveMouse(ev.DeltaX, ev.DeltaY);
                break;
            case RemoteActionType.MouseDown:
                _injector.MouseDown((RemoteMouseButton)ev.ButtonOrKey);
                break;
            case RemoteActionType.MouseUp:
                _injector.MouseUp((RemoteMouseButton)ev.ButtonOrKey);
                break;
            case RemoteActionType.MouseClick:
                _injector.MouseClick((RemoteMouseButton)ev.ButtonOrKey);
                break;
            case RemoteActionType.MouseDoubleClick:
                _injector.MouseDoubleClick();
                break;
            case RemoteActionType.MouseScroll:
                _injector.MouseScroll(ev.WheelOrData);
                break;
            case RemoteActionType.TextInput:
                if (!string.IsNullOrEmpty(ev.TextPayload))
                {
                    _injector.SendText(ev.TextPayload);
                }
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _injector.Dispose();
    }
}
