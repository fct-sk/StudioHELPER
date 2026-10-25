namespace StudioHELPER.Core;

public sealed class AppSettings
{
    public string Language { get; set; } = "system";
    public bool RunAtSystemStartup { get; set; } = true;
    public bool MinimizeToSystemTray { get; set; } = true;
    public StudioUsbSettings StudioUsb { get; set; } = new();
}

public sealed class StudioUsbSettings
{
    public string PrintFolderName { get; set; } = "PRINTSIZE";
    public string WebFolderName { get; set; } = "WEBSIZE";
    public string? Suffix { get; set; } = "-websize";
    public bool PrintResample { get; set; }
    public bool PrintNoUpscale { get; set; } = true;
    public bool WebNoUpscale { get; set; } = true;
    public int PrintQuality { get; set; } = 95;
    public int WebQuality { get; set; } = 80;
    public string PrintSide { get; set; } = "long";
    public string WebSide { get; set; } = "long";
    public string PrintUnit { get; set; } = "px";
    public string WebUnit { get; set; } = "px";
    public string PrintSize { get; set; } = "6000";
    public string WebSize { get; set; } = "2400";
    public int PrintDpi { get; set; } = 300;
    public int WebDpi { get; set; } = 72;
    public bool FormatFirst { get; set; }
    public string VolumeLabel { get; set; } = "FOTO";
    public int FormatLimitGiB { get; set; } = 32;
    public string? TargetDriveRoot { get; set; }
}
