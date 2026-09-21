using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace Hinge.Setup;

internal static class Program
{
    private const string PayloadMarker = "HINGE_PAYLOAD_V1";
    private const string AppUserModelId = "Hinge.Office";
    private const string SparsePackageName = "Hinge.Office.Identity";
    private const string ExplorerSnapshotPath = "Software\\Hinge\\ExplorerSend";
    private const string UninstallRegistryPath =
        "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Hinge";

    private readonly struct InstallProgress
    {
        public int Percent { get; }
        public string Status { get; }

        public InstallProgress(int percent, string status)
        {
            Percent = percent;
            Status = status;
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 &&
            string.Equals(args[0], "--unattended-install", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Install(NormalizeInstallPath(args[1]), progress: null);
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length == 3 &&
            string.Equals(args[0], "--trust-certificate", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = TrustCertificateForMachine(args[1], args[2]) ? 0 : 1;
            return;
        }

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
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
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

                SetBusy(true, "准备开始安装…");
                _progressBar.Value = 0;
                var progress = new Progress<InstallProgress>(p =>
                {
                    _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
                    _statusLabel.Text = p.Status;
                });
                await Task.Run(() => Install(installPath, progress));
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

        // 1. Check running Hinge process
        try
        {
            foreach (var process in Process.GetProcessesByName("Hinge"))
            {
                try
                {
                    var mainModulePath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(mainModulePath))
                    {
                        AddCandidate(candidates, Path.GetDirectoryName(mainModulePath));
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        catch { }

        // 2. Check machine-wide install-location.txt
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                var locFile = Path.Combine(programData, "Hinge", "install-location.txt");
                if (File.Exists(locFile))
                {
                    var text = File.ReadAllText(locFile, Encoding.UTF8).Trim();
                    AddCandidate(candidates, text);
                }
            }
        }
        catch { }

        // 3. Check HKCU and HKLM in both 64-bit and 32-bit views
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, registryView);

                    // 3a. Software\Hinge
                    using var hingeKey = baseKey.OpenSubKey("Software\\Hinge");
                    AddCandidate(candidates, hingeKey?.GetValue("InstallPath") as string);
                    AddCandidate(candidates, hingeKey?.GetValue("InstallLocation") as string);
                    var exePath = hingeKey?.GetValue("ExecutablePath") as string;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        AddCandidate(candidates, Path.GetDirectoryName(exePath));
                    }

                    // 3b. Uninstall key
                    using var uninstKey = baseKey.OpenSubKey(UninstallRegistryPath);
                    AddCandidate(candidates, uninstKey?.GetValue("InstallLocation") as string);

                    // 3c. ExplorerSend key
                    using var expKey = baseKey.OpenSubKey(ExplorerSnapshotPath);
                    var expExe = expKey?.GetValue("ExecutablePath") as string;
                    if (!string.IsNullOrWhiteSpace(expExe))
                    {
                        AddCandidate(candidates, Path.GetDirectoryName(expExe));
                    }
                }
                catch { }
            }
        }

        // 4. Check all loaded user profiles in HKEY_USERS (when elevated via UAC)
        try
        {
            foreach (var subKeyName in Registry.Users.GetSubKeyNames())
            {
                if (subKeyName.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var userKey = Registry.Users.OpenSubKey(subKeyName);
                    if (userKey == null) continue;

                    using var hingeKey = userKey.OpenSubKey("Software\\Hinge");
                    AddCandidate(candidates, hingeKey?.GetValue("InstallPath") as string);
                    AddCandidate(candidates, hingeKey?.GetValue("InstallLocation") as string);
                    var exe = hingeKey?.GetValue("ExecutablePath") as string;
                    if (!string.IsNullOrWhiteSpace(exe)) AddCandidate(candidates, Path.GetDirectoryName(exe));

                    using var uninstKey = userKey.OpenSubKey(UninstallRegistryPath);
                    AddCandidate(candidates, uninstKey?.GetValue("InstallLocation") as string);

                    using var expKey = userKey.OpenSubKey(ExplorerSnapshotPath);
                    var expExe = expKey?.GetValue("ExecutablePath") as string;
                    if (!string.IsNullOrWhiteSpace(expExe)) AddCandidate(candidates, Path.GetDirectoryName(expExe));
                }
                catch { }
            }
        }
        catch { }

        // 5. Check desktop and start menu shortcuts
        var shortcutDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs")
        };
        foreach (var dir in shortcutDirs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            var lnkPath = Path.Combine(dir, "Hinge.lnk");
            var target = ResolveShortcutTarget(lnkPath);
            if (!string.IsNullOrWhiteSpace(target))
            {
                if (File.Exists(target))
                {
                    AddCandidate(candidates, Path.GetDirectoryName(target));
                }
                else if (Directory.Exists(target))
                {
                    AddCandidate(candidates, target);
                }
            }
        }

        // 6. Conventional default locations
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

    private static string? ResolveShortcutTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            string target = (string)shortcut.TargetPath;
            try { Marshal.FinalReleaseComObject(shortcut); } catch { }
            try { Marshal.FinalReleaseComObject(shell); } catch { }
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
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

    private static string? Install(string installPath, IProgress<InstallProgress>? progress = null)
    {
        progress?.Report(new InstallProgress(5, "正在检查并停止已运行的实例…"));
        StopRunningApp();

        Directory.CreateDirectory(installPath);

        progress?.Report(new InstallProgress(10, "正在解压应用核心组件…"));
        ExtractPayloadDirect(installPath, progress);

        var executablePath = Path.Combine(installPath, "Hinge.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("安装文件不完整，未找到 Hinge.exe。", executablePath);
        }

        progress?.Report(new InstallProgress(75, "正在创建桌面与开始菜单快捷方式…"));
        CreateShortcuts(installPath, executablePath, AppUserModelId);

        progress?.Report(new InstallProgress(82, "正在注册卸载程序信息…"));
        RegisterUninstaller(installPath, executablePath);

        progress?.Report(new InstallProgress(88, "正在配置 Windows 11 右键菜单集成…"));
        RegisterModernExplorerMenu(installPath);

        progress?.Report(new InstallProgress(95, "正在配置局域网防火墙规则…"));
        var firewallResult = ConfigureFirewall(installPath);

        progress?.Report(new InstallProgress(100, "安装完成"));
        return firewallResult;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private static void NotifyShellAssociationChanged()
    {
        try
        {
            const int SHCNE_ASSOCCHANGED = 0x08000000;
            const uint SHCNF_IDLIST = 0x0000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Best effort notification to refresh shell icon/menu cache
        }
    }

    private static void RegisterModernExplorerMenu(string installPath)
    {
        var packagePath = Path.Combine(installPath, "Hinge.Identity.msix");
        var certificatePath = Path.Combine(installPath, "Hinge.Identity.cer");
        var logPath = Path.Combine(installPath, "shell-integration-error.log");
        try
        {
            if (!File.Exists(packagePath) || !File.Exists(certificatePath))
            {
                throw new FileNotFoundException("安装包缺少 Windows 11 右键菜单组件。");
            }

            var certificate = new X509Certificate2(certificatePath);
            EnsureMachineCertificateTrusted(certificatePath, certificate.Thumbprint);

            RegisterSparseIdentity(packagePath, installPath);

            NotifyShellAssociationChanged();

            using var snapshot = Registry.CurrentUser.CreateSubKey(ExplorerSnapshotPath, writable: true);
            snapshot?.SetValue("ModernMenuRegistered", 1, RegistryValueKind.DWord);
            snapshot?.SetValue("CertificateThumbprint", certificate.Thumbprint, RegistryValueKind.String);
            try { File.Delete(logPath); } catch { }
        }
        catch (Exception exception)
        {
            try
            {
                using var snapshot = Registry.CurrentUser.CreateSubKey(ExplorerSnapshotPath, writable: true);
                snapshot?.SetValue("ModernMenuRegistered", 0, RegistryValueKind.DWord);
                File.WriteAllText(logPath, exception.ToString());
            }
            catch
            {
                // Installation itself remains usable through the legacy menu.
            }
        }
    }

    private static void RegisterSparseIdentity(string packagePath, string installPath)
    {
        var package = QuotePowerShellLiteral(Path.GetFullPath(packagePath));
        var externalLocation = QuotePowerShellLiteral(Path.GetFullPath(installPath));
        var command =
            "$ErrorActionPreference='Stop'; " +
            $"$existing = @(Get-AppxPackage -Name '{SparsePackageName}' -ErrorAction SilentlyContinue); " +
            "if ($existing.Count -gt 0) { " +
            "  $existing | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue }; " +
            "}; " +
            $"Add-AppxPackage -Path {package} -ExternalLocation {externalLocation} " +
            "-ForceApplicationShutdown -ForceUpdateFromAnyVersion; " +
            $"$registered = @(Get-AppxPackage -Name '{SparsePackageName}' -ErrorAction SilentlyContinue); " +
            "if ($registered.Count -eq 0 -or $registered[0].Status -ne 'Ok') { " +
            "  throw 'Hinge 稀疏身份包注册后状态不是 Ok。'; " +
            "}";
        RunPowerShell(command);
    }

    private static void EnsureMachineCertificateTrusted(string certificatePath, string thumbprint)
    {
        if (IsMachineCertificateTrusted(thumbprint)) return;

        // Current installer is already elevated (requireAdministrator). Trust directly in-process:
        if (TrustCertificateForMachine(certificatePath, thumbprint)) return;

        // Fallback to runas if for any reason in-process trust failed
        var installerPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位安装程序以注册 Windows 集成证书。");
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--trust-certificate");
        startInfo.ArgumentList.Add(certificatePath);
        startInfo.ArgumentList.Add(thumbprint);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows 集成证书注册程序。");
        process.WaitForExit();
        if (process.ExitCode != 0 || !IsMachineCertificateTrusted(thumbprint))
        {
            throw new InvalidOperationException("Windows 11 右键菜单证书未获得本机信任。");
        }
    }


    private static bool IsMachineCertificateTrusted(string thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates
                .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                .Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrustCertificateForMachine(string certificatePath, string expectedThumbprint)
    {
        try
        {
            var certificate = new X509Certificate2(certificatePath);
            if (!string.Equals(
                    certificate.Thumbprint,
                    expectedThumbprint,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    certificate.Subject,
                    "CN=Hinge Package Identity",
                    StringComparison.Ordinal) ||
                certificate.HasPrivateKey)
            {
                return false;
            }
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            var exists = store.Certificates
                .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
                .Count > 0;
            if (!exists) store.Add(certificate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string QuotePowerShellLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void RunPowerShell(string command)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("无法启动 Windows 集成注册程序。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(errorTask.Result)
                ? outputTask.Result
                : errorTask.Result;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Windows 集成注册失败，退出代码 {process.ExitCode}。"
                    : detail.Trim());
        }
    }

    private sealed class BoundedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _startOffset;
        private readonly long _length;
        private long _position;

        public BoundedStream(Stream inner, long startOffset, long length)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _startOffset = startOffset;
            _length = length;
            _position = 0;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;
            int toRead = (int)Math.Min(count, _length - _position);
            _inner.Seek(_startOffset + _position, SeekOrigin.Begin);
            int read = _inner.Read(buffer, offset, toRead);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0 || target > _length)
            {
                throw new IOException($"Seek out of bounds: {target} (length: {_length})");
            }
            _position = target;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void ExtractPayloadDirect(string installPath, IProgress<InstallProgress>? progress)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位安装程序。");
        var marker = Encoding.ASCII.GetBytes(PayloadMarker);

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

        using var boundedStream = new BoundedStream(source, payloadOffset, payloadLength);
        using var archive = new ZipArchive(boundedStream, ZipArchiveMode.Read);

        int totalEntries = archive.Entries.Count;
        int processedEntries = 0;

        foreach (var entry in archive.Entries)
        {
            processedEntries++;
            if (string.IsNullOrEmpty(entry.Name)) continue; // Directory entry

            var destinationPath = Path.Combine(installPath, entry.FullName);
            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            // Smart incremental extraction: skip rewriting if file already exists with same size & timestamp
            if (File.Exists(destinationPath))
            {
                try
                {
                    var fileInfo = new FileInfo(destinationPath);
                    if (fileInfo.Length == entry.Length &&
                        Math.Abs((fileInfo.LastWriteTimeUtc - entry.LastWriteTime.UtcDateTime).TotalSeconds) < 2)
                    {
                        if (processedEntries % 25 == 0 || processedEntries == totalEntries)
                        {
                            int percent = 10 + (int)(processedEntries * 60.0 / totalEntries);
                            progress?.Report(new InstallProgress(percent, $"正在检查组件 ({processedEntries}/{totalEntries})…"));
                        }
                        continue;
                    }
                }
                catch
                {
                    // If checking fails, proceed to extract
                }
            }

            entry.ExtractToFile(destinationPath, overwrite: true);

            if (processedEntries % 20 == 0 || processedEntries == totalEntries)
            {
                int percent = 10 + (int)(processedEntries * 60.0 / totalEntries);
                progress?.Report(new InstallProgress(percent, $"正在释放组件 ({processedEntries}/{totalEntries})…"));
            }
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

                // Hinge may have launched a user application such as QQ or
                // Weixin earlier. Those applications must outlive a Hinge
                // update, so force-close only Hinge itself rather than its
                // entire descendant process tree.
                process.Kill(entireProcessTree: false);
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

    private static void CreateShortcuts(
        string installPath,
        string executablePath,
        string appUserModelId)
    {
        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs");
        Directory.CreateDirectory(startMenu);
        CreateShortcut(
            Path.Combine(startMenu, "Hinge.lnk"),
            executablePath,
            installPath,
            appUserModelId);
        CreateShortcut(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Hinge.lnk"),
            executablePath,
            installPath,
            appUserModelId);
    }

    private static void CreateShortcut(
        string shortcutPath,
        string executablePath,
        string workingDirectory,
        string appUserModelId)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executablePath;
        shortcut.WorkingDirectory = workingDirectory;
        var iconPath = Path.Combine(workingDirectory, "Assets", "app_icon.ico");
        shortcut.IconLocation = File.Exists(iconPath)
            ? $"{iconPath},0"
            : $"{executablePath},0";
        shortcut.Description = "Hinge 跨设备办公";
        shortcut.Save();
        SetShortcutProperty(shortcutPath, appUserModelId);
        try { Marshal.FinalReleaseComObject(shortcut); } catch { }
        try { Marshal.FinalReleaseComObject(shell); } catch { }
    }

    private static void SetShortcutProperty(string shortcutPath, string appUserModelId)
    {
        IPropertyStore? propertyStore = null;
        try
        {
            var propertyKey = new PropertyKey(
                new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                5);
            var propertyVariant = PropVariant.FromString(appUserModelId);
            var iid = typeof(IPropertyStore).GUID;
            var result = SHGetPropertyStoreFromParsingName(
                shortcutPath,
                IntPtr.Zero,
                GetPropertyStoreFlags.ReadWrite,
                ref iid,
                out propertyStore);
            if (result != 0 || propertyStore == null) return;
            propertyStore.SetValue(ref propertyKey, ref propertyVariant);
            propertyStore.Commit();
            propertyVariant.Clear();
        }
        catch
        {
            // The shortcut is still usable without the identity property; the
            // next installer run will retry it.
        }
        finally
        {
            if (propertyStore != null)
            {
                try { Marshal.FinalReleaseComObject(propertyStore); } catch { }
            }
        }
    }

    [Flags]
    private enum GetPropertyStoreFlags : uint
    {
        ReadWrite = 0x00000002
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private ushort _valueType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _pointer;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                _valueType = 31, // VT_LPWSTR
                _pointer = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Clear()
        {
            if (_pointer == IntPtr.Zero) return;
            Marshal.FreeCoTaskMem(_pointer);
            _pointer = IntPtr.Zero;
        }
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint propertyCount);
        int GetAt(uint propertyIndex, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path,
        IntPtr bindContext,
        GetPropertyStoreFlags flags,
        ref Guid interfaceId,
        out IPropertyStore propertyStore);

    private static void RegisterUninstaller(string installPath, string executablePath)
    {
        PersistInstallLocation(installPath, executablePath);
    }

    private static void PersistInstallLocation(string installPath, string executablePath)
    {
        // 1. Machine-wide install location in %ProgramData%\Hinge\install-location.txt
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                var dir = Path.Combine(programData, "Hinge");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "install-location.txt"), installPath, Encoding.UTF8);
            }
        }
        catch
        {
            // Best effort
        }

        // 2. Persist in HKCU and HKLM Software\Hinge
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.CreateSubKey("Software\\Hinge");
                    key?.SetValue("InstallPath", installPath);
                    key?.SetValue("InstallLocation", installPath);
                    key?.SetValue("ExecutablePath", executablePath);
                }
                catch
                {
                    // Best effort
                }
            }
        }

        // 3. Persist uninstall entry in HKCU and HKLM
        var uninstallerPath = Path.Combine(installPath, "Uninstall-Hinge.ps1");
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.CreateSubKey(UninstallRegistryPath);
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Hinge");
                        key.SetValue("DisplayPublisher", "Hinge");
                        key.SetValue("InstallLocation", installPath);
                        key.SetValue("DisplayIcon", $"{executablePath},0");
                        if (File.Exists(uninstallerPath))
                        {
                            key.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{uninstallerPath}\"");
                        }
                    }
                }
                catch
                {
                    // Best effort
                }
            }
        }
    }

    private static string? ConfigureFirewall(string installPath)
    {
        var executablePath = Path.Combine(installPath, "Hinge.exe");
        if (!File.Exists(executablePath))
        {
            return $"未找到主执行文件：{executablePath}";
        }

        try
        {
            // Delete legacy and current rules directly via netsh without launching powershell.exe
            var rulesToDelete = new[]
            {
                "Office Suite LAN - Discovery UDP",
                "Office Suite LAN - Session TCP",
                "Hinge LAN - Discovery UDP",
                "Hinge LAN - Session TCP",
                "Hinge LAN - Session TCP Dynamic"
            };

            foreach (var name in rulesToDelete)
            {
                RunNetsh($"advfirewall firewall delete rule \"name={name}\"");
            }

            var rulesToAdd = new[]
            {
                $"advfirewall firewall add rule name=\"Hinge LAN - Discovery UDP\" dir=in action=allow program=\"{executablePath}\" protocol=UDP localport=52830 remoteip=localsubnet profile=any enable=yes",
                $"advfirewall firewall add rule name=\"Hinge LAN - Session TCP\" dir=in action=allow program=\"{executablePath}\" protocol=TCP localport=52831 remoteip=localsubnet profile=any enable=yes",
                $"advfirewall firewall add rule name=\"Hinge LAN - Session TCP Dynamic\" dir=in action=allow program=\"{executablePath}\" protocol=TCP localport=any remoteip=localsubnet profile=any enable=yes"
            };

            foreach (var rule in rulesToAdd)
            {
                RunNetsh(rule);
            }

            return null;
        }
        catch (Exception exception)
        {
            var logPath = Path.Combine(installPath, "firewall-error.log");
            try { File.WriteAllText(logPath, exception.ToString()); } catch { }
            return $"{logPath}\n{exception.Message}";
        }
    }

    private static void RunNetsh(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(3000);
        }
        catch
        {
            // Best effort rule configuration
        }
    }
}
