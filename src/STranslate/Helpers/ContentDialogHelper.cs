using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using System.Windows;
using System.Windows.Controls;

namespace STranslate.Helpers;

internal static class ContentDialogHelper
{
    public static void HighlightPrimaryAction(ContentDialog dialog)
    {
        dialog.Opened += (sender, _) =>
        {
            // 覆盖模板的默认按钮样式，保留原有回车键行为。
            var primaryButton = (Button)sender.Template.FindName("PrimaryButton", sender);
            var closeButton = (Button)sender.Template.FindName("CloseButton", sender);
            primaryButton.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.AccentButtonStyleKey);
            closeButton.SetResourceReference(FrameworkElement.StyleProperty, ThemeKeys.DefaultButtonStyleKey);
        };
    }
}
