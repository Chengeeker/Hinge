using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public sealed class GameLayoutDocument
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("selected")] public string? Selected { get; set; }
    [JsonPropertyName("presets")] public List<GameLayoutPreset> Presets { get; set; } = [];

    public void Validate()
    {
        if (Version != 1 || Presets is null || Presets.Count is < 1 or > 32)
            throw new FormatException("无效游戏布局文档。");
        var presetIds = new HashSet<string>(StringComparer.Ordinal);
        int controlCount = 0;
        foreach (var preset in Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || preset.Id.Length > 80 ||
                !presetIds.Add(preset.Id) || string.IsNullOrWhiteSpace(preset.Name) ||
                preset.Name.Length > 40 || preset.Controls is null || preset.Controls.Count > 64)
                throw new FormatException("游戏布局预设无效。");
            var controlIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var control in preset.Controls)
            {
                if (string.IsNullOrWhiteSpace(control.Id) || control.Id.Length > 80 ||
                    !controlIds.Add(control.Id) || control.Label is null || control.Label.Length > 64 ||
                    !double.IsFinite(control.X) || control.X is < 0 or > 1 ||
                    !double.IsFinite(control.Y) || control.Y is < 0 or > 1 ||
                    !double.IsFinite(control.Size) || control.Size is < .10 or > .48 ||
                    !double.IsFinite(control.Aspect) || control.Aspect is < .5 or > 3 ||
                    !ValidShape(control.Shape) || !double.IsFinite(control.CornerRadius) ||
                    control.CornerRadius is < 0 or > .5 ||
                    !ValidKey(control.Binding) || control.Directions is null || control.Directions.Length != 4 ||
                    control.Directions.Any(direction => !ValidKey(direction)) ||
                    control.Image is { Length: > 4096 })
                    throw new FormatException("游戏布局控件无效。");
                controlCount++;
            }
        }
        if (controlCount > 2048 || Selected != null && !presetIds.Contains(Selected))
            throw new FormatException("游戏布局选择或控件数量无效。");
    }

    private static bool ValidKey(int key) =>
        key == 0 || key is >= 8 and <= 254 || key is >= -3 and <= -1;

    private static bool ValidShape(string? shape) =>
        shape is "auto" or "circle" or "rounded" or "rect";
}

public sealed class GameLayoutPreset
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("controls")] public List<GameLayoutControl> Controls { get; set; } = [];
}

public sealed class GameLayoutControl
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("x")] public double X { get; set; } = .5;
    [JsonPropertyName("y")] public double Y { get; set; } = .5;
    [JsonPropertyName("size")] public double Size { get; set; } = .15;
    [JsonPropertyName("aspect")] public double Aspect { get; set; } = 1;
    [JsonPropertyName("shape")] public string Shape { get; set; } = "auto";
    [JsonPropertyName("cornerRadius")] public double CornerRadius { get; set; } = .12;
    [JsonPropertyName("binding")] public int Binding { get; set; }
    [JsonPropertyName("joystick")] public bool Joystick { get; set; }
    [JsonPropertyName("directions")] public int[] Directions { get; set; } = [87, 65, 83, 68];
}

public static class GameLayoutProtocol
{
    public const int MaxDocumentBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static byte[] RequestPayload() => Encoding.UTF8.GetBytes("{\"version\":1,\"action\":\"get\"}");

    public static byte[] SetPayload(GameLayoutDocument document)
    {
        document.Validate();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            action = "set",
            document
        });
        if (payload.Length > MaxDocumentBytes) throw new FormatException("游戏布局数据超过1MiB。");
        return payload;
    }

    public static bool TryRead(byte[] payload, out string action, out GameLayoutDocument? document)
    {
        action = string.Empty;
        document = null;
        if (payload.Length is 0 or > MaxDocumentBytes) return false;
        try
        {
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 20 });
            var root = json.RootElement;
            if (!root.TryGetProperty("version", out var version) || version.GetInt32() != 1 ||
                !root.TryGetProperty("action", out var actionElement)) return false;
            action = actionElement.GetString() ?? string.Empty;
            if (action is "data" or "set")
            {
                if (!root.TryGetProperty("document", out var data)) return false;
                document = JsonSerializer.Deserialize<GameLayoutDocument>(data.GetRawText(), JsonOptions);
                document?.Validate();
                return document != null;
            }
            return action is "get" or "ack" or "error";
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            action = string.Empty;
            document = null;
            return false;
        }
    }
}
