namespace Hinge.Core;

public interface IInputInjector : IDisposable
{
    void MoveMouse(int deltaX, int deltaY);
    void MouseDown(RemoteMouseButton button);
    void MouseUp(RemoteMouseButton button);
    void MouseClick(RemoteMouseButton button);
    void MouseDoubleClick();
    void MouseScroll(int delta);
    void SendText(string text);
}
