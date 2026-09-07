using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class MockInputInjector : IInputInjector
{
    public int LastMoveX { get; private set; }
    public int LastMoveY { get; private set; }
    public RemoteMouseButton? LastClickedButton { get; private set; }
    public int DoubleClickCount { get; private set; }
    public int LastScrollDelta { get; private set; }
    public string? LastText { get; private set; }

    public void MoveMouse(int deltaX, int deltaY)
    {
        LastMoveX += deltaX;
        LastMoveY += deltaY;
    }

    public void MouseDown(RemoteMouseButton button) { }
    public void MouseUp(RemoteMouseButton button) { }

    public void MouseClick(RemoteMouseButton button)
    {
        LastClickedButton = button;
    }

    public void MouseDoubleClick()
    {
        DoubleClickCount++;
    }

    public void MouseScroll(int delta)
    {
        LastScrollDelta = delta;
    }

    public void SendText(string text)
    {
        LastText = text;
    }

    public void Dispose() { }
}

public class RemoteInputTests
{
    [Fact]
    public void RemoteInputEvent_MouseMove_SerializationRoundtrip()
    {
        var ev = new RemoteInputEvent
        {
            ActionType = RemoteActionType.MouseMove,
            DeltaX = -45,
            DeltaY = 120
        };

        byte[] bytes = ev.Serialize();
        Assert.Equal(16, bytes.Length);

        bool success = RemoteInputEvent.TryParse(bytes, out var parsed);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(RemoteActionType.MouseMove, parsed.ActionType);
        Assert.Equal(-45, parsed.DeltaX);
        Assert.Equal(120, parsed.DeltaY);
    }

    [Fact]
    public void RemoteInputEvent_MouseScroll_SerializationRoundtrip()
    {
        var ev = new RemoteInputEvent
        {
            ActionType = RemoteActionType.MouseScroll,
            WheelOrData = -120
        };

        byte[] bytes = ev.Serialize();
        Assert.Equal(16, bytes.Length);

        bool success = RemoteInputEvent.TryParse(bytes, out var parsed);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(RemoteActionType.MouseScroll, parsed.ActionType);
        Assert.Equal(-120, parsed.WheelOrData);
    }

    [Fact]
    public void RemoteInputEvent_TextInput_SerializationRoundtrip()
    {
        var ev = new RemoteInputEvent
        {
            ActionType = RemoteActionType.TextInput,
            TextPayload = "Hello from Android Remote Keyboard 🚀"
        };

        byte[] bytes = ev.Serialize();
        Assert.True(bytes.Length > 16);

        bool success = RemoteInputEvent.TryParse(bytes, out var parsed);
        Assert.True(success);
        Assert.NotNull(parsed);
        Assert.Equal(RemoteActionType.TextInput, parsed.ActionType);
        Assert.Equal("Hello from Android Remote Keyboard 🚀", parsed.TextPayload);
    }

    [Fact]
    public void RemoteInputManager_DispatchesToInjector()
    {
        var injector = new MockInputInjector();
        var trustStore = new TrustStore();
        using var manager = new RemoteInputManager(injector, trustStore);

        // Move
        var moveEvent = new RemoteInputEvent
        {
            ActionType = RemoteActionType.MouseMove,
            DeltaX = 15,
            DeltaY = 30
        };
        var moveFrame = new ProtocolFrame { Type = MessageType.RemoteInput, Payload = moveEvent.Serialize() };
        bool handled = manager.HandleIncomingFrame(null!, moveFrame);
        Assert.True(handled);
        Assert.Equal(15, injector.LastMoveX);
        Assert.Equal(30, injector.LastMoveY);

        // Click Right
        var clickEvent = new RemoteInputEvent
        {
            ActionType = RemoteActionType.MouseClick,
            ButtonOrKey = (byte)RemoteMouseButton.Right
        };
        var clickFrame = new ProtocolFrame { Type = MessageType.RemoteInput, Payload = clickEvent.Serialize() };
        manager.HandleIncomingFrame(null!, clickFrame);
        Assert.Equal(RemoteMouseButton.Right, injector.LastClickedButton);

        // Text input
        var textEvent = new RemoteInputEvent
        {
            ActionType = RemoteActionType.TextInput,
            TextPayload = "Testing Text Input"
        };
        var textFrame = new ProtocolFrame { Type = MessageType.RemoteInput, Payload = textEvent.Serialize() };
        manager.HandleIncomingFrame(null!, textFrame);
        Assert.Equal("Testing Text Input", injector.LastText);
    }
}
