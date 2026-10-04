using STranslate.Core;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using System.Windows.Interop;

namespace STranslate.Helpers;

/// <summary>
/// 接收全局键盘按下与松开事件以识别三击左 Ctrl。
/// </summary>
public static class CtrlSameCHelper
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const ushort KeyboardUsagePage = 0x01;
    private const ushort KeyboardUsage = 0x06;
    private const uint KeyboardInputType = 1;
    private static readonly uint RawInputHeaderSize = (uint)(8 + 2 * IntPtr.Size);

    private static HwndSource? _source;
    private static DispatcherTimer? _healthTimer;
    private static TripleCtrlGestureDetector _detector = CreateDetector();
    private static bool _isListening;
    private static long _lastCtrlEvent;

    private static TripleCtrlGestureDetector CreateDetector() => new(key => (GetAsyncKeyState(key) & 0x8000) != 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Window;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(ref RawInputDevice device, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices([Out] RawInputDevice[]? devices, ref uint count, uint size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    public static event Action? OnCtrlSameC;

    public static bool IsListening => _isListening;

    public static void Start()
    {
        if (_isListening) return;

        _isListening = true;
        _healthTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _healthTimer.Tick += CheckRegistration;
        _healthTimer.Start();
        EnsureKeyboardRegistration();
    }

    internal static bool EnsureKeyboardRegistration()
    {
        if (!_isListening) return false;
        if (HasKeyboardRegistration()) return true;

        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
        HwndSource? source = null;
        try
        {
            var parameters = new HwndSourceParameters("STranslate.TripleCtrlInput")
            {
                ParentWindow = new nint(-3),
                WindowStyle = 0
            };
            source = new HwndSource(parameters);
            source.AddHook(WndProc);
            var device = new RawInputDevice
            {
                UsagePage = KeyboardUsagePage,
                Usage = KeyboardUsage,
                Flags = RidevInputSink,
                Window = source.Handle
            };
            if (!RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            {
                Serilog.Log.Error("三击 Ctrl 原始键盘输入注册失败，错误码 {Error}", Marshal.GetLastWin32Error());
                return false;
            }

            _detector = CreateDetector();
            _source = source;
            _lastCtrlEvent = Environment.TickCount64;
            Serilog.Log.Information("三击 Ctrl 原始键盘输入监听已启动，独立消息窗口 {Window}", source.Handle);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "三击 Ctrl 监听窗口创建失败，将在健康检查时重试");
            return false;
        }
        finally
        {
            if (_source != source) source?.Dispose();
        }
    }

    public static void Stop()
    {
        if (!_isListening) return;

        var ownsRegistration = HasKeyboardRegistration();
        _isListening = false;
        _healthTimer?.Stop();
        if (_healthTimer is not null) _healthTimer.Tick -= CheckRegistration;
        _healthTimer = null;
        _source?.RemoveHook(WndProc);
        var source = _source;
        _source = null;
        var device = new RawInputDevice
        {
            UsagePage = KeyboardUsagePage,
            Usage = KeyboardUsage,
            Flags = RidevRemove
        };
        if (ownsRegistration && !RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            Serilog.Log.Warning("三击 Ctrl 原始键盘输入注销失败，错误码 {Error}", Marshal.GetLastWin32Error());
        source?.Dispose();
        Serilog.Log.Information("三击 Ctrl 原始键盘输入监听已停止");
    }

    public static void Toggle()
    {
        if (_isListening)
            Stop();
        else
            Start();
    }

    internal static bool HasKeyboardRegistration()
    {
        uint count = 0;
        var deviceSize = (uint)Marshal.SizeOf<RawInputDevice>();
        if (_source is null || _source.IsDisposed ||
            GetRegisteredRawInputDevices(null, ref count, deviceSize) == uint.MaxValue)
            return false;
        var devices = new RawInputDevice[count];
        var result = GetRegisteredRawInputDevices(devices, ref count, deviceSize);
        return result != uint.MaxValue && devices.Take((int)result).Any(device =>
            device.UsagePage == KeyboardUsagePage && device.Usage == KeyboardUsage &&
            device.Window == _source.Handle && (device.Flags & RidevInputSink) != 0);
    }

    private static void CheckRegistration(object? sender, EventArgs e)
    {
        if (!_isListening) return;
        if (!HasKeyboardRegistration())
        {
            Serilog.Log.Warning("三击 Ctrl 键盘注册已失效，重新创建监听");
            EnsureKeyboardRegistration();
        }
        Serilog.Log.Information("三击 Ctrl 监听健康检查：注册 {Registered}，距最近 Ctrl {Elapsed}ms",
            HasKeyboardRegistration(), Environment.TickCount64 - _lastCtrlEvent);
    }

    private static nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmInput || !_isListening)
            return 0;

        uint size = 0;
        var sizeResult = GetRawInputData(lParam, RidInput, 0, ref size, RawInputHeaderSize);
        if (sizeResult == uint.MaxValue)
        {
            Serilog.Log.Warning("三击 Ctrl 原始输入读取长度失败，错误码 {Error}", Marshal.GetLastWin32Error());
            return 0;
        }
        if (sizeResult != 0 || size < RawInputHeaderSize + 16 || size > 4096)
            return 0;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var bytesRead = GetRawInputData(lParam, RidInput, buffer, ref size, RawInputHeaderSize);
            if (bytesRead == uint.MaxValue)
            {
                Serilog.Log.Warning("三击 Ctrl 原始输入读取事件失败，错误码 {Error}", Marshal.GetLastWin32Error());
                return 0;
            }
            if (bytesRead < RawInputHeaderSize + 16 || Marshal.ReadInt32(buffer) != KeyboardInputType)
                return 0;

            var flags = (ushort)Marshal.ReadInt16(buffer, (int)RawInputHeaderSize + 2);
            var key = (ushort)Marshal.ReadInt16(buffer, (int)RawInputHeaderSize + 6);
            if (key == 0xFF)
                return 0;

            var isCtrl = TripleCtrlGestureDetector.IsLeftControl(key, flags);
            var wasDown = _detector.CtrlDown;
            var wasIsolated = _detector.Isolated;
            var triggered = _detector.OnKeyEvent(key, flags, Environment.TickCount64);
            if (isCtrl)
            {
                _lastCtrlEvent = Environment.TickCount64;
                Serilog.Log.Information("三击 Ctrl：原始输入 {Transition}，已计 {Count}/3，独立按键 {Isolated}",
                    (flags & 1) == 0 ? "按下" : "松开", _detector.PressCount, _detector.Isolated);
            }
            else if (wasDown && wasIsolated && !_detector.Isolated)
            {
                Serilog.Log.Information("三击 Ctrl：检测到组合键，本次点击作废");
            }

            if (triggered)
            {
                if (HotkeyExecutionGuard.ShouldSkipGlobalHotkey())
                    Serilog.Log.Information("三击 Ctrl 触发被跳过（初始化向导/全局热键禁用/全屏忽略）");
                else
                {
                    Serilog.Log.Information("三击 Ctrl 触发划词翻译");
                    OnCtrlSameC?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "三击 Ctrl 原始键盘输入处理异常");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // 前台 WM_INPUT 必须留给 DefWindowProc 完成清理。
        return 0;
    }
}
