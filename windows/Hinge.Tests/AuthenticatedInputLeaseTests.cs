using System.Collections.Concurrent;
using Hinge.Core;

namespace Hinge.Tests;

public class AuthenticatedInputLeaseTests
{
    [Fact]
    public async Task DesktopLeaseRoutesInputAndReleaseToItsOwnInjector()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HingeScopedInput-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var trust = new TrustStore(Path.Combine(directory, "trust.json"));
            trust.AddOrUpdate(new TrustedDevice { DeviceId = "phone", Name = "Phone" });
            var ordinary = new Injector(); var desktop = new Injector();
            using var manager = new RemoteInputManager(ordinary, trust, true, true);
            using var lease = manager.BeginAuthenticatedControl("phone", desktop);
            Assert.NotNull(lease);
            Assert.True(lease.Send(new() { ActionType = RemoteActionType.MouseMove, DeltaX = 9000, DeltaY = -9000 }));
            Assert.Equal((4096, -4096), desktop.Moves.Single());
            Assert.True(lease.Send(new() { ActionType = RemoteActionType.MouseMove, Flags = RemoteInputEvent.RelativeMouseFlag,
                DeltaX = 123, DeltaY = -234 }));
            Assert.Equal((123, -234), desktop.RelativeMoves.Single());
            Assert.True(lease.Send(new() { ActionType = RemoteActionType.MouseMove, Flags = RemoteInputEvent.AbsoluteMouseFlag,
                DeltaX = 65535, DeltaY = 32768 }));
            Assert.Equal((65535, 32768), desktop.AbsoluteMoves.Single());
            lease.Send(new() { ActionType = RemoteActionType.MouseMove, Flags = RemoteInputEvent.AbsoluteMouseFlag, DeltaX = -1 });
            lease.Send(new() { ActionType = RemoteActionType.MouseMove, Flags = RemoteInputEvent.AbsoluteMouseFlag | RemoteInputEvent.RelativeMouseFlag });
            Assert.Single(desktop.AbsoluteMoves);
            Assert.True(lease.Send(new() { ActionType = RemoteActionType.MouseDown, ButtonOrKey = 0 }));
            Assert.True(lease.Send(new() { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 87 }));
            await manager.SetEnabledAsync(false);
            Assert.Equal(new[] { true, false }, desktop.Buttons.ToArray());
            Assert.Equal(new[] { (true, (byte)87), (false, (byte)87) }, desktop.Events.ToArray());
            Assert.Empty(ordinary.Moves); Assert.Empty(ordinary.Buttons); Assert.Empty(ordinary.Events);
            await manager.SetEnabledAsync(true);
            using var replacement = manager.BeginAuthenticatedControl("phone");
            lease.Dispose();
            Assert.True(replacement!.Send(new() { ActionType = RemoteActionType.MouseMove, DeltaX = 2, DeltaY = 3 }));
            Assert.Equal((2, 3), ordinary.Moves.Single());
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task LeaseIsExclusiveAndDisableReleasesKeysWithoutLettingOldOwnerReclaim()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HingeInputTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "trust.json");
        var trust = new TrustStore(path); trust.AddOrUpdate(new TrustedDevice { DeviceId = "phone", Name = "Phone" });
        var input = new Injector();
        using var manager = new RemoteInputManager(input, trust, true, true);
        try
        {
            Assert.Null(manager.BeginAuthenticatedControl("unknown"));
            using var first = manager.BeginAuthenticatedControl("phone"); Assert.NotNull(first);
            Assert.True(manager.ActiveControlEncrypted);
            Assert.Null(manager.BeginAuthenticatedControl("phone"));
            Assert.True(first.Send(new() { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 87 }));
            Assert.True(first.Send(new() { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 87 }));
            Assert.Single(input.Events);
            Assert.True(first.Send(new() { ActionType = RemoteActionType.MouseDown, ButtonOrKey = 0 }));
            await manager.SetEnabledAsync(false);
            Assert.Equal(new[] { true, false }, input.Buttons.ToArray());
            Assert.Equal(new[] { (true, (byte)87), (false, (byte)87) }, input.Events.ToArray());
            Assert.False(first.Send(new() { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 65 }));
            await manager.SetEnabledAsync(true);
            using var second = manager.BeginAuthenticatedControl("phone"); Assert.NotNull(second);
            first.Dispose();
            Assert.True(second.Send(new() { ActionType = RemoteActionType.ControlKeepAlive }));
            Assert.True(second.Send(new() { ActionType = RemoteActionType.KeyDown, ButtonOrKey = 65 }));
            second.Dispose();
            Assert.Equal((false, (byte)65), input.Events.Last());
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
    private sealed class Injector : IInputInjector
    {
        public ConcurrentQueue<(bool, byte)> Events = new();
        public ConcurrentQueue<bool> Buttons = new();
        public ConcurrentQueue<(int, int)> Moves = new();
        public ConcurrentQueue<(int, int)> RelativeMoves = new();
        public ConcurrentQueue<(int, int)> AbsoluteMoves = new();
        public void MoveAbsoluteMouse(int x, int y) => AbsoluteMoves.Enqueue((x, y));
        public void KeyDown(byte key) => Events.Enqueue((true, key));
        public void KeyUp(byte key) => Events.Enqueue((false, key));
        public void MoveMouse(int x, int y) => Moves.Enqueue((x, y));
        public void MoveRelativeMouse(int x, int y) => RelativeMoves.Enqueue((x, y));
        public void MouseDown(RemoteMouseButton button) => Buttons.Enqueue(true);
        public void MouseUp(RemoteMouseButton button) => Buttons.Enqueue(false);
        public void MouseClick(RemoteMouseButton button) { }
        public void MouseDoubleClick() { }
        public void MouseScroll(int delta) { }
        public void SendText(string text) { }
        public void Dispose() { }
    }
}
