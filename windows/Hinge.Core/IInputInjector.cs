namespace Hinge.Core;

public interface IInputInjector : IDisposable
{
    void MoveMouse(int deltaX, int deltaY);
    void MoveAbsoluteMouse(int x, int y) => throw new NotSupportedException("Absolute desktop input is unavailable.");
    void MoveRelativeMouse(int deltaX, int deltaY) => MoveMouse(deltaX, deltaY);
    void MouseDown(RemoteMouseButton button);
    void MouseUp(RemoteMouseButton button);
    void MouseClick(RemoteMouseButton button);
    void MouseDoubleClick();
    void MouseScroll(int delta);
    void SendText(string text);
    void KeyDown(byte virtualKey);
    void KeyUp(byte virtualKey);
}
