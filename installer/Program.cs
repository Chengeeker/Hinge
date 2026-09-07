using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hinge.Setup;

internal static class Program
{
    private const string PayloadMarker = "HINGE_PAYLOAD_V1";

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
            Text = "Hinge 安装程序";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 270);
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
                Text = "选择安装位置。安装包不依赖 MSIX 证书，也不会强制安装到 C 盘。",
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
                Text = _defaultPath,
                Location = new Point(30, 142),
                Width = 480,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _browseButton = new Button
            {
                Text = "浏览…",
                Location = new Point(520, 140),
                Width = 88,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _browseButton.Click += BrowseButton_Click;
            _statusLabel = new Label
            {
                Text = "准备安装",
                AutoSize = true,
                Location = new Point(30, 190),
                ForeColor = Color.DimGray
            };
            _progressBar = new ProgressBar
            {
                Location = new Point(30, 216),
                Width = 478,
                Height = 18,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            _installButton = new Button
            {
                Text = "安装",
                Location = new Point(520, 210),
                Width = 88,
                Height = 30,
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
            var installPath = _pathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(installPath))
            {
                MessageBox.Show(this, "请选择安装目录。", "Hinge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                installPath = Path.GetFullPath(installPath);
                if (installPath == Path.GetPathRoot(installPath))
                {
                    throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个文件夹。 ");
                }

                SetBusy(true, "正在安装，请稍候…");
                await Task.Run(() => Install(installPath));
                SetBusy(false, "安装完成");
                MessageBox.Show(
                    this,
                    $"Hinge 安装完成。\n\n安装位置：{installPath}",
                    "Hinge",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
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
    }

    private static void Install(string installPath)
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
            ConfigureFirewall(installPath);
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

    private static void StopRunningApp()
    {
        foreach (var process in Process.GetProcessesByName("Hinge"))
        {
            try
            {
                if (process.CloseMainWindow() && process.WaitForExit(5000)) continue;
                throw new InvalidOperationException("Hinge 正在运行，请先关闭它再安装。");
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
            "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Hinge");
        key?.SetValue("DisplayName", "Hinge");
        key?.SetValue("DisplayPublisher", "Hinge");
        key?.SetValue("InstallLocation", installPath);
        key?.SetValue("DisplayIcon", $"{executablePath},0");
        key?.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{uninstallerPath}\"");
    }

    private static void ConfigureFirewall(string installPath)
    {
        var firewallScript = Path.Combine(installPath, "allow_hinge_firewall.ps1");
        if (!File.Exists(firewallScript)) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{firewallScript}\""
            });
            process?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // The user declined elevation; the app can still be used.
        }
    }
}
