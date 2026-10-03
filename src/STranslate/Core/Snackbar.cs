using STranslate.Controls;
using STranslate.Plugin;
using System.Windows;
using System.Windows.Controls;

namespace STranslate.Core;

public class Snackbar : ISnackbar, IDisposable
{
    /// <summary>
    /// 附加属性：窗口内容区上边缘距窗口顶部的偏移，提示框将与其对齐（未设置时保持默认 16）
    /// </summary>
    public static readonly DependencyProperty ContentTopOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ContentTopOffset",
            typeof(double),
            typeof(Snackbar),
            new PropertyMetadata(16.0));

    public static void SetContentTopOffset(DependencyObject element, double value) =>
        element.SetValue(ContentTopOffsetProperty, value);

    public static double GetContentTopOffset(DependencyObject element) =>
        (double)element.GetValue(ContentTopOffsetProperty);

    private readonly Dictionary<Window, SnackbarEntry> _snackbars = [];
    private bool _disposed;

    public void Show(
        string message,
        Severity severity = Severity.Informational,
        int durationMs = 3000,
        string? actionText = null,
        Action? actionCallback = null)
    {
        if (_disposed || Application.Current?.Dispatcher == null)
            return;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
                return;

            var activeWindow = Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

            if (activeWindow == null)
                return;

            var snackbar = GetOrCreateSnackbar(activeWindow);
            snackbar?.Show(message, severity, durationMs, actionText, actionCallback);
        });
    }

    public void ShowSuccess(string message, int durationMs = 3000) =>
        Show(message, Severity.Success, durationMs);

    public void ShowError(string message, int durationMs = 4000) =>
        Show(message, Severity.Error, durationMs);

    public void ShowWarning(string message, int durationMs = 3000) =>
        Show(message, Severity.Warning, durationMs);

    public void ShowInfo(string message, int durationMs = 3000) =>
        Show(message, Severity.Informational, durationMs);

    public void Dispose()
    {
        if (_disposed)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess() && !dispatcher.HasShutdownStarted)
        {
            dispatcher.Invoke(Dispose);
            return;
        }

        _disposed = true;
        foreach (var window in _snackbars.Keys.ToArray())
            ReleaseSnackbar(window);
    }

    private SnackbarContainer? GetOrCreateSnackbar(Window window)
    {
        if (_snackbars.TryGetValue(window, out var existingEntry))
            return existingEntry.Container;

        if (window.Content is not Panel panel)
            return null;

        var container = new SnackbarContainer();
        container.TopOffset = GetContentTopOffset(window);
        panel.Children.Add(container);
        Panel.SetZIndex(container, 9999);

        _snackbars[window] = new SnackbarEntry(panel, container);
        window.Closed += Window_Closed;
        return container;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            ReleaseSnackbar(window);
    }

    private void ReleaseSnackbar(Window window)
    {
        if (!_snackbars.Remove(window, out var entry))
            return;

        window.Closed -= Window_Closed;
        entry.Parent.Children.Remove(entry.Container);
        entry.Container.Dispose();
    }

    private sealed record SnackbarEntry(Panel Parent, SnackbarContainer Container);
}
