using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public class OcrResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public TimeSpan Elapsed { get; set; }
}

public interface IOcrEngine
{
    bool IsAvailable { get; }
    Task<OcrResult> RecognizeTextAsync(byte[] imageBytes, string language = "en-US");
}

public class MockOcrEngine : IOcrEngine
{
    public bool IsAvailable => true;
    public string MockRecognizedText { get; set; } = "Hinge Native OCR: Clean text recognized from sample image.";

    public Task<OcrResult> RecognizeTextAsync(byte[] imageBytes, string language = "en-US")
    {
        var sw = Stopwatch.StartNew();
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return Task.FromResult(new OcrResult
            {
                Success = false,
                ErrorMessage = "Image payload is empty or invalid.",
                Elapsed = sw.Elapsed
            });
        }

        sw.Stop();
        return Task.FromResult(new OcrResult
        {
            Success = true,
            Text = MockRecognizedText,
            Elapsed = sw.Elapsed
        });
    }
}

public class ToolCommandMessage
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("toolType")]
    public string ToolType { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ToolCommandMessage? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ToolCommandMessage>(json); }
        catch { return null; }
    }
}

public class ToolResultMessage
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("resultText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResultText { get; set; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ToolResultMessage? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ToolResultMessage>(json); }
        catch { return null; }
    }
}
