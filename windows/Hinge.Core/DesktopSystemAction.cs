namespace Hinge.Core;

// Only the authenticated desktop media session may dispatch these operations.
public enum DesktopSystemAction : byte
{
    Lock = 1,
    Restart = 2,
    Shutdown = 3,
}
