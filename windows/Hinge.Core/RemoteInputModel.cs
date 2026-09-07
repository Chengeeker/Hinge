using System.Buffers.Binary;
using System.Text;

namespace Hinge.Core;

public enum RemoteActionType : byte
{
    MouseMove        = 0x01,
    MouseDown        = 0x02,
    MouseUp          = 0x03,
    MouseClick       = 0x04,
    MouseDoubleClick = 0x05,
    MouseScroll      = 0x06,
    KeyDown          = 0x10,
    KeyUp            = 0x11,
    TextInput        = 0x12
}

public enum RemoteMouseButton : byte
{
    Left   = 0,
    Right  = 1,
    Middle = 2
}

public class RemoteInputEvent
{
    public const int HeaderSize = 16;

    public RemoteActionType ActionType { get; set; }
    public byte ButtonOrKey { get; set; }
    public ushort Flags { get; set; }
    public int DeltaX { get; set; }
    public int DeltaY { get; set; }
    public int WheelOrData { get; set; }
    public string? TextPayload { get; set; }

    public byte[] Serialize()
    {
        byte[]? textBytes = null;
        int totalSize = HeaderSize;
        if (ActionType == RemoteActionType.TextInput && !string.IsNullOrEmpty(TextPayload))
        {
            textBytes = Encoding.UTF8.GetBytes(TextPayload);
            totalSize += textBytes.Length;
        }

        byte[] buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        span[0] = (byte)ActionType;
        span[1] = ButtonOrKey;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), Flags);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(4, 4), DeltaX);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(8, 4), DeltaY);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(12, 4), WheelOrData);

        if (textBytes != null && textBytes.Length > 0)
        {
            textBytes.CopyTo(span.Slice(HeaderSize));
        }

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out RemoteInputEvent? result)
    {
        result = null;
        if (span.Length < HeaderSize) return false;

        var ev = new RemoteInputEvent
        {
            ActionType = (RemoteActionType)span[0],
            ButtonOrKey = span[1],
            Flags = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2)),
            DeltaX = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)),
            DeltaY = BinaryPrimitives.ReadInt32BigEndian(span.Slice(8, 4)),
            WheelOrData = BinaryPrimitives.ReadInt32BigEndian(span.Slice(12, 4))
        };

        if (ev.ActionType == RemoteActionType.TextInput && span.Length > HeaderSize)
        {
            ev.TextPayload = Encoding.UTF8.GetString(span.Slice(HeaderSize));
        }

        result = ev;
        return true;
    }
}
