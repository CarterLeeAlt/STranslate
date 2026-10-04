using ChefKeys;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using NHotkey;
using NHotkey.Wpf;
using STranslate.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace STranslate.Helpers;

public class HotkeyMapper
{
    private static readonly ILogger<HotkeyMapper> _logger;
    private static readonly Internationalization _i18n;
    private const string LWin = "LWin";
    private const string RWin = "RWin";
    private static readonly IReadOnlyDictionary<string, string> ReservedGlobalHotkeyResourceKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl + C"] = "HotkeyReservedClipboardCopy"
        };

    #region Global Keyboard Hook

    private static UnhookWindowsHookExSafeHandle? _hookHandle;
    private static HOOKPROC? _hookProc;
    private static readonly Lock _hookStateLock = new();
    private static readonly HoldKeyGate _holdKeyGate = new();
    private static (Action OnPress, Action OnRelease)? _holdKeyActions;

    #endregion

    static HotkeyMapper()
    {
        _logger = Ioc.Default.GetRequiredService<ILogger<HotkeyMapper>>();
        _i18n = Ioc.Default.GetRequiredService<Internationalization>();
    }

    #region 传统热键注册

    /// <summary>
    /// 注册热键
    /// </summary>
    /// <param name="hotkeyStr">热键字符串</param>
    /// <param name="action">触发回调</param>
    /// <returns></returns>
    internal static HotkeyRegistrationResult SetHotkey(string hotkeyStr, Action action, bool showConflictDialog = true)
    {
        var hotkey = new HotkeyModel(hotkeyStr);
        hotkeyStr = hotkey.ToString();

        // 避免注册空热键导致异常 谷歌浏览器通知会触发注册里的回调
        // https://github.com/STranslate/STranslate/issues/559
        if (string.IsNullOrEmpty(hotkeyStr))
            return HotkeyRegistrationResult.Success;
        //_logger.LogInformation("Registering hotkey: {HotkeyStr}", hotkeyStr);

        if (IsReservedGlobalHotkey(hotkey))
        {
            _logger.LogWarning("Skipped reserved global hotkey: {HotkeyStr}", hotkeyStr);
            return HotkeyRegistrationResult.Failed;
        }

        // 避免 NHotkey 注册和低级钩子的按住键使用同一个主键。
        if (IsRegisteredHoldKey(hotkey.CharKey))
            return HotkeyRegistrationResult.Failed;

        try
        {
            // Win 键必须用 ChefKeys
            if (hotkeyStr == LWin || hotkeyStr == RWin)
                return SetWithChefKeys(hotkeyStr, action)
                    ? HotkeyRegistrationResult.Success
                    : HotkeyRegistrationResult.Failed;

            HotkeyManager.Current.AddOrReplace(hotkeyStr, hotkey.CharKey, hotkey.ModifierKeys, (_, _) => action.Invoke());
            return HotkeyRegistrationResult.Success;
        }
        catch (HotkeyAlreadyRegisteredException e)
        {
            if (showConflictDialog)
            {
                _logger.LogError(e, "Error registering hotkey: {HotkeyStr}", hotkeyStr);
                ShowDialog(string.Format(_i18n.GetTranslation("RegisterHotkeyFailed"), hotkeyStr));
            }
            else
            {
                _logger.LogWarning("Hotkey is temporarily unavailable, registration will be retried: {HotkeyStr}", hotkeyStr);
            }

            return HotkeyRegistrationResult.Conflict;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error registering hotkey: {HotkeyStr}", hotkeyStr);
            ShowDialog(string.Format(_i18n.GetTranslation("RegisterHotkeyFailed"), hotkeyStr));
            return HotkeyRegistrationResult.Failed;
        }
    }

    internal static bool RemoveHotkey(string hotkeyStr)
    {
        try
        {
            if (string.IsNullOrEmpty(hotkeyStr))
                return true;

            if (IsReservedGlobalHotkey(new HotkeyModel(hotkeyStr)))
                return true;

            if (hotkeyStr == LWin || hotkeyStr == RWin)
                return RemoveWithChefKeys(hotkeyStr);

            HotkeyManager.Current.Remove(hotkeyStr);

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error removing hotkey: {HotkeyStr}", hotkeyStr);
            ShowDialog(string.Format(_i18n.GetTranslation("UnregisterHotkeyFailed"), hotkeyStr));
            return false;
        }
    }

    #endregion

    #region 全局钩子方式（低级键盘钩子）

    /// <summary>
    /// 启动全局键盘监听（使用低级钩子）
    /// </summary>
    public static void StartGlobalKeyboardMonitoring()
    {
        if (_hookHandle != null && !_hookHandle.IsInvalid) return;

        try
        {
            _hookProc = HookCallback;
            
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            
            var hModule = PInvoke.GetModuleHandle(curModule?.ModuleName);
            
            _hookHandle = PInvoke.SetWindowsHookEx(
                WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
                _hookProc,
                hModule,
                0);

            if (_hookHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to set keyboard hook. Error code: {Error}", error);
                _hookHandle = null;
                return;
            }

            _logger.LogInformation("Global keyboard monitoring started (Low-level hook)");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to start global keyboard monitoring");
            _hookHandle?.Dispose();
            _hookHandle = null;
        }
    }

    /// <summary>
    /// 停止全局键盘监听
    /// </summary>
    public static void StopGlobalKeyboardMonitoring()
    {
        if (_hookHandle == null || _hookHandle.IsInvalid) return;

        try
        {
            _hookHandle.Dispose();
            _hookHandle = null;
            _hookProc = null;

            lock (_hookStateLock)
            {
                _holdKeyGate.SetHoldKey(Key.None);
                _holdKeyGate.Reset();
                _holdKeyActions = null;
            }
            _logger.LogInformation("Global keyboard monitoring stopped");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to stop global keyboard monitoring");
        }
    }

    /// <summary>
    /// 注册按住按键时的功能（按下时启用，抬起时禁用）
    /// </summary>
    /// <param name="key">要监听的按键</param>
    /// <param name="onPress">按下时执行的操作</param>
    /// <param name="onRelease">抬起时执行的操作</param>
    public static void RegisterHoldKey(Key key, Action onPress, Action onRelease)
    {
        lock (_hookStateLock)
        {
            _holdKeyGate.SetHoldKey(key);
            _holdKeyActions = (onPress, onRelease);
        }

        _logger.LogInformation("Registered hold key action for {Key}", key);
    }

    private static readonly VIRTUAL_KEY[] ModifierVirtualKeys =
    [
        VIRTUAL_KEY.VK_LMENU, VIRTUAL_KEY.VK_RMENU, VIRTUAL_KEY.VK_LCONTROL, VIRTUAL_KEY.VK_RCONTROL,
        VIRTUAL_KEY.VK_LSHIFT, VIRTUAL_KEY.VK_RSHIFT, VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_RWIN
    ];

    /// <summary>
    /// 按住键仅支持单独按下；其他修饰键按下时放行组合键（如 Alt+F4 关闭窗口）。
    /// 按住键本身是修饰键（右 Ctrl）时按左右区分查询并排除自身。
    /// 低级键盘钩子中 GetKeyState 反映的是本线程状态，需用 GetAsyncKeyState 读取全局状态。
    /// </summary>
    private static bool IsOtherModifierDown(Key holdKey)
    {
        var holdVirtualKey = (VIRTUAL_KEY)KeyInterop.VirtualKeyFromKey(holdKey);
        return ModifierVirtualKeys.Any(key => key != holdVirtualKey && IsKeyDown(key));
    }

    private static bool IsKeyDown(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;

    private static bool IsRegisteredHoldKey(Key key)
    {
        if (key == Key.None)
            return false;

        lock (_hookStateLock)
        {
            return _holdKeyGate.HoldKey == key;
        }
    }

    private static LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var kbdStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // 按住键只认物理按键：取词模拟 Ctrl+C 前会注入右 Ctrl 等修饰键松开，不能当作用户松键。
            if ((kbdStruct.flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0)
                return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);

            var key = KeyInterop.KeyFromVirtualKey((int)kbdStruct.vkCode);

            uint message = (uint)wParam;
            bool isKeyDown = message == PInvoke.WM_KEYDOWN || message == PInvoke.WM_SYSKEYDOWN;
            bool isKeyUp = message == PInvoke.WM_KEYUP || message == PInvoke.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                HoldKeyDecision decision;
                Action? action;

                lock (_hookStateLock)
                {
                    decision = isKeyDown
                        ? _holdKeyGate.OnKeyDown(key, Environment.TickCount64, IsOtherModifierDown, ShouldSkipHotkey)
                        : _holdKeyGate.OnKeyUp(key);
                    action = decision.Action switch
                    {
                        HoldKeyAction.Press => _holdKeyActions?.OnPress,
                        HoldKeyAction.Release => _holdKeyActions?.OnRelease,
                        _ => null
                    };
                }

                // 执行按住按键的 OnPress / OnRelease 操作
                if (action is not null)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing {Action} action for key {Key}", decision.Action, key);
                    }
                }

                if (decision.Suppress)
                    return new LRESULT(1); // 返回非零值阻止按键传递
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    #endregion

    #region 辅助方法

    internal static bool CheckAvailability(HotkeyModel currentHotkey)
    {
        try
        {
            HotkeyManager.Current.AddOrReplace("HotkeyAvailabilityTest", currentHotkey.CharKey, currentHotkey.ModifierKeys, (sender, e) => { });
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            HotkeyManager.Current.Remove("HotkeyAvailabilityTest");
        }
    }

    internal static SpecialKeyState CheckModifiers()
    {
        SpecialKeyState state = new SpecialKeyState();
        if ((PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) & 0x8000) != 0)
        {
            state.ShiftPressed = true;
        }
        if ((PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0)
        {
            state.CtrlPressed = true;
        }
        if ((PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_MENU) & 0x8000) != 0)
        {
            state.AltPressed = true;
        }
        if ((PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_LWIN) & 0x8000) != 0 ||
            (PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_RWIN) & 0x8000) != 0)
        {
            state.WinPressed = true;
        }

        return state;
    }

    /// <summary>
    /// 判断热键是否为应用内部保留的全局热键。
    /// </summary>
    /// <param name="hotkey">待检查的热键。</param>
    /// <returns>保留热键返回 true，否则返回 false。</returns>
    internal static bool IsReservedGlobalHotkey(HotkeyModel hotkey)
        => TryGetReservedGlobalHotkeyMessageKey(hotkey, out _);

    /// <summary>
    /// 尝试获取保留全局热键对应的本地化提示资源键。
    /// </summary>
    /// <param name="hotkey">待检查的热键。</param>
    /// <param name="resourceKey">保留热键对应的本地化资源键。</param>
    /// <returns>保留热键返回 true，否则返回 false。</returns>
    internal static bool TryGetReservedGlobalHotkeyMessageKey(HotkeyModel hotkey, out string resourceKey)
    {
        if (ReservedGlobalHotkeyResourceKeys.TryGetValue(hotkey.ToString(), out var foundResourceKey))
        {
            resourceKey = foundResourceKey;
            return true;
        }

        resourceKey = string.Empty;
        return false;
    }

    #endregion

    #region Private Methods

    private static bool SetWithChefKeys(string hotkeyStr, Action action)
    {
        try
        {
            ChefKeysManager.RegisterHotkey(hotkeyStr, hotkeyStr, action);
            ChefKeysManager.Start();

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error registering hotkey with ChefKeys: {HotkeyStr}", hotkeyStr);

            ShowDialog(string.Format(_i18n.GetTranslation("RegisterHotkeyFailed"), hotkeyStr));

            return false;
        }
    }

    private static bool RemoveWithChefKeys(string hotkeyStr)
    {
        try
        {
            ChefKeysManager.UnregisterHotkey(hotkeyStr);
            ChefKeysManager.Stop();

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error removing hotkey: {HotkeyStr}", hotkeyStr);

            ShowDialog(string.Format(_i18n.GetTranslation("UnregisterHotkeyFailed"), hotkeyStr));

            return false;
        }
    }

    private static void ShowDialog(string message)
    {
        try
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    AppMessageBox.Show(
                        message,
                        Constant.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning,
                        MessageBoxResult.OK);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to show message box in dispatcher");
                }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to show message box");
        }
    }

    /// <summary>
    /// 检查是否应该跳过热键执行
    /// </summary>
    private static bool ShouldSkipHotkey() => HotkeyExecutionGuard.ShouldSkipGlobalHotkey();

    #endregion
}

internal enum HotkeyRegistrationResult
{
    Success,
    Conflict,
    Failed
}
