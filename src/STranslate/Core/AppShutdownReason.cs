namespace STranslate.Core;

public enum AppShutdownReason
{
    ExternalOrUnknown,
    SystemSessionEnding,
    TrayMenu,
    TrayDoubleClick,
    KeyboardShortcut,
    LocalBackup,
    LocalRestore,
    WebDavBackup,
    WebDavRestore,
    PluginChange,
}
