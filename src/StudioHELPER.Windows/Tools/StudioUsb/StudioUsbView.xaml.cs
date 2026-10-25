using Microsoft.Win32;
using StudioHELPER.Core;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using Image = System.Drawing.Image;
using UserControl = System.Windows.Controls.UserControl;
using Localization = StudioHELPER.Core.Localization;

namespace StudioHELPER.Windows.Tools.StudioUsb;

public partial class StudioUsbView : UserControl
{
    private const int DefaultFormatLimitGiB = 32;
    private const int MaxTargetPixels = 30000;
    private static readonly int[] AllowedFormatLimits = [4, 8, 16, 32, 64, 128, 256];
    private readonly SettingsStore _settingsStore;
    private CancellationTokenSource? _cancellation;
    private bool _initializing = true;

    public StudioUsbView(SettingsStore settingsStore)
    {
        InitializeComponent();
        _settingsStore = settingsStore;
        FormatLimitComboBox.ItemsSource = AllowedFormatLimits.Select(value => new NumberOption(value, $"{value} GiB")).ToList();
        ApplyLocalization();
        ApplySettings(settingsStore.Settings.StudioUsb);
        RefreshDrives(settingsStore.Settings.StudioUsb.TargetDriveRoot);
        _initializing = false;
        UpdateControlStates();
    }

    private static string T(string key) => Localization.T(key);

    private void ApplyLocalization()
    {
        SourceFolderLabel.Text = T("SourceFolder");
        BrowseButton.Content = T("Choose");
        TargetUsbLabel.Text = T("TargetUsb");
        RefreshButton.Content = T("Refresh");
        PrintBlockTitle.Text = T("PrintBlock");
        PrintFolderLabel.Text = T("FolderName");
        PrintResampleCheckBox.Content = T("Resample");
        PrintNoUpscaleCheckBox.Content = T("NoUpscale");
        PrintSideLabel.Text = T("Side");
        PrintSizeLabel.Text = T("Size");
        PrintUnitLabel.Text = T("Unit");
        PrintDpiLabel.Text = T("Dpi");
        PrintQualityLabel.Text = T("Quality");
        WebBlockTitle.Text = T("WebBlock");
        WebFolderLabel.Text = T("FolderName");
        WebSuffixLabel.Text = T("Suffix");
        WebNoUpscaleCheckBox.Content = T("NoUpscale");
        WebSideLabel.Text = T("Side");
        WebSizeLabel.Text = T("Size");
        WebUnitLabel.Text = T("Unit");
        WebDpiLabel.Text = T("Dpi");
        WebQualityLabel.Text = T("Quality");
        FormatCheckBox.Content = T("FormatBefore");
        VolumeNameLabel.Text = T("VolumeName");
        FormatLimitLabel.Text = T("MaxMedia");
        SettingsNoteText.Text = T("SettingsNote");
        CancelButton.Content = T("Cancel");
        StartButton.Content = T("Start");
        if (_cancellation is null) StatusTextBlock.Text = DriveComboBox.Items.Count == 0 ? T("NoUsb") : T("Ready");
        PopulateLocalizedOptions();
    }

    private void PopulateLocalizedOptions()
    {
        string printSide = SelectedString(PrintSideComboBox, "long");
        string webSide = SelectedString(WebSideComboBox, "long");
        string printUnit = SelectedString(PrintUnitComboBox, "px");
        string webUnit = SelectedString(WebUnitComboBox, "px");
        int printDpi = SelectedNumber(PrintDpiComboBox, 300);
        int webDpi = SelectedNumber(WebDpiComboBox, 72);

        var sides = new[] { new ChoiceOption("long", T("LongSide")), new ChoiceOption("short", T("ShortSide")) };
        var units = new[] { new ChoiceOption("px", "PX"), new ChoiceOption("cm", "CM") };
        var dpis = new[]
        {
            new NumberOption(72, $"72 dpi ({T("Screen")})"),
            new NumberOption(240, $"240 dpi ({T("Press")})"),
            new NumberOption(300, $"300 dpi ({T("Print")})")
        };
        PrintSideComboBox.ItemsSource = sides;
        WebSideComboBox.ItemsSource = sides;
        PrintUnitComboBox.ItemsSource = units;
        WebUnitComboBox.ItemsSource = units;
        PrintDpiComboBox.ItemsSource = dpis;
        WebDpiComboBox.ItemsSource = dpis;
        PrintSideComboBox.SelectedValue = printSide;
        WebSideComboBox.SelectedValue = webSide;
        PrintUnitComboBox.SelectedValue = printUnit;
        WebUnitComboBox.SelectedValue = webUnit;
        PrintDpiComboBox.SelectedValue = printDpi;
        WebDpiComboBox.SelectedValue = webDpi;
    }

    public void RefreshLanguage()
    {
        ApplyLocalization();
        UpdateControlStates();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = T("SelectSourceTitle"), Multiselect = false };
        bool? result = Window.GetWindow(this) is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (result == true) SourceTextBox.Text = dialog.FolderName;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        RefreshDrives((DriveComboBox.SelectedItem as DriveOption)?.Root);

    private void RefreshDrives(string? preferredRoot = null)
    {
        string? selectedRoot = preferredRoot ?? (DriveComboBox.SelectedItem as DriveOption)?.Root;
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Removable)
            .Select(d => new DriveOption(d)).OrderBy(d => d.Root).ToList();
        DriveComboBox.ItemsSource = drives;
        DriveComboBox.SelectedItem = drives.FirstOrDefault(d => d.Root == selectedRoot) ?? drives.FirstOrDefault();
        StatusTextBlock.Text = drives.Count == 0 ? T("NoUsb") : T("Ready");
    }

    private void FormatCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateControlStates();
    private void OutputOptions_Changed(object sender, RoutedEventArgs e) => UpdateControlStates();
    private void UnitComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateControlStates();

    private void UpdateControlStates()
    {
        if (PrintSettingsPanel is null) return;
        bool busy = _cancellation is not null;
        bool printResample = PrintResampleCheckBox.IsChecked == true;
        PrintSettingsPanel.IsEnabled = !busy && printResample;
        PrintNoUpscaleCheckBox.IsEnabled = !busy && printResample;
        PrintDpiComboBox.IsEnabled = !busy && printResample && SelectedString(PrintUnitComboBox, "px") == "cm";
        WebDpiComboBox.IsEnabled = !busy && SelectedString(WebUnitComboBox, "px") == "cm";
        bool format = FormatCheckBox.IsChecked == true;
        VolumeLabelTextBox.IsEnabled = !busy && format;
        FormatLimitComboBox.IsEnabled = !busy && format;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
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
            StatusTextBlock.Text = T("CompleteStatus");
            AppendLog(T("CompleteLog"));
            MessageBox.Show(T("CompleteDialog"), "StudioUSB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = T("CancelledStatus");
            AppendLog(T("CancelledLog"));
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = T("FailedStatus");
            AppendLog($"{T("Error")}: {ex.Message}");
            MessageBox.Show(ex.Message, $"StudioUSB – {T("Error")}", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (!Directory.Exists(source)) return WarnAndReturn(T("SelectSource"));
        if (DriveComboBox.SelectedItem is not DriveOption drive) return WarnAndReturn(T("SelectUsb"));
        if (FormatLimitComboBox.SelectedItem is not NumberOption limitOption) return WarnAndReturn(T("SelectLimit"));
        bool formatFirst = FormatCheckBox.IsChecked == true;
        DriveInfo currentDrive = ValidateRemovableDrive(drive.Root, formatFirst ? limitOption.Value : null);
        string sourceRoot = Path.GetPathRoot(Path.GetFullPath(source)) ?? string.Empty;
        if (string.Equals(sourceRoot, currentDrive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
            return WarnAndReturn(T("SourceOnUsb"));

        string label = VolumeLabelTextBox.Text.Trim();
        if (formatFirst && !Regex.IsMatch(label, "^[A-Za-z0-9 _-]{1,11}$")) return WarnAndReturn(T("InvalidLabel"));
        string webFolder = WebFolderTextBox.Text.Trim();
        string printFolder = PrintFolderTextBox.Text.Trim();
        string suffix = SuffixTextBox.Text.Trim();
        if (!IsValidSimpleName(webFolder)) return WarnAndReturn(T("InvalidWebFolder"));
        if (!IsValidSimpleName(printFolder)) return WarnAndReturn(T("InvalidPrintFolder"));
        if (string.Equals(webFolder, printFolder, StringComparison.OrdinalIgnoreCase)) return WarnAndReturn(T("SameFolders"));
        if (suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return WarnAndReturn(T("InvalidSuffix"));

        bool printResample = PrintResampleCheckBox.IsChecked == true;
        ResizeSettings print = new(printResample, (int)Math.Round(PrintQualitySlider.Value),
            SelectedString(PrintSideComboBox, "long"), GetTargetPixels(PrintSizeTextBox.Text, PrintUnitComboBox, PrintDpiComboBox),
            PrintNoUpscaleCheckBox.IsChecked == true, string.Empty);
        if (printResample && print.TargetPixels <= 0) return WarnAndReturn(T("InvalidPrintSize"));
        ResizeSettings web = new(true, (int)Math.Round(WebQualitySlider.Value),
            SelectedString(WebSideComboBox, "long"), GetTargetPixels(WebSizeTextBox.Text, WebUnitComboBox, WebDpiComboBox),
            WebNoUpscaleCheckBox.IsChecked == true, suffix);
        if (web.TargetPixels <= 0) return WarnAndReturn(T("InvalidWebSize"));

        var jpgFiles = EnumerateJpegs(source).ToList();
        if (jpgFiles.Count == 0) return WarnAndReturn(T("NoJpegs"));
        long estimatedNeed = jpgFiles.Sum(f => new FileInfo(f).Length) * 3 + 64L * 1024 * 1024;
        if (!formatFirst && currentDrive.AvailableFreeSpace < estimatedNeed) return WarnAndReturn(T("NoSpace"));
        return new WorkRequest(source, currentDrive.RootDirectory.FullName, formatFirst, label, limitOption.Value,
            webFolder, printFolder, print, web, jpgFiles);
    }

    private bool ConfirmRequest(WorkRequest request)
    {
        if (request.FormatFirst)
        {
            DriveInfo drive = ValidateRemovableDrive(request.TargetRoot, request.FormatLimitGiB);
            string message = string.Join("\n",
                string.Format(T("DangerDelete"), drive.Name), "", T("RemovableType"),
                $"{T("Capacity")}: {FormatBytes(drive.TotalSize)}",
                $"{T("SafetyLimit")}: {request.FormatLimitGiB} GiB",
                $"{T("FileSystem")}: {GetFormatFileSystem(drive.TotalSize)}",
                $"{T("NewName")}: {request.VolumeLabel}", "", T("Continue"));
            return MessageBox.Show(message, T("ConfirmFormat"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
        string web = Path.Combine(request.TargetRoot, request.WebFolderName);
        string print = Path.Combine(request.TargetRoot, request.PrintFolderName);
        string txt = Path.Combine(request.TargetRoot, GetSourceName(request.Source) + ".txt");
        if (Directory.Exists(web) || Directory.Exists(print) || File.Exists(txt))
        {
            string message = string.Format(T("ReplaceMessage"), request.WebFolderName, request.PrintFolderName);
            return MessageBox.Show(message, T("ReplaceTitle"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
        return true;
    }

    private void RunWorkflow(WorkRequest request, IProgress<WorkProgress> progress, CancellationToken token)
    {
        if (request.FormatFirst)
        {
            progress.Report(new WorkProgress(1, T("Formatting"), T("SafeFormat")));
            FormatDrive(request.TargetRoot, request.VolumeLabel, request.FormatLimitGiB);
        }
        token.ThrowIfCancellationRequested();
        DriveInfo drive = ValidateRemovableDrive(request.TargetRoot, null);
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
            if (request.Print.Enabled)
                ResizeJpeg(sourceFile, printFile, request.Print);
            else
                File.Copy(sourceFile, printFile, true);
            string status = request.Print.Enabled ? T("CreatePrint") : T("CopyOriginals");
            progress.Report(ProgressFor(++completed, totalSteps, status, relative));
        }

        foreach (string sourceFile in request.JpegFiles)
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(request.Source, sourceFile);
            string directory = Path.GetDirectoryName(relative) ?? string.Empty;
            string outputName = Path.GetFileNameWithoutExtension(relative) + request.Web.Suffix + Path.GetExtension(relative);
            string webFile = Path.Combine(webRoot, directory, outputName);
            Directory.CreateDirectory(Path.GetDirectoryName(webFile)!);
            ResizeJpeg(sourceFile, webFile, request.Web);
            progress.Report(ProgressFor(++completed, totalSteps, T("CreateWeb"), outputName));
        }

        File.WriteAllText(txtPath, string.Empty, new UTF8Encoding(false));
        foreach (string sourceFile in request.JpegFiles)
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(request.Source, sourceFile);
            string printFile = Path.Combine(printRoot, relative);
            string directory = Path.GetDirectoryName(relative) ?? string.Empty;
            string outputName = Path.GetFileNameWithoutExtension(relative) + request.Web.Suffix + Path.GetExtension(relative);
            string webFile = Path.Combine(webRoot, directory, outputName);
            if (!File.Exists(printFile) || !File.Exists(webFile)) throw new IOException(string.Format(T("MissingOutput"), relative));
            if (request.Print.Enabled)
                VerifyResizedImage(sourceFile, printFile, request.Print);
            else if (!HashesMatch(sourceFile, printFile))
                throw new IOException(string.Format(T("OriginalMismatch"), request.PrintFolderName, relative));
            VerifyResizedImage(sourceFile, webFile, request.Web);
            progress.Report(ProgressFor(++completed, totalSteps, T("Verify"), relative));
        }
        if (!File.Exists(txtPath) || new FileInfo(txtPath).Length != 0) throw new IOException(T("BadText"));
    }

    private static int GetTargetPixels(string text, System.Windows.Controls.ComboBox unitCombo,
        System.Windows.Controls.ComboBox dpiCombo)
    {
        if (!TryParsePositiveNumber(text, out double value)) return -1;
        string unit = SelectedString(unitCombo, "px");
        double pixels = ConvertToPixels(value, unit, SelectedNumber(dpiCombo, 300));
        if (pixels < 1 || pixels > MaxTargetPixels) return -1;
        return (int)Math.Round(pixels);
    }

    private static double ConvertToPixels(double value, string unit, int dpi) =>
        unit == "cm" ? value / 2.54 * dpi : value;

    private static bool TryParsePositiveNumber(string text, out double value)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                      double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return parsed && double.IsFinite(value) && value > 0;
    }

    private static string SelectedString(System.Windows.Controls.ComboBox combo, string fallback) =>
        combo.SelectedValue as string ?? fallback;

    private static int SelectedNumber(System.Windows.Controls.ComboBox combo, int fallback) =>
        combo.SelectedValue is int value ? value : fallback;

    private static void FormatDrive(string targetRoot, string label, int formatLimitGiB)
    {
        DriveInfo drive = ValidateRemovableDrive(targetRoot, formatLimitGiB);
        string driveLetter = drive.Name.TrimEnd('\\');
        string fileSystem = GetFormatFileSystem(drive.TotalSize);
        long limitBytes = GiBToBytes(formatLimitGiB);
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $d = [System.IO.DriveInfo]::new('{{driveLetter}}')
            if (-not $d.IsReady) { throw 'USB is not ready.' }
            if ($d.DriveType -ne [System.IO.DriveType]::Removable) { throw 'Drive is not removable media.' }
            if ([uint64]$d.TotalSize -gt [uint64]{{limitBytes}}) { throw 'Drive exceeds the configured safety limit.' }
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
        using Process process = Process.Start(info) ?? throw new InvalidOperationException(T("FormatStartFailed"));
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.Format(T("FormatFailed"), process.ExitCode));
        DriveInfo result = WaitForDrive(targetRoot, formatLimitGiB);
        if (!string.Equals(result.DriveFormat, fileSystem, StringComparison.OrdinalIgnoreCase))
            throw new IOException(string.Format(T("FormatMismatch"), fileSystem));
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
        throw new IOException(T("AfterFormatNotReady"), lastError);
    }

    private static DriveInfo ValidateRemovableDrive(string root, int? formatLimitGiB)
    {
        string fullRoot = Path.GetPathRoot(Path.GetFullPath(root)) ?? throw new IOException(T("InvalidDrive"));
        var drive = new DriveInfo(fullRoot);
        if (!drive.IsReady) throw new IOException(T("UsbNotReady"));
        if (drive.DriveType != DriveType.Removable) throw new IOException(T("NotRemovable"));
        if (string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("SystemDisk"));
        if (formatLimitGiB.HasValue && drive.TotalSize > GiBToBytes(formatLimitGiB.Value))
            throw new IOException(string.Format(T("LimitExceeded"), formatLimitGiB.Value, FormatBytes(drive.TotalSize)));
        return drive;
    }

    private static string GetFormatFileSystem(long totalBytes) => totalBytes <= GiBToBytes(32) ? "FAT32" : "exFAT";
    private static long GiBToBytes(int value) => value * 1024L * 1024L * 1024L;

    private static void ResizeJpeg(string sourcePath, string destinationPath, ResizeSettings settings)
    {
        using var image = Image.FromFile(sourcePath);
        ApplyExifOrientation(image);
        (int width, int height) = CalculateDimensions(image.Width, image.Height, settings);
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
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)settings.Quality);
        bitmap.Save(destinationPath, jpeg, parameters);
    }

    private static (int Width, int Height) CalculateDimensions(int sourceWidth, int sourceHeight, ResizeSettings settings)
    {
        int referenceSide = settings.Side == "short"
            ? Math.Min(sourceWidth, sourceHeight)
            : Math.Max(sourceWidth, sourceHeight);
        double scale = (double)settings.TargetPixels / referenceSide;
        if (settings.NoUpscale) scale = Math.Min(1.0, scale);
        return (Math.Max(1, (int)Math.Round(sourceWidth * scale)),
                Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    private static void ApplyExifOrientation(Image image)
    {
        const int orientationId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationId)) return;
        try
        {
            PropertyItem? property = image.GetPropertyItem(orientationId);
            if (property?.Value is not { Length: > 0 }) return;
            RotateFlipType transform = property.Value[0] switch
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

    private static void VerifyResizedImage(string sourcePath, string outputPath, ResizeSettings settings)
    {
        using var source = Image.FromFile(sourcePath);
        ApplyExifOrientation(source);
        (int expectedWidth, int expectedHeight) = CalculateDimensions(source.Width, source.Height, settings);
        using var output = Image.FromFile(outputPath);
        if (output.Width != expectedWidth || output.Height != expectedHeight)
            throw new IOException(string.Format(T("WrongSize"), Path.GetFileName(outputPath)));
        if (!Path.GetFileNameWithoutExtension(outputPath).EndsWith(settings.Suffix, StringComparison.OrdinalIgnoreCase))
            throw new IOException(string.Format(T("WrongSuffix"), Path.GetFileName(outputPath)));
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

    private void ApplySettings(StudioUsbSettings settings)
    {
        PrintFolderTextBox.Text = DefaultIfEmpty(settings.PrintFolderName, "PRINTSIZE");
        WebFolderTextBox.Text = DefaultIfEmpty(settings.WebFolderName, "WEBSIZE");
        SuffixTextBox.Text = settings.Suffix ?? "-websize";
        PrintResampleCheckBox.IsChecked = settings.PrintResample;
        PrintNoUpscaleCheckBox.IsChecked = settings.PrintNoUpscale;
        WebNoUpscaleCheckBox.IsChecked = settings.WebNoUpscale;
        PrintQualitySlider.Value = Math.Clamp(settings.PrintQuality, 1, 100);
        WebQualitySlider.Value = Math.Clamp(settings.WebQuality, 1, 100);
        PrintSideComboBox.SelectedValue = NormalizeChoice(settings.PrintSide, "long", "short");
        WebSideComboBox.SelectedValue = NormalizeChoice(settings.WebSide, "long", "short");
        PrintUnitComboBox.SelectedValue = NormalizeChoice(settings.PrintUnit, "px", "cm");
        WebUnitComboBox.SelectedValue = NormalizeChoice(settings.WebUnit, "px", "cm");
        PrintSizeTextBox.Text = DefaultIfEmpty(settings.PrintSize, "6000");
        WebSizeTextBox.Text = DefaultIfEmpty(settings.WebSize, "2400");
        PrintDpiComboBox.SelectedValue = NormalizeDpi(settings.PrintDpi, 300);
        WebDpiComboBox.SelectedValue = NormalizeDpi(settings.WebDpi, 72);
        VolumeLabelTextBox.Text = DefaultIfEmpty(settings.VolumeLabel, "FOTO");
        FormatLimitComboBox.SelectedValue = AllowedFormatLimits.Contains(settings.FormatLimitGiB)
            ? settings.FormatLimitGiB : DefaultFormatLimitGiB;
        FormatCheckBox.IsChecked = settings.FormatFirst;
    }

    public void SaveSettings()
    {
        if (_initializing) return;
        try
        {
            StudioUsbSettings settings = _settingsStore.Settings.StudioUsb;
            settings.PrintFolderName = PrintFolderTextBox.Text;
            settings.WebFolderName = WebFolderTextBox.Text;
            settings.Suffix = SuffixTextBox.Text;
            settings.PrintResample = PrintResampleCheckBox.IsChecked == true;
            settings.PrintNoUpscale = PrintNoUpscaleCheckBox.IsChecked == true;
            settings.WebNoUpscale = WebNoUpscaleCheckBox.IsChecked == true;
            settings.PrintQuality = (int)Math.Round(PrintQualitySlider.Value);
            settings.WebQuality = (int)Math.Round(WebQualitySlider.Value);
            settings.PrintSide = SelectedString(PrintSideComboBox, "long");
            settings.WebSide = SelectedString(WebSideComboBox, "long");
            settings.PrintUnit = SelectedString(PrintUnitComboBox, "px");
            settings.WebUnit = SelectedString(WebUnitComboBox, "px");
            settings.PrintSize = PrintSizeTextBox.Text;
            settings.WebSize = WebSizeTextBox.Text;
            settings.PrintDpi = SelectedNumber(PrintDpiComboBox, 300);
            settings.WebDpi = SelectedNumber(WebDpiComboBox, 72);
            settings.FormatFirst = FormatCheckBox.IsChecked == true;
            settings.VolumeLabel = VolumeLabelTextBox.Text;
            settings.FormatLimitGiB = SelectedNumber(FormatLimitComboBox, DefaultFormatLimitGiB);
            settings.TargetDriveRoot = (DriveComboBox.SelectedItem as DriveOption)?.Root;
            _settingsStore.Save();
        }
        catch { }
    }

    private static string DefaultIfEmpty(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static string NormalizeChoice(string? value, string first, string second) => value == second ? second : first;
    private static int NormalizeDpi(int value, int fallback) => value is 72 or 240 or 300 ? value : fallback;
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
        MessageBox.Show(message, "StudioUSB", MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void SetBusy(bool busy)
    {
        BrowseButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        DriveComboBox.IsEnabled = !busy;
        PrintFolderTextBox.IsEnabled = !busy;
        WebFolderTextBox.IsEnabled = !busy;
        SuffixTextBox.IsEnabled = !busy;
        PrintResampleCheckBox.IsEnabled = !busy;
        WebNoUpscaleCheckBox.IsEnabled = !busy;
        WebSideComboBox.IsEnabled = !busy;
        WebSizeTextBox.IsEnabled = !busy;
        WebUnitComboBox.IsEnabled = !busy;
        WebQualitySlider.IsEnabled = !busy;
        FormatCheckBox.IsEnabled = !busy;
        StartButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        if (!busy) UpdateControlStates();
        else
        {
            PrintSettingsPanel.IsEnabled = false;
            PrintNoUpscaleCheckBox.IsEnabled = false;
            PrintDpiComboBox.IsEnabled = false;
            WebDpiComboBox.IsEnabled = false;
            VolumeLabelTextBox.IsEnabled = false;
            FormatLimitComboBox.IsEnabled = false;
        }
    }

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024d / 1024d:N2} GiB";

    private sealed record ResizeSettings(bool Enabled, int Quality, string Side, int TargetPixels, bool NoUpscale, string Suffix);
    private sealed record WorkRequest(string Source, string TargetRoot, bool FormatFirst, string VolumeLabel,
        int FormatLimitGiB, string WebFolderName, string PrintFolderName, ResizeSettings Print, ResizeSettings Web,
        IReadOnlyList<string> JpegFiles);
    private sealed record WorkProgress(int Percent, string Status, string Detail);

    private sealed class ChoiceOption(string value, string displayName)
    {
        public string Value { get; } = value;
        public string DisplayName { get; } = displayName;
    }

    private sealed class NumberOption(int value, string displayName)
    {
        public int Value { get; } = value;
        public string DisplayName { get; } = displayName;
    }

    private sealed class DriveOption
    {
        public DriveOption(DriveInfo drive)
        {
            Root = drive.RootDirectory.FullName;
            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "—" : drive.VolumeLabel;
            DisplayName = $"{drive.Name}  {label}  ({FormatBytes(drive.TotalSize)} / {FormatBytes(drive.AvailableFreeSpace)})";
        }
        public string Root { get; }
        public string DisplayName { get; }
    }
}
