using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class SetupProgram
{
    private const string ProductName = "StudioHELPER";
    private const string ProductVersion = "0.1.0";
    private const string PayloadResource = "StudioHELPER.Payload";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "/verify", StringComparison.OrdinalIgnoreCase)))
            return VerifyPayload() ? 0 : 10;
        if (args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase)))
            return BeginUninstall();
        int finalIndex = Array.FindIndex(args, a => string.Equals(a, "/uninstall-final", StringComparison.OrdinalIgnoreCase));
        if (finalIndex >= 0 && finalIndex + 1 < args.Length)
            return FinishUninstall(args[finalIndex + 1]);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }

    private static bool VerifyPayload()
    {
        try
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                return archive.Entries.Any(e => string.Equals(e.FullName, "StudioHELPER.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static int BeginUninstall()
    {
        if (MessageBox.Show("Remove StudioHELPER from this computer?", "StudioHELPER",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
        try
        {
            string installDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string temporary = Path.Combine(Path.GetTempPath(), "StudioHELPER-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, temporary, true);
            Process.Start(new ProcessStartInfo
            {
                FileName = temporary,
                Arguments = "/uninstall-final \"" + installDirectory + "\"",
                UseShellExecute = true
            });
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "StudioHELPER", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int FinishUninstall(string installDirectory)
    {
        try
        {
            Thread.Sleep(900);
            DeleteShortcut();
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudioHELPER", false);
            string full = Path.GetFullPath(installDirectory);
            string allowedParent = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
            if (!full.StartsWith(allowedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected installation path.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
            MessageBox.Show("StudioHELPER was removed.", "StudioHELPER", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "StudioHELPER", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string ShortcutPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "StudioHELPER.lnk");
        }
    }

    private static void DeleteShortcut()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }

    private sealed class SetupForm : Form
    {
        private readonly Button _installButton;
        private readonly Button _cancelButton;
        private readonly CheckBox _launchCheckBox;
        private readonly ProgressBar _progress;
        private readonly Label _status;

        public SetupForm()
        {
            Text = "StudioHELPER Setup";
            ClientSize = new Size(540, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);

            var title = new Label { Text = "StudioHELPER", Font = new Font("Segoe UI", 22, FontStyle.Bold), AutoSize = true, Location = new Point(28, 24) };
            var version = new Label { Text = "Version " + SetupProgram.ProductVersion, Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray, AutoSize = true, Location = new Point(32, 70) };
            var text = new Label
            {
                Text = "Install StudioHELPER for the current Windows user.\r\nA Start menu shortcut and an uninstall entry will be created.",
                Font = new Font("Segoe UI", 10), AutoSize = true, Location = new Point(32, 108)
            };
            _launchCheckBox = new CheckBox { Text = "Launch StudioHELPER after installation", Checked = true, AutoSize = true, Location = new Point(32, 165), Font = new Font("Segoe UI", 9) };
            _progress = new ProgressBar { Location = new Point(32, 199), Size = new Size(476, 18), Style = ProgressBarStyle.Continuous };
            _status = new Label { Text = "Ready", AutoSize = true, Location = new Point(32, 224), ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9) };
            _installButton = new Button { Text = "Install", Size = new Size(110, 36), Location = new Point(278, 250), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _cancelButton = new Button { Text = "Cancel", Size = new Size(110, 36), Location = new Point(398, 250), Font = new Font("Segoe UI", 9) };
            _installButton.Click += InstallButtonClick;
            _cancelButton.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { title, version, text, _launchCheckBox, _progress, _status, _installButton, _cancelButton });
            AcceptButton = _installButton;
            CancelButton = _cancelButton;
        }

        private void InstallButtonClick(object sender, EventArgs e)
        {
            _installButton.Enabled = false;
            _cancelButton.Enabled = false;
            _progress.Value = 10;
            _status.Text = "Installing…";
            try
            {
                string installDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", SetupProgram.ProductName);
                Directory.CreateDirectory(installDirectory);
                ExtractPayload(installDirectory);
                _progress.Value = 70;
                string uninstaller = Path.Combine(installDirectory, "StudioHELPER.Uninstall.exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true);
                CreateShortcut(installDirectory);
                RegisterUninstaller(installDirectory, uninstaller);
                _progress.Value = 100;
                _status.Text = "Installation complete";

                bool runtimeAvailable = Directory.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App"));
                if (!runtimeAvailable)
                    MessageBox.Show("Microsoft .NET 8 Desktop Runtime is required before StudioHELPER can run.",
                        "StudioHELPER", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (_launchCheckBox.Checked && runtimeAvailable)
                    Process.Start(new ProcessStartInfo { FileName = Path.Combine(installDirectory, "StudioHELPER.exe"), UseShellExecute = true });
                MessageBox.Show("StudioHELPER was installed successfully.", "StudioHELPER", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Installation failed";
                MessageBox.Show(ex.Message, "StudioHELPER", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _installButton.Enabled = true;
                _cancelButton.Enabled = true;
            }
        }

        private static void ExtractPayload(string installDirectory)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                string root = Path.GetFullPath(installDirectory) + Path.DirectorySeparatorChar;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destination = Path.GetFullPath(Path.Combine(installDirectory, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid installer payload path.");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                }
            }
        }

        private static void CreateShortcut(string installDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = Path.Combine(installDirectory, "StudioHELPER.exe");
            shortcut.WorkingDirectory = installDirectory;
            shortcut.Description = "StudioHELPER";
            shortcut.Save();
        }

        private static void RegisterUninstaller(string installDirectory, string uninstaller)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudioHELPER"))
            {
                key.SetValue("DisplayName", SetupProgram.ProductName);
                key.SetValue("DisplayVersion", SetupProgram.ProductVersion);
                key.SetValue("Publisher", "fct.sk");
                key.SetValue("InstallLocation", installDirectory);
                key.SetValue("DisplayIcon", Path.Combine(installDirectory, "StudioHELPER.exe"));
                key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
    }
}
