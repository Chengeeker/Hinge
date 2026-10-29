namespace Hinge.Core;

public sealed record DesktopPointerBounds(int Left, int Top, int Width, int Height)
{
    public void Validate()
    {
        if (Width is < 2 or > 65536 || Height is < 2 or > 65536 ||
            (long)Left + Width - 1 > int.MaxValue || (long)Top + Height - 1 > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(DesktopPointerBounds));
    }

    public (int X, int Y) Move(int x, int y, int deltaX, int deltaY)
    {
        Validate();
        if (x < Left || (long)x >= (long)Left + Width || y < Top || (long)y >= (long)Top + Height) {
            x = Left + Width / 2; y = Top + Height / 2;
        }
        return ((int)Math.Clamp((long)x + deltaX, Left, (long)Left + Width - 1),
            (int)Math.Clamp((long)y + deltaY, Top, (long)Top + Height - 1));
    }

    public (int X, int Y) Normalize(int x, int y)
    {
        Validate();
        // Target pixel centers so rounding cannot put a boundary click on the adjacent display.
        return ((int)((Math.Clamp((long)x - Left, 0, Width - 1L) * 65536 + 32768) / Width),
            (int)((Math.Clamp((long)y - Top, 0, Height - 1L) * 65536 + 32768) / Height));
    }

    public (int X, int Y) FromNormalized(int x, int y)
    {
        Validate();
        if (x is < 0 or > 65535 || y is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(x));
        return (Left + (int)((long)x * (Width - 1) / 65535),
            Top + (int)((long)y * (Height - 1) / 65535));
    }


}
