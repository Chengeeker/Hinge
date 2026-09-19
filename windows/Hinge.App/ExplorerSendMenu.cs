using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Hinge.App;

internal readonly record struct ExplorerSendDevice(string DeviceId, string Name);

internal sealed record ShellSendRequest(
    string DeviceId,
    IReadOnlyList<string> FilePaths)
{
    private const string ShellSendArgument = "--shell-send";
    private const string DeviceIdArgument = "--device-id";

    public static bool IsMarked(string? arguments)
    {
        return ParseArguments(arguments).Any(argument =>
            string.Equals(argument, ShellSendArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryParse(string? arguments, out ShellSendRequest? request)
    {
        request = null;
        var parsed = ParseArguments(arguments);
        var markerIndex = parsed.FindIndex(argument =>
            string.Equals(argument, ShellSendArgument, StringComparison.OrdinalIgnoreCase));
        if (markerIndex < 0) return false;

        string? deviceId = null;
        var pathStart = markerIndex + 1;
        for (var index = pathStart; index < parsed.Count; index++)
        {
            var argument = parsed[index];
            if (string.Equals(argument, DeviceIdArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= parsed.Count || string.IsNullOrWhiteSpace(parsed[index])) return false;
                deviceId = parsed[index];
                pathStart = index + 1;
                continue;
            }

            if (argument.StartsWith(DeviceIdArgument + "=", StringComparison.OrdinalIgnoreCase))
            {
                deviceId = argument[(DeviceIdArgument.Length + 1)..];
                pathStart = index + 1;
                continue;
            }

            if (string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase))
            {
                pathStart = index + 1;
                continue;
            }

            // The shell command places all selected paths after the device
            // identifier. Stop parsing switches here so a local path that
            // happens to start with a dash is not silently discarded.
            break;
        }

        if (string.IsNullOrWhiteSpace(deviceId) || pathStart >= parsed.Count) return false;

        var paths = parsed
            .Skip(pathStart)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0) return false;

        request = new ShellSendRequest(deviceId, paths);
        return true;
    }

    private static List<string> ParseArguments(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return result;

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        var index = 0;

        void FinishToken()
        {
            if (!hasToken) return;
            result.Add(current.ToString());
            current.Clear();
            hasToken = false;
        }

        while (index < commandLine.Length)
        {
            var character = commandLine[index];
            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                FinishToken();
                index++;
                continue;
            }

            if (character == '\\')
            {
                var slashStart = index;
                while (index < commandLine.Length && commandLine[index] == '\\') index++;
                var slashCount = index - slashStart;

                if (index < commandLine.Length && commandLine[index] == '"')
                {
                    current.Append('\\', slashCount / 2);
                    hasToken = true;
                    if ((slashCount & 1) != 0)
                    {
                        current.Append('"');
                        index++;
                    }
                    else if (inQuotes && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                    {
                        current.Append('"');
                        index += 2;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                        index++;
                    }
                    continue;
                }

                current.Append('\\', slashCount);
                hasToken = true;
                continue;
            }

            if (character == '"')
            {
                hasToken = true;
                if (inQuotes && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                {
                    current.Append('"');
                    index += 2;
                }
                else
                {
                    inQuotes = !inQuotes;
                    index++;
                }
                continue;
            }

            current.Append(character);
            hasToken = true;
            index++;
        }

        FinishToken();
        return result;
    }
}

internal static class ExplorerSendMenu
{
    private const string ShellParentPath = @"Software\Classes\*\shell";
    private const string SnapshotPath = @"Software\Hinge\ExplorerSend";
    private const string SnapshotDevicesKey = "Devices";
    private const string SnapshotFileName = "explorer-send.txt";
    private const string MenuKeyName = "HingeSend";
    private const string MenuTitle = "通过 Hinge 发送到";
    private static readonly object Sync = new();
    private static string? _lastSignature;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);

    public static void Refresh(string? executablePath, IEnumerable<ExplorerSendDevice> devices)
    {
        var executable = NormalizeExecutablePath(executablePath);
        var deviceList = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var signature = BuildSignature(executable, deviceList);

        lock (Sync)
        {
            if (string.Equals(_lastSignature, signature, StringComparison.Ordinal) &&
                IsLegacyMenuHealthy(executable, deviceList))
            {
                return;
            }

            try
            {
                // The packaged shell extension reads the device snapshot when
                // Explorer expands the submenu. Keep the broad association
                // notification only for registering the menu root itself;
                // changing connected devices must not invalidate Explorer's
                // global file-association and desktop-icon caches.
                var shellWasRegistered = IsLegacyMenuRegistered(executable);
                WriteSnapshot(executable, deviceList);
                using var parent = Registry.CurrentUser.CreateSubKey(ShellParentPath, writable: true);
                if (parent == null) return;

                if (executable == null)
                {
                    parent.DeleteSubKeyTree(MenuKeyName, throwOnMissingSubKey: false);
                    _lastSignature = signature;
                    if (shellWasRegistered) NotifyShell();
                    return;
                }

                // Keep the live parent in place while replacing its children.
                // Explorer can enumerate this key while a device connects or
                // disconnects; deleting it first creates a window in which
                // the command disappears from the context menu.
                using var menu = parent.CreateSubKey(MenuKeyName);
                if (menu == null) return;
                // Leave the default value unset. This is the documented
                // ExtendedSubCommandsKey shape; writing an empty REG_SZ here
                // can make Explorer treat the parent as a normal verb and
                // fall back to the selected file's default association.
                menu.DeleteValue(string.Empty, throwOnMissingValue: false);
                menu.SetValue("MUIVerb", MenuTitle, RegistryValueKind.String);
                menu.SetValue("Icon", $"{executable},0", RegistryValueKind.String);

                // ExtendedSubCommandsKey is the registry-supported cascading
                // menu shape for unpackaged desktop applications. Keeping the
                // device entries under HKCU avoids elevation and machine-wide
                // registry changes. Keep this compatibility entry even when
                // the sparse package is registered: Explorer caches packaged
                // COM extensions per shell process, and an install/update can
                // leave the current Explorer process unable to see the
                // first-layer command until it is restarted. Removing this
                // fallback based only on a registry flag would make both menu
                // locations disappear during that window.
                //
                // ExtendedSubCommandsKey is a subkey, not a REG_SZ value. The
                // child Shell/command keys belong below that subkey. Putting
                // them directly under the menu key makes Explorer treat the
                // parent as a normal file verb; invoking it then falls through
                // to the selected file association instead of sending it.
                using var extended = menu.CreateSubKey("ExtendedSubCommandsKey");
                using var shell = extended?.CreateSubKey("Shell");
                if (shell == null) return;

                var existingChildNames = shell.GetSubKeyNames();
                var currentChildNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (device, index) in deviceList.Select((device, index) => (device, index)))
                {
                    var keyName = $"device_{index}_{ShortHash(device.DeviceId)}";
                    currentChildNames.Add(keyName);
                    using var child = shell.CreateSubKey(keyName);
                    if (child == null) continue;

                    child.SetValue("MUIVerb", DisplayName(device, deviceList), RegistryValueKind.String);
                    child.SetValue("Icon", $"{executable},0", RegistryValueKind.String);
                    child.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
                    using var command = child.CreateSubKey("command");
                    command?.SetValue(
                        string.Empty,
                        $"{Quote(executable)} --silent --shell-send --device-id {Quote(device.DeviceId)} %*",
                        RegistryValueKind.String);
                }

                foreach (var staleName in existingChildNames.Where(name => !currentChildNames.Contains(name)))
                {
                    shell.DeleteSubKeyTree(staleName, throwOnMissingSubKey: false);
                }

                _lastSignature = signature;
                if (!shellWasRegistered)
                {
                    NotifyShell();
                }
            }
            catch
            {
                // The context menu is an optional integration. A locked-down
                // profile or a registry policy must not affect connectivity or
                // the main Hinge process.
            }
        }
    }

    private static bool IsLegacyMenuHealthy(
        string? executablePath,
        IReadOnlyList<ExplorerSendDevice> devices)
    {
        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(ShellParentPath, writable: false);
            using var menu = parent?.OpenSubKey(MenuKeyName, writable: false);
            if (executablePath == null)
            {
                return menu == null;
            }

            if (menu == null ||
                !string.Equals(menu.GetValue("MUIVerb") as string, MenuTitle, StringComparison.Ordinal) ||
                !string.Equals(
                    menu.GetValue("Icon") as string,
                    $"{executablePath},0",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var shell = menu.OpenSubKey("ExtendedSubCommandsKey\\Shell", writable: false);
            if (shell == null) return false;

            var expected = devices
                .Select((device, index) => $"device_{index}_{ShortHash(device.DeviceId)}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return expected.SetEquals(shell.GetSubKeyNames());
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLegacyMenuRegistered(string? executablePath)
    {
        if (executablePath == null) return false;

        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(ShellParentPath, writable: false);
            using var menu = parent?.OpenSubKey(MenuKeyName, writable: false);
            if (menu == null ||
                !string.Equals(menu.GetValue("MUIVerb") as string, MenuTitle, StringComparison.Ordinal) ||
                !string.Equals(
                    menu.GetValue("Icon") as string,
                    $"{executablePath},0",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var shell = menu.OpenSubKey("ExtendedSubCommandsKey\\Shell", writable: false);
            return shell != null;
        }
        catch
        {
            return false;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            try
            {
                using (var snapshot = Registry.CurrentUser.OpenSubKey(SnapshotPath, writable: true))
                {
                    snapshot?.DeleteSubKeyTree(SnapshotDevicesKey, throwOnMissingSubKey: false);
                    snapshot?.DeleteValue("ExecutablePath", throwOnMissingValue: false);
                }
                DeleteSnapshotFile();
                using var parent = Registry.CurrentUser.OpenSubKey(ShellParentPath, writable: true);
                parent?.DeleteSubKeyTree(MenuKeyName, throwOnMissingSubKey: false);
                _lastSignature = null;
                if (parent != null) NotifyShell();
            }
            catch
            {
                // Best effort cleanup; the next startup will overwrite stale
                // entries before publishing the current connected devices.
            }
        }
    }

    private static void WriteSnapshot(
        string? executablePath,
        IReadOnlyList<ExplorerSendDevice> devices)
    {
        WriteSnapshotFile(executablePath, devices);

        using var snapshot = Registry.CurrentUser.CreateSubKey(SnapshotPath, writable: true);
        if (snapshot == null) return;

        if (executablePath == null)
        {
            snapshot.DeleteValue("ExecutablePath", throwOnMissingValue: false);
        }
        else
        {
            snapshot.SetValue("ExecutablePath", executablePath, RegistryValueKind.String);
        }

        snapshot.DeleteSubKeyTree(SnapshotDevicesKey, throwOnMissingSubKey: false);
        using var deviceRoot = snapshot.CreateSubKey(SnapshotDevicesKey, writable: true);
        if (deviceRoot == null) return;

        foreach (var device in devices)
        {
            using var child = deviceRoot.CreateSubKey(ShortHash(device.DeviceId), writable: true);
            child?.SetValue("DeviceId", device.DeviceId, RegistryValueKind.String);
            child?.SetValue("Name", device.Name, RegistryValueKind.String);
        }
    }

    private static void WriteSnapshotFile(
        string? executablePath,
        IReadOnlyList<ExplorerSendDevice> devices)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hinge");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SnapshotFileName);
        var temporaryPath = path + ".tmp";
        var lines = new List<string>
        {
            "version\t1",
            $"executable\t{SanitizeSnapshotValue(executablePath)}",
        };
        lines.AddRange(devices.Select(device =>
            $"device\t{SanitizeSnapshotValue(device.DeviceId)}\t{SanitizeSnapshotValue(device.Name)}"));
        File.WriteAllLines(
            temporaryPath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void DeleteSnapshotFile()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hinge",
            SnapshotFileName);
        File.Delete(path);
        File.Delete(path + ".tmp");
    }

    private static string SanitizeSnapshotValue(string? value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string? NormalizeExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSignature(
        string? executablePath,
        IReadOnlyList<ExplorerSendDevice> devices)
    {
        var raw = new StringBuilder(executablePath ?? string.Empty);
        foreach (var device in devices)
        {
            raw.Append('\n').Append(device.DeviceId).Append('\n').Append(device.Name);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToString())));
    }

    private static string DisplayName(
        ExplorerSendDevice device,
        IReadOnlyList<ExplorerSendDevice> devices)
    {
        var name = string.IsNullOrWhiteSpace(device.Name) ? "已连接设备" : device.Name.Trim();
        if (devices.Count(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) <= 1)
        {
            return name;
        }

        var shortId = device.DeviceId.Length <= 8 ? device.DeviceId : device.DeviceId[..8];
        return $"{name} ({shortId})";
    }

    private static string ShortHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var slashCount = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', slashCount * 2 + 1);
                builder.Append('"');
                slashCount = 0;
                continue;
            }

            builder.Append('\\', slashCount);
            slashCount = 0;
            builder.Append(character);
        }

        builder.Append('\\', slashCount * 2);
        builder.Append('"');
        return builder.ToString();
    }

    private static void NotifyShell()
    {
        const uint ShcneAssocChanged = 0x08000000;
        const uint ShcnfIdList = 0x0000;
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }
}
