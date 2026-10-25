using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace StudioUSB;

public partial class MainWindow : Window
{
    private const int WebLongSide = 2400;
    private const int DefaultQuality = 80;
    private const int DefaultFormatLimitGiB = 32;
    private static readonly int[] AllowedFormatLimits = [4, 8, 16, 32, 64, 128, 256];
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudioUSB");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private CancellationTokenSource? _cancellation;

    public MainWindow()
    {
        InitializeComponent();
        FormatLimitComboBox.ItemsSource = AllowedFormatLimits.Select(value => new FormatLimitOption(value)).ToList();
        AppSettings settings = LoadSettings();
        ApplySettings(settings);
        RefreshDrives(settings.TargetDriveRoot);
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e) => SaveSettingsFromUi();

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Vyberte adresár s fotografiami", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
            SourceTextBox.Text = dialog.FolderName;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        RefreshDrives((DriveComboBox.SelectedItem as DriveOption)?.Root);

    private void RefreshDrives(string? preferredRoot = null)
    {
        string? selectedRoot = preferredRoot ?? (DriveComboBox.SelectedItem as DriveOption)?.Root;
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Removable)
            .Select(d => new DriveOption(d)).OrderBy(d => d.Root).ToList();
        DriveComboBox.ItemsSource = drives;
        DriveComboBox.SelectedItem = drives.FirstOrDefault(d => d.Root == selectedRoot) ?? drives.FirstOrDefault();
        StatusTextBlock.Text = drives.Count == 0 ? "Nenašlo sa žiadne pripravené USB médium." : "Pripravené";
    }

    private void FormatCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = FormatCheckBox.IsChecked == true;
        if (VolumeLabelTextBox is not null) VolumeLabelTextBox.IsEnabled = enabled;
        if (FormatLimitComboBox is not null) FormatLimitComboBox.IsEnabled = enabled;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        try
        {
            WorkRequest? request = ValidateAndCreateRequest();
            if (request is null || !ConfirmRequest(request)) return;
            SetBusy(true);
            LogTextBox.Clear();
            _cancellation = new CancellationTokenSource();
            var progress = new Progress<WorkProgress>(UpdateProgress);
            await Task.Run(() => RunWorkflow(request, progress, _cancellation.Token));
            ProgressBar.Value = 100;
            StatusTextBlock.Text = "Hotovo – všetky súbory boli overené.";
            AppendLog("HOTOVO: Web, tlačové originály a TXT boli vytvorené a overené.");
            MessageBox.Show(this, "Spracovanie bolo úspešne dokončené a overené.", "StudioUSB",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Spracovanie bolo zrušené.";
            AppendLog("ZRUŠENÉ: Čiastočne vytvorené súbory môžu zostať na USB.");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Spracovanie zlyhalo.";
            AppendLog($"CHYBA: {ex.Message}");
            MessageBox.Show(this, ex.Message, "StudioUSB – chyba", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetBusy(false);
            RefreshDrives();
        }
    }

    private WorkRequest? ValidateAndCreateRequest()
    {
        string source = SourceTextBox.Text.Trim();
        if (!Directory.Exists(source)) return WarnAndReturn("Vyberte existujúci zdrojový adresár.");
        if (DriveComboBox.SelectedItem is not DriveOption drive) return WarnAndReturn("Vyberte cieľové USB médium.");
        if (FormatLimitComboBox.SelectedItem is not FormatLimitOption limitOption)
            return WarnAndReturn("Vyberte maximálnu veľkosť média.");

        bool formatFirst = FormatCheckBox.IsChecked == true;
        DriveInfo currentDrive = ValidateRemovableDrive(drive.Root, formatFirst ? limitOption.GiB : null);
        string sourceRoot = Path.GetPathRoot(Path.GetFullPath(source)) ?? string.Empty;
        if (string.Equals(sourceRoot, currentDrive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
            return WarnAndReturn("Zdrojový adresár nesmie byť na cieľovom USB.");

        string label = VolumeLabelTextBox.Text.Trim();
        if (formatFirst && !Regex.IsMatch(label, "^[A-Za-z0-9 _-]{1,11}$"))
            return WarnAndReturn("Názov USB musí mať 1 až 11 znakov: písmená bez diakritiky, čísla, medzeru, _ alebo -.");

        string webFolder = WebFolderTextBox.Text.Trim();
        string printFolder = PrintFolderTextBox.Text.Trim();
        string suffix = SuffixTextBox.Text.Trim();
        if (!IsValidSimpleName(webFolder)) return WarnAndReturn("Názov webového adresára nie je platný.");
        if (!IsValidSimpleName(printFolder)) return WarnAndReturn("Názov tlačového adresára nie je platný.");
        if (string.Equals(webFolder, printFolder, StringComparison.OrdinalIgnoreCase))
            return WarnAndReturn("Webový a tlačový adresár musia mať odlišné názvy.");
        if (suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return WarnAndReturn("Prípona názvu obsahuje nepovolený znak.");

        var jpgFiles = EnumerateJpegs(source).ToList();
        if (jpgFiles.Count == 0) return WarnAndReturn("Zdrojový adresár neobsahuje žiadne JPG alebo JPEG súbory.");
        long sourceBytes = jpgFiles.Sum(f => new FileInfo(f).Length);
        long estimatedNeed = sourceBytes * 2 + 64L * 1024 * 1024;
        if (!formatFirst && currentDrive.AvailableFreeSpace < estimatedNeed)
            return WarnAndReturn("Na USB pravdepodobne nie je dostatok voľného miesta.");

        return new WorkRequest(source, currentDrive.RootDirectory.FullName, formatFirst, label, limitOption.GiB,
            (int)Math.Round(QualitySlider.Value), suffix, webFolder, printFolder, jpgFiles);
    }

    private bool ConfirmRequest(WorkRequest request)
    {
        if (request.FormatFirst)
        {
            DriveInfo drive = ValidateRemovableDrive(request.TargetRoot, request.FormatLimitGiB);
            string fileSystem = GetFormatFileSystem(drive.TotalSize);
            string message = $"POZOR: celý obsah {drive.Name} bude vymazaný.\n\n" +
                $"Typ: vymeniteľné médium\nKapacita: {FormatBytes(drive.TotalSize)}\n" +
                $"Bezpečnostný limit: {request.FormatLimitGiB} GiB\nSúborový systém: {fileSystem}\n" +
                $"Nový názov: {request.VolumeLabel}\n\nPokračovať?";
            return MessageBox.Show(this, message, "Potvrdenie formátovania", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        string web = Path.Combine(request.TargetRoot, request.WebFolderName);
        string print = Path.Combine(request.TargetRoot, request.PrintFolderName);
        string txt = Path.Combine(request.TargetRoot, GetSourceName(request.Source) + ".txt");
        if (Directory.Exists(web) || Directory.Exists(print) || File.Exists(txt))
        {
            return MessageBox.Show(this,
                $"Existujúce {request.WebFolderName}, {request.PrintFolderName} alebo príslušný TXT súbor budú nahradené. " +
                "Ostatné súbory na USB zostanú nedotknuté.\n\nPokračovať?",
                "Nahradenie výstupu", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
        return true;
    }

    private void RunWorkflow(WorkRequest request, IProgress<WorkProgress> progress, CancellationToken token)
    {
        if (request.FormatFirst)
        {
            progress.Report(new WorkProgress(1, "Formátujem USB…", "Spúšťam bezpečné formátovanie."));
            FormatDrive(request.TargetRoot, request.VolumeLabel, request.FormatLimitGiB);
        }

        token.ThrowIfCancellationRequested();
        DriveInfo drive = ValidateRemovableDrive(request.TargetRoot, null);
        long estimatedNeed = request.JpegFiles.Sum(f => new FileInfo(f).Length) * 2 + 64L * 1024 * 1024;
        if (drive.AvailableFreeSpace < estimatedNeed) throw new IOException("Na USB nie je dostatok voľného miesta.");

        string webRoot = Path.Combine(drive.RootDirectory.FullName, request.WebFolderName);
        string printRoot = Path.Combine(drive.RootDirectory.FullName, request.PrintFolderName);
        string txtPath = Path.Combine(drive.RootDirectory.FullName, GetSourceName(request.Source) + ".txt");
        ReplaceDirectory(webRoot);
        ReplaceDirectory(printRoot);
        if (File.Exists(txtPath)) File.Delete(txtPath);

        int totalSteps = request.JpegFiles.Count * 3;
        int completed = 0;
        foreach (string sourceFile in request.JpegFiles)
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(request.Source, sourceFile);
            string printFile = Path.Combine(printRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(printFile)!);
            File.Copy(sourceFile, printFile, true);
            progress.Report(ProgressFor(++completed, totalSteps, "Kopírujem originály…", relative));
        }

        foreach (string sourceFile in request.JpegFiles)
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(request.Source, sourceFile);
            string directory = Path.GetDirectoryName(relative) ?? string.Empty;
            string outputName = Path.GetFileNameWithoutExtension(relative) + request.Suffix + Path.GetExtension(relative);
            string webFile = Path.Combine(webRoot, directory, outputName);
            Directory.CreateDirectory(Path.GetDirectoryName(webFile)!);
            ResizeJpeg(sourceFile, webFile, request.JpegQuality);
            progress.Report(ProgressFor(++completed, totalSteps, "Vytváram webové fotografie…", outputName));
        }

        File.WriteAllText(txtPath, string.Empty, new UTF8Encoding(false));
        foreach (string sourceFile in request.JpegFiles)
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(request.Source, sourceFile);
            string printFile = Path.Combine(printRoot, relative);
            string directory = Path.GetDirectoryName(relative) ?? string.Empty;
            string outputName = Path.GetFileNameWithoutExtension(relative) + request.Suffix + Path.GetExtension(relative);
            string webFile = Path.Combine(webRoot, directory, outputName);
            if (!File.Exists(printFile) || !File.Exists(webFile)) throw new IOException($"Chýba výstupný súbor pre {relative}.");
            if (!HashesMatch(sourceFile, printFile)) throw new IOException($"Originál v {request.PrintFolderName} sa nezhoduje: {relative}.");
            VerifyWebImage(sourceFile, webFile, request.Suffix);
            progress.Report(ProgressFor(++completed, totalSteps, "Overujem výsledok…", relative));
        }
        if (!File.Exists(txtPath) || new FileInfo(txtPath).Length != 0)
            throw new IOException("Textový súbor nebol vytvorený správne.");
    }

    private static void FormatDrive(string targetRoot, string label, int formatLimitGiB)
    {
        DriveInfo drive = ValidateRemovableDrive(targetRoot, formatLimitGiB);
        string driveLetter = drive.Name.TrimEnd('\\');
        string fileSystem = GetFormatFileSystem(drive.TotalSize);
        long limitBytes = GiBToBytes(formatLimitGiB);
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $d = [System.IO.DriveInfo]::new('{{driveLetter}}')
            if (-not $d.IsReady) { throw 'USB nie je pripravené.' }
            if ($d.DriveType -ne [System.IO.DriveType]::Removable) { throw 'Disk nie je vymeniteľné médium.' }
            if ([uint64]$d.TotalSize -gt [uint64]{{limitBytes}}) { throw 'Disk prekračuje nastavený bezpečnostný limit.' }
            & "$env:SystemRoot\System32\format.com" '{{driveLetter}}' '/FS:{{fileSystem}}' '/V:{{label}}' '/Q' '/Y'
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            exit 0
            """;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Nepodarilo sa spustiť formátovanie.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Formátovanie zlyhalo (kód {process.ExitCode}).");

        DriveInfo result = WaitForDrive(targetRoot, formatLimitGiB);
        if (!string.Equals(result.DriveFormat, fileSystem, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"USB po formátovaní nemá súborový systém {fileSystem}.");
    }

    private static DriveInfo WaitForDrive(string root, int formatLimitGiB)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try { return ValidateRemovableDrive(root, formatLimitGiB); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                Thread.Sleep(250);
            }
        }
        throw new IOException("USB po formátovaní nie je pripravené.", lastError);
    }

    private static DriveInfo ValidateRemovableDrive(string root, int? formatLimitGiB)
    {
        string fullRoot = Path.GetPathRoot(Path.GetFullPath(root)) ?? throw new IOException("Neplatná cieľová jednotka.");
        var drive = new DriveInfo(fullRoot);
        if (!drive.IsReady) throw new IOException("Cieľové USB nie je pripravené.");
        if (drive.DriveType != DriveType.Removable) throw new IOException("Cieľ nie je vymeniteľné médium.");
        if (string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Systémový disk nemožno použiť.");
        if (formatLimitGiB.HasValue && drive.TotalSize > GiBToBytes(formatLimitGiB.Value))
            throw new IOException($"Formátovanie je povolené iba do {formatLimitGiB.Value} GiB. " +
                                  $"Vybrané médium má {FormatBytes(drive.TotalSize)}.");
        return drive;
    }

    private static string GetFormatFileSystem(long totalBytes) => totalBytes <= GiBToBytes(32) ? "FAT32" : "exFAT";
    private static long GiBToBytes(int value) => value * 1024L * 1024L * 1024L;

    private static void ResizeJpeg(string sourcePath, string destinationPath, int jpegQuality)
    {
        using var image = Image.FromFile(sourcePath);
        ApplyExifOrientation(image);
        double scale = Math.Min(1.0, (double)WebLongSide / Math.Max(image.Width, image.Height));
        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.White);
            graphics.DrawImage(image, new Rectangle(0, 0, width, height));
        }
        ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().Single(c => c.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)jpegQuality);
        bitmap.Save(destinationPath, jpeg, parameters);
    }

    private static void ApplyExifOrientation(Image image)
    {
        const int orientationId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationId)) return;
        try
        {
            PropertyItem? property = image.GetPropertyItem(orientationId);
            if (property?.Value is not { Length: > 0 }) return;
            int orientation = property.Value[0];
            RotateFlipType transform = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            if (transform != RotateFlipType.RotateNoneFlipNone) image.RotateFlip(transform);
        }
        catch (ArgumentException) { }
    }

    private static void VerifyWebImage(string sourcePath, string webPath, string suffix)
    {
        using var source = Image.FromFile(sourcePath);
        ApplyExifOrientation(source);
        double scale = Math.Min(1.0, (double)WebLongSide / Math.Max(source.Width, source.Height));
        int expectedWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        int expectedHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var web = Image.FromFile(webPath);
        if (web.Width != expectedWidth || web.Height != expectedHeight)
            throw new IOException($"Nesprávny rozmer webovej fotografie: {Path.GetFileName(webPath)}.");
        if (!Path.GetFileNameWithoutExtension(webPath).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Nesprávna prípona názvu: {Path.GetFileName(webPath)}.");
    }

    private static bool HashesMatch(string first, string second)
    {
        using var sha = SHA256.Create();
        using var a = File.OpenRead(first);
        byte[] firstHash = sha.ComputeHash(a);
        using var b = File.OpenRead(second);
        byte[] secondHash = sha.ComputeHash(b);
        return CryptographicOperations.FixedTimeEquals(firstHash, secondHash);
    }

    private static IEnumerable<string> EnumerateJpegs(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Path.GetExtension(path), ".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static void ReplaceDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, true);
        }
        Directory.CreateDirectory(path);
    }

    private static bool IsValidSimpleName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..") return false;
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.EndsWith('.')) return false;
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return !Regex.IsMatch(value, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", RegexOptions.IgnoreCase);
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    private void ApplySettings(AppSettings settings)
    {
        QualitySlider.Value = Math.Clamp(settings.JpegQuality, 1, 100);
        SuffixTextBox.Text = settings.Suffix ?? "-websize";
        WebFolderTextBox.Text = string.IsNullOrWhiteSpace(settings.WebFolderName) ? "WEBSIZE" : settings.WebFolderName;
        PrintFolderTextBox.Text = string.IsNullOrWhiteSpace(settings.PrintFolderName) ? "PRINTSIZE" : settings.PrintFolderName;
        VolumeLabelTextBox.Text = string.IsNullOrWhiteSpace(settings.VolumeLabel) ? "FOTO" : settings.VolumeLabel;
        int limit = AllowedFormatLimits.Contains(settings.FormatLimitGiB) ? settings.FormatLimitGiB : DefaultFormatLimitGiB;
        FormatLimitComboBox.SelectedValue = limit;
        FormatCheckBox.IsChecked = settings.FormatFirst;
    }

    private void SaveSettingsFromUi()
    {
        try
        {
            int limit = (FormatLimitComboBox.SelectedItem as FormatLimitOption)?.GiB ?? DefaultFormatLimitGiB;
            var settings = new AppSettings
            {
                JpegQuality = (int)Math.Round(QualitySlider.Value),
                Suffix = SuffixTextBox.Text,
                WebFolderName = WebFolderTextBox.Text,
                PrintFolderName = PrintFolderTextBox.Text,
                FormatFirst = FormatCheckBox.IsChecked == true,
                VolumeLabel = VolumeLabelTextBox.Text,
                FormatLimitGiB = limit,
                TargetDriveRoot = (DriveComboBox.SelectedItem as DriveOption)?.Root
            };
            Directory.CreateDirectory(SettingsDirectory);
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch { }
    }

    private static string GetSourceName(string source) => new DirectoryInfo(Path.TrimEndingDirectorySeparator(source)).Name;
    private static WorkProgress ProgressFor(int done, int total, string status, string detail) =>
        new((int)Math.Round(done * 100.0 / total), status, detail);

    private void UpdateProgress(WorkProgress progress)
    {
        ProgressBar.Value = progress.Percent;
        StatusTextBlock.Text = progress.Status;
        AppendLog(progress.Detail);
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private WorkRequest? WarnAndReturn(string message)
    {
        MessageBox.Show(this, message, "StudioUSB", MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void SetBusy(bool busy)
    {
        BrowseButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        DriveComboBox.IsEnabled = !busy;
        QualitySlider.IsEnabled = !busy;
        SuffixTextBox.IsEnabled = !busy;
        WebFolderTextBox.IsEnabled = !busy;
        PrintFolderTextBox.IsEnabled = !busy;
        FormatCheckBox.IsEnabled = !busy;
        VolumeLabelTextBox.IsEnabled = !busy && FormatCheckBox.IsChecked == true;
        FormatLimitComboBox.IsEnabled = !busy && FormatCheckBox.IsChecked == true;
        StartButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:N2} GiB";

    private sealed record WorkRequest(string Source, string TargetRoot, bool FormatFirst, string VolumeLabel,
        int FormatLimitGiB, int JpegQuality, string Suffix, string WebFolderName, string PrintFolderName,
        IReadOnlyList<string> JpegFiles);
    private sealed record WorkProgress(int Percent, string Status, string Detail);

    private sealed class AppSettings
    {
        public int JpegQuality { get; set; } = DefaultQuality;
        public string? Suffix { get; set; } = "-websize";
        public string WebFolderName { get; set; } = "WEBSIZE";
        public string PrintFolderName { get; set; } = "PRINTSIZE";
        public bool FormatFirst { get; set; }
        public string VolumeLabel { get; set; } = "FOTO";
        public int FormatLimitGiB { get; set; } = DefaultFormatLimitGiB;
        public string? TargetDriveRoot { get; set; }
    }

    private sealed class FormatLimitOption(int gib)
    {
        public int GiB { get; } = gib;
        public string DisplayName => $"{GiB} GiB";
    }

    private sealed class DriveOption
    {
        public DriveOption(DriveInfo drive)
        {
            Root = drive.RootDirectory.FullName;
            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "bez názvu" : drive.VolumeLabel;
            DisplayName = $"{drive.Name}  {label}  ({FormatBytes(drive.TotalSize)}, voľné {FormatBytes(drive.AvailableFreeSpace)})";
        }
        public string Root { get; }
        public string DisplayName { get; }
    }
}
