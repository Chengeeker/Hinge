using System.IO;
using System.Text.Json;
using Hinge.Core;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace Hinge.App;

public sealed record WebDavSettingsState
{
    public List<WebDavProfile> Profiles { get; init; } = [];
    public List<string> TabOrder { get; init; } = [];
}

internal static class WebDavSettingsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hinge", "webdav-settings.json");
    private static readonly object Gate = new();
    public static event EventHandler? Changed;

    public static WebDavSettingsState Load()
    {
        lock (Gate)
        {
            if (!File.Exists(StorePath)) return new();
            return JsonSerializer.Deserialize<WebDavSettingsState>(File.ReadAllText(StorePath))
                ?? throw new InvalidDataException("WebDAV 配置为空或损坏；原文件未覆盖。");
        }
    }

    private static void Write(WebDavSettingsState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temporary = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, StorePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Upsert(WebDavProfile profile)
    {
        lock (Gate)
        {
            var state = Load();
            int index = state.Profiles.FindIndex(item => item.Id == profile.Id);
            if (index < 0) state.Profiles.Add(profile); else state.Profiles[index] = profile;
            Write(state);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Remove(string id)
    {
        lock (Gate)
        {
            var state = Load();
            state.Profiles.RemoveAll(profile => profile.Id == id);
            state.TabOrder.RemoveAll(item => item == id);
            Write(state);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void SaveOrder(IEnumerable<string> ids)
    {
        lock (Gate)
        {
            var state = Load();
            Write(state with { TabOrder = ids.Distinct().ToList() });
        }
    }

    public static async Task<string> ProtectAsync(string password)
    {
        if (password.Length == 0) return "";
        var input = CryptographicBuffer.ConvertStringToBinary(password, BinaryStringEncoding.Utf8);
        var output = await new DataProtectionProvider("LOCAL=user").ProtectAsync(input);
        return CryptographicBuffer.EncodeToBase64String(output);
    }

    public static async Task<string> UnprotectAsync(string protectedPassword)
    {
        if (protectedPassword.Length == 0) return "";
        try
        {
            var output = await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.DecodeFromBase64String(protectedPassword));
            return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, output);
        }
        catch { throw new InvalidOperationException("无法解密此 WebDAV 密码，请在当前 Windows 账户下重新填写并保存。"); }
    }
}
