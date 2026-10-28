namespace Hinge.Core;

public class RemoteInputManager : IDisposable
{
    private readonly IInputInjector _injector;
    private readonly TrustStore _trustStore;
    private readonly object _sync = new();
    private readonly HashSet<RemoteMouseButton> _pressedButtons = new();
    private readonly HashSet<byte> _pressedKeys = new();
    private readonly Dictionary<SessionConnection, (byte[] Challenge, RemoteInputCipher Cipher)> _secure = new();
    private byte[]? _controlKey;
    private bool _encryptionRequired;
    private bool _transitioning;

    public void SetControlKey(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Invalid control key.");
        lock (_sync)
        {
            if (_activeConnection is { } active) EndControlLocked(active);
            foreach (var pair in _secure) { pair.Key.StateChanged -= SecureConnection_StateChanged; pair.Value.Cipher.Dispose(); }
            _secure.Clear();
            if (_controlKey != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(_controlKey);
            _controlKey = key.ToArray();
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SecureConnection_StateChanged(object? sender, SessionState state)
    {
        if (state == SessionState.Connected || sender is not SessionConnection conn) return;
        lock (_sync)
        {
            if (_secure.Remove(conn, out var channel)) { conn.StateChanged -= SecureConnection_StateChanged; channel.Cipher.Dispose(); }
        }
    }
    private SessionConnection? _activeConnection;
    private bool _enabled;
    private bool _disposed;
    private readonly Timer _watchdog;
    private long _lastInputAt;

    public bool Enabled { get { lock (_sync) return _enabled; } }
    public bool EncryptionRequired { get { lock (_sync) return _encryptionRequired; } }
    public string? ActiveDeviceName
    {
        get
        {
            lock (_sync)
            {
                var id = _activeConnection?.RemoteDeviceId;
                return id == null ? null : _trustStore.GetDevice(id)?.Name ?? "已连接手机";
            }
        }
    }

    public event EventHandler? StatusChanged;

    public RemoteInputManager(IInputInjector injector, TrustStore trustStore, bool enabled = false, bool encryptionRequired = true)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _trustStore = trustStore ?? throw new ArgumentNullException(nameof(trustStore));
        _enabled = enabled;
        _encryptionRequired = encryptionRequired;
        _watchdog = new Timer(state => { _ = ExpireControlAsync(); }, null, 1000, 1000);
    }

    private async Task ExpireControlAsync()
    {
        SessionConnection? stopped = null;
        lock (_sync)
        {
            if (_activeConnection is { } active && Environment.TickCount64 - _lastInputAt >= 3000)
            {
                stopped = active;
                EndControlLocked(active);
            }
        }
        if (stopped == null) return;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        await SendControlStatusAsync(stopped, RemoteActionType.ControlEnded);
    }

    public async Task<bool> HandleIncomingFrameAsync(SessionConnection? conn, ProtocolFrame frame)
    {
        if (frame.Type is not (MessageType.RemoteInput or MessageType.SecureRemoteInput) || !IsTrustedConnectedSession(conn)) return false;
        if (frame.Type == MessageType.RemoteInput)
        {
            if (!RemoteInputEvent.TryParse(frame.Payload, out var plainEvent) || plainEvent == null) return false;
            if (plainEvent.ActionType == RemoteActionType.ControlProbe)
            {
                byte[]? challenge = null;
                lock (_sync)
                {
                    if (!_enabled || _transitioning || _disposed) return false;
                    if (_encryptionRequired)
                    {
                        if (_controlKey == null) return false;
                        if (_secure.Remove(conn!, out var previous))
                        {
                            EndControlLocked(conn!);
                            previous.Cipher.Dispose();
                            conn!.StateChanged -= SecureConnection_StateChanged;
                        }
                        if (_secure.Count >= 8) return false;
                        challenge = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                        _secure.Add(conn!, (challenge, new RemoteInputCipher(_controlKey, challenge, true)));
                        conn!.StateChanged += SecureConnection_StateChanged;
                    }
                }
                if (challenge != null)
                {
                    var response = new byte[48]; response[0] = 0x31; challenge.CopyTo(response, 16);
                    await conn!.SendFrameAsync(MessageType.RemoteInput, response);
                    return true;
                }
                plainEvent.ActionType = RemoteActionType.ControlStart;
            }
            else
            {
                lock (_sync) if (_encryptionRequired || _transitioning) return false;
            }
            frame = new ProtocolFrame { Type = MessageType.RemoteInput, Payload = plainEvent.Serialize() };
        }
        else
        {
            byte[] decrypted;
            lock (_sync)
            {
                if (_transitioning || !_secure.TryGetValue(conn!, out var channel) || !channel.Cipher.TryDecrypt(frame.Payload, out decrypted)) return false;
            }
            frame = new ProtocolFrame { Type = MessageType.SecureRemoteInput, Payload = decrypted };
        }
        if (frame.Payload.Length > RemoteInputEvent.HeaderSize + 16_384) return false;
        if (!RemoteInputEvent.TryParse(frame.Payload, out var inputEvent) || inputEvent == null) return false;

        if (inputEvent.ActionType == RemoteActionType.ControlStart)
        {
            RemoteActionType reply;
            bool changed = false;
            lock (_sync)
            {
                if (_transitioning) return false;
                if (!_enabled)
                {
                    reply = RemoteActionType.ControlDenied;
                }
                else if (_activeConnection != null && !ReferenceEquals(_activeConnection, conn))
                {
                    reply = RemoteActionType.ControlDenied;
                }
                else
                {
                    changed = _activeConnection == null;
                    if (changed)
                    {
                        _activeConnection = conn;
                        conn!.StateChanged += ActiveConnection_StateChanged;
                    }
                    reply = RemoteActionType.ControlAccepted;
                    _lastInputAt = Environment.TickCount64;
                }
            }

            if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
            await SendControlStatusAsync(conn!, reply);
            return true;
        }

        if (inputEvent.ActionType == RemoteActionType.ControlStop)
        {
            bool changed;
            lock (_sync) changed = EndControlLocked(conn!);
            if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        lock (_sync)
        {
            if (_transitioning || !_enabled || !ReferenceEquals(_activeConnection, conn)) return false;
            _lastInputAt = Environment.TickCount64;
            DispatchEventLocked(inputEvent);
        }
        return true;
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        SessionConnection? stoppedConnection = null;
        lock (_sync)
        {
            if (_disposed || _enabled == enabled) return;
            _enabled = enabled;
            if (!enabled && _activeConnection is { } active)
            {
                stoppedConnection = active;
                EndControlLocked(active);
            }
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
        if (stoppedConnection != null)
            await SendControlStatusAsync(stoppedConnection, RemoteActionType.ControlEnded);
    }

    public async Task SetEncryptionRequiredAsync(bool required)
    {
        SessionConnection? stoppedConnection;
        lock (_sync)
        {
            if (_disposed || _encryptionRequired == required) return;
            _transitioning = true;
            stoppedConnection = _activeConnection;
            if (stoppedConnection != null) EndControlLocked(stoppedConnection);
        }
        if (stoppedConnection != null)
            await SendControlStatusAsync(stoppedConnection, RemoteActionType.ControlEnded);
        lock (_sync)
        {
            foreach (var pair in _secure)
            {
                pair.Key.StateChanged -= SecureConnection_StateChanged;
                pair.Value.Cipher.Dispose();
            }
            _secure.Clear();
            _encryptionRequired = required;
            _transitioning = false;
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsTrustedConnectedSession(SessionConnection? conn)
    {
        if (conn == null || conn.State != SessionState.Connected ||
            !string.Equals(conn.PeerInfo?.Platform, "android", StringComparison.OrdinalIgnoreCase)) return false;
        var deviceId = conn.RemoteDeviceId;
        return !string.IsNullOrWhiteSpace(deviceId) && _trustStore.IsTrusted(deviceId);
    }

    private void ActiveConnection_StateChanged(object? sender, SessionState state)
    {
        if (state == SessionState.Connected || sender is not SessionConnection connection) return;
        bool changed;
        lock (_sync) changed = EndControlLocked(connection);
        if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool EndControlLocked(SessionConnection connection)
    {
        if (!ReferenceEquals(_activeConnection, connection)) return false;
        _activeConnection.StateChanged -= ActiveConnection_StateChanged;
        _activeConnection = null;
        ReleasePressedButtonsLocked();
        return true;
    }

    private void DispatchEventLocked(RemoteInputEvent ev)
    {
        if ((ev.ActionType is RemoteActionType.MouseDown or RemoteActionType.MouseUp or RemoteActionType.MouseClick) &&
            !Enum.IsDefined((RemoteMouseButton)ev.ButtonOrKey)) return;
        switch (ev.ActionType)
        {
            case RemoteActionType.MouseMove:
                _injector.MoveMouse(Math.Clamp(ev.DeltaX, -4096, 4096), Math.Clamp(ev.DeltaY, -4096, 4096));
                break;
            case RemoteActionType.MouseDown:
                var downButton = (RemoteMouseButton)ev.ButtonOrKey;
                if (_pressedButtons.Add(downButton)) _injector.MouseDown(downButton);
                break;
            case RemoteActionType.MouseUp:
                var upButton = (RemoteMouseButton)ev.ButtonOrKey;
                _pressedButtons.Remove(upButton);
                _injector.MouseUp(upButton);
                break;
            case RemoteActionType.MouseClick:
                _injector.MouseClick((RemoteMouseButton)ev.ButtonOrKey);
                break;
            case RemoteActionType.MouseDoubleClick:
                _injector.MouseDoubleClick();
                break;
            case RemoteActionType.MouseScroll:
                _injector.MouseScroll(Math.Clamp(ev.WheelOrData, -1200, 1200));
                break;
            case RemoteActionType.KeyDown:
                if (ev.ButtonOrKey >= 8 && ev.ButtonOrKey < 255 && _pressedKeys.Add(ev.ButtonOrKey)) _injector.KeyDown(ev.ButtonOrKey);
                break;
            case RemoteActionType.KeyUp:
                if (_pressedKeys.Remove(ev.ButtonOrKey)) _injector.KeyUp(ev.ButtonOrKey);
                break;
            case RemoteActionType.TextInput:
                if (!string.IsNullOrEmpty(ev.TextPayload) && ev.TextPayload.Length <= 8192)
                    _injector.SendText(ev.TextPayload);
                break;
        }
    }

    private void ReleasePressedButtonsLocked()
    {
        foreach (var key in _pressedKeys) _injector.KeyUp(key);
        _pressedKeys.Clear();
        foreach (var button in _pressedButtons) _injector.MouseUp(button);
        _pressedButtons.Clear();
    }

    private async Task SendControlStatusAsync(SessionConnection connection, RemoteActionType action)
    {
        try
        {
            byte[] payload;
            MessageType frameType;
            lock (_sync)
            {
                if (_secure.TryGetValue(connection, out var channel))
                {
                    payload = channel.Cipher.Encrypt(new RemoteInputEvent { ActionType = action, Flags = 1 }.Serialize());
                    frameType = MessageType.SecureRemoteInput;
                }
                else if (!_encryptionRequired)
                {
                    payload = new RemoteInputEvent { ActionType = action, Flags = 3 }.Serialize();
                    frameType = MessageType.RemoteInput;
                }
                else return;
            }
            await connection.SendFrameAsync(frameType, payload);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The control status is best-effort when a peer disconnects during the transition.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _watchdog.Dispose();
            if (_activeConnection is { } active) EndControlLocked(active);
            _enabled = false;
            foreach (var pair in _secure) { pair.Key.StateChanged -= SecureConnection_StateChanged; pair.Value.Cipher.Dispose(); }
            _secure.Clear();
            if (_controlKey != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(_controlKey);
        }
        _injector.Dispose();
    }
}
