namespace StudioHELPER.Core;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string InstallerUrl,
    string? ChecksumUrl,
    string? ReleaseNotes);
