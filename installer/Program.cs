using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hinge.Setup;

internal static class Program
{
    private const string PayloadMarker = "HINGE_PAYLOAD_V1";
    private const string UninstallRegistryPath =
        "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Hinge";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }

    private sealed class InstallerForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly Button _browseButton;
        private readonly Button _installButton;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly string _defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Hinge");

        public InstallerForm()
        {
            var existingPath = FindExistingInstallPath();

            Text = "Hinge 安装程序";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            // Keep enough horizontal room for the Chinese browse label even
            // when the installer is rendered with non-default system scaling.
            ClientSize = new Size(810, 270);
            Font = new Font("Segoe UI", 10F);

            var title = new Label
            {
                Text = "安装 Hinge",
                AutoSize = true,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(28, 24)
            };
            var description = new Label
            {
                Text = "选择安装位置。选择磁盘根目录时会自动创建 Hinge 文件夹；更新会自动使用原安装目录。",
                AutoSize = true,
                Location = new Point(30, 68),
                ForeColor = Color.DimGray
            };
            var pathLabel = new Label
            {
                Text = "安装目录",
                AutoSize = true,
                Location = new Point(30, 116)
            };
            _pathBox = new TextBox
            {
                Text = existingPath ?? _defaultPath,
                Location = new Point(30, 142),
                Width = 580,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _browseButton = new Button
            {
                Text = "浏览",
                Location = new Point(630, 140),
                Width = 150,
                Height = 34,
                AutoSize = false,
                Padding = new Padding(0),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = false,
                UseCompatibleTextRendering = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _browseButton.Click += BrowseButton_Click;
            _statusLabel = new Label
            {
                Text = existingPath == null
                    ? "准备安装"
                    : $"检测到现有安装，将更新：{existingPath}",
                AutoSize = true,
                Location = new Point(30, 190),
                ForeColor = Color.DimGray
            };
            _progressBar = new ProgressBar
            {
                Location = new Point(30, 216),
                Width = 580,
                Height = 18,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            _installButton = new Button
            {
                Text = existingPath == null ? "安装" : "更新",
                Location = new Point(630, 208),
                Width = 150,
                Height = 34,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                UseCompatibleTextRendering = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _installButton.Click += InstallButton_Click;

            Controls.AddRange(new Control[]
            {
                title, description, pathLabel, _pathBox, _browseButton,
                _statusLabel, _progressBar, _installButton
            });
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择 Hinge 的安装目录",
                SelectedPath = _pathBox.Text.Trim(),
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.SelectedPath;
            }
        }

        private async void InstallButton_Click(object? sender, EventArgs e)
        {
            var selectedPath = _pathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                MessageBox.Show(this, "请选择安装目录。", "Hinge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var installPath = NormalizeInstallPath(selectedPath);
                if (!string.Equals(selectedPath, installPath, StringComparison.OrdinalIgnoreCase))
                {
                    _pathBox.Text = installPath;
                }

                if (IsHingeRunning())
                {
                    var answer = MessageBox.Show(
                        this,
                        "检测到 Hinge 正在运行。继续安装会自动关闭正在运行的 Hinge，是否继续？",
                        "Hinge",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) return;
                }

                SetBusy(true, "正在安装，请稍候…");
                await Task.Run(() => Install(installPath));
                SetBusy(false, "安装完成");
                ShowCompletionDialog(installPath);
                Close();
            }
            catch (Exception exception)
            {
                SetBusy(false, "安装失败");
                MessageBox.Show(this, $"安装失败：{exception.Message}", "Hinge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _statusLabel.Text = status;
            _progressBar.Visible = busy;
            _pathBox.Enabled = !busy;
            _browseButton.Enabled = !busy;
            _installButton.Enabled = !busy;
        }

        private void ShowCompletionDialog(string installPath)
        {
            using var dialog = new Form
            {
                Text = "Hinge",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(420, 170),
                Font = new Font("Segoe UI", 10F)
            };

            var title = new Label
            {
                Text = "安装完成",
                AutoSize = true,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                Location = new Point(28, 24)
            };
            var location = new Label
            {
                Text = $"安装位置：{installPath}",
                AutoSize = true,
                Location = new Point(30, 66),
                ForeColor = Color.DimGray
            };
            var finishButton = new Button
            {
                Text = "完成",
                Width = 88,
                Height = 32,
                Location = new Point(310, 116),
                DialogResult = DialogResult.OK
            };
            var openButton = new Button
            {
                Text = "打开",
                Width = 88,
                Height = 32,
                Location = new Point(212, 116)
            };
            openButton.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(installPath, "Hinge.exe"),
                        UseShellExecute = true,
                        WorkingDirectory = installPath
                    });
                    dialog.Close();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(dialog, $"无法打开 Hinge：{exception.Message}", "Hinge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            dialog.AcceptButton = finishButton;
            dialog.CancelButton = finishButton;
            dialog.Controls.AddRange(new Control[] { title, location, openButton, finishButton });
            dialog.ShowDialog(this);
        }
    }

    private static string NormalizeInstallPath(string selectedPath)
    {
        var fullPath = Path.GetFullPath(selectedPath);
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(root, "Hinge");
        }

        return fullPath;
    }

    private static string? FindExistingInstallPath()
    {
        var candidates = new List<string>();

        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, registryView)
                    .OpenSubKey(UninstallRegistryPath);
                AddCandidate(candidates, key?.GetValue("InstallLocation") as string);
            }
            catch
            {
                // A registry view may not exist on every Windows installation.
            }
        }

        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)
                    .OpenSubKey(UninstallRegistryPath);
                AddCandidate(candidates, key?.GetValue("InstallLocation") as string);
            }
            catch
            {
                // Reading HKLM is best effort; the installer itself uses HKCU.
            }
        }

        AddCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Hinge"));
        AddCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Hinge"));
        AddCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Hinge"));

        return candidates.FirstOrDefault(path =>
            File.Exists(Path.Combine(path, "Hinge.exe")));
    }

    private static void AddCandidate(ICollection<string> candidates, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return;
        try
        {
            var fullPath = Path.GetFullPath(candidate.Trim());
            if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(fullPath);
            }
        }
        catch
        {
            // Ignore malformed or inaccessible paths from stale registry data.
        }
    }

    private static string? Install(string installPath)
    {
        StopRunningApp();
        Directory.CreateDirectory(installPath);

        var temporaryZip = Path.Combine(Path.GetTempPath(), $"Hinge-{Guid.NewGuid():N}.zip");
        try
        {
            ExtractEmbeddedPayload(temporaryZip);
            ZipFile.ExtractToDirectory(temporaryZip, installPath, overwriteFiles: true);

            var executablePath = Path.Combine(installPath, "Hinge.exe");
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("安装文件不完整，未找到 Hinge.exe。", executablePath);
            }

            CreateShortcuts(installPath, executablePath);
            RegisterUninstaller(installPath, executablePath);
            return ConfigureFirewall(installPath);
        }
        finally
        {
            try { File.Delete(temporaryZip); } catch { }
        }
    }

    private static void ExtractEmbeddedPayload(string outputPath)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位安装程序。");
        var marker = System.Text.Encoding.ASCII.GetBytes(PayloadMarker);
        using var source = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        source.Seek(-sizeof(long), SeekOrigin.End);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        source.ReadExactly(lengthBytes);
        var payloadLength = BitConverter.ToInt64(lengthBytes);
        var markerOffset = source.Length - sizeof(long) - marker.Length;
        var payloadOffset = markerOffset - payloadLength;
        if (payloadLength <= 0 || markerOffset < 0 || payloadOffset < 0)
        {
            throw new InvalidDataException("安装程序中没有找到有效的应用文件。");
        }

        source.Seek(markerOffset, SeekOrigin.Begin);
        var markerBytes = new byte[marker.Length];
        source.ReadExactly(markerBytes);
        if (!markerBytes.SequenceEqual(marker))
        {
            throw new InvalidDataException("安装程序文件已损坏。");
        }

        source.Seek(payloadOffset, SeekOrigin.Begin);
        using var destination = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 1024];
        var remaining = payloadLength;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException("安装程序中的应用文件不完整。");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static bool IsHingeRunning()
    {
        var processes = Process.GetProcessesByName("Hinge");
        try
        {
            return processes.Any(process =>
            {
                try { return !process.HasExited; }
                catch { return true; }
            });
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static void StopRunningApp()
    {
        foreach (var process in Process.GetProcessesByName("Hinge"))
        {
            try
            {
                if (process.HasExited) continue;

                // Prefer a normal close so the application can persist its
                // settings, then force-close tray/background instances that
                // do not expose a closable main window.
                process.CloseMainWindow();
                if (process.WaitForExit(3000)) continue;

                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    throw new InvalidOperationException("无法关闭正在运行的 Hinge 进程。");
                }
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void CreateShortcuts(string installPath, string executablePath)
    {
        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs");
        Directory.CreateDirectory(startMenu);
        CreateShortcut(Path.Combine(startMenu, "Hinge.lnk"), executablePath, installPath);
        CreateShortcut(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Hinge.lnk"),
            executablePath,
            installPath);
    }

    private static void CreateShortcut(string shortcutPath, string executablePath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executablePath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = $"{executablePath},0";
        shortcut.Description = "Hinge 跨设备办公";
        shortcut.Save();
        try { Marshal.FinalReleaseComObject(shortcut); } catch { }
        try { Marshal.FinalReleaseComObject(shell); } catch { }
    }

    private static void RegisterUninstaller(string installPath, string executablePath)
    {
        var uninstallerPath = Path.Combine(installPath, "Uninstall-Hinge.ps1");
        if (!File.Exists(uninstallerPath)) return;
        using var key = Registry.CurrentUser.CreateSubKey(
            UninstallRegistryPath);
        key?.SetValue("DisplayName", "Hinge");
        key?.SetValue("DisplayPublisher", "Hinge");
        key?.SetValue("InstallLocation", installPath);
        key?.SetValue("DisplayIcon", $"{executablePath},0");
        key?.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{uninstallerPath}\"");
    }

    private static string? ConfigureFirewall(string installPath)
    {
        var firewallScript = Path.Combine(installPath, "allow_hinge_firewall.ps1");
        if (!File.Exists(firewallScript))
        {
            return $"未找到防火墙配置脚本：{firewallScript}";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{firewallScript}\""
            }) ?? throw new InvalidOperationException("无法启动 Windows 防火墙配置程序。");
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) return null;

            var detail = string.IsNullOrWhiteSpace(error) ? $"退出代码 {process.ExitCode}" : error.Trim();
            var logPath = Path.Combine(installPath, "firewall-error.log");
            File.WriteAllText(logPath, detail);
            return $"{logPath}\n{detail}";
        }
        catch (Exception exception)
        {
            var logPath = Path.Combine(installPath, "firewall-error.log");
            try { File.WriteAllText(logPath, exception.ToString()); } catch { }
            return $"{logPath}\n{exception.Message}";
        }
    }
}
