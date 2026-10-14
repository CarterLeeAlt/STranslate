using CommunityToolkit.Mvvm.ComponentModel;

namespace STranslate.Plugin.Ocr.RapidOcrBuiltIn.ViewModel;

public partial class SettingsViewModel(IPluginContext context, Settings settings, Action applyIdleTimeout) : ObservableObject
{
    [ObservableProperty] public partial long UseCount { get; set; } = settings.UseCount;

    [ObservableProperty] public partial double IdleUnloadMinutes { get; set; } = settings.IdleUnloadMinutes;

    partial void OnUseCountChanged(long value)
    {
        settings.UseCount = value;
        context.SaveSettingStorage<Settings>();
    }

    partial void OnIdleUnloadMinutesChanged(double value)
    {
        // NumberBox 清空时为 NaN，按默认值处理
        var minutes = double.IsNaN(value) ? 10 : (int)Math.Clamp(Math.Round(value), 0, 1440);
        if (minutes != value)
        {
            IdleUnloadMinutes = minutes;
            return;
        }
        settings.IdleUnloadMinutes = minutes;
        context.SaveSettingStorage<Settings>();
        applyIdleTimeout();
    }
}
