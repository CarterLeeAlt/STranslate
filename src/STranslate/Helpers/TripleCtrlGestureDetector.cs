namespace STranslate.Helpers;

/// <summary>
/// 三击只认左 Ctrl；右 Ctrl 留给增量翻译，按普通按键处理，会打断正在进行的三击。
/// </summary>
internal sealed class TripleCtrlGestureDetector(Func<ushort, bool>? isKeyDown = null)
{
    private const ushort Control = 0x11;
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;
    private const ushort KeyBreak = 1;
    private const ushort Extended = 2;
    private const long ClickWindowMs = 500;
    private const long StaleInputMs = 5_000;

    private readonly HashSet<ushort> _otherKeysDown = [];
    private bool _ctrlDown;
    private bool _isolated;
    private int _pressCount;
    private long _lastRelease;
    private long _lastInput;

    internal int PressCount => _pressCount;
    internal bool CtrlDown => _ctrlDown;
    internal bool Isolated => _isolated;

    /// <summary>
    /// 原始输入里 Ctrl 常以通用码 0x11 上报，左右靠 E0 扩展标志区分。
    /// </summary>
    internal static bool IsLeftControl(ushort virtualKey, ushort flags) =>
        virtualKey == LeftControl || (virtualKey == Control && (flags & Extended) == 0);

    internal bool OnKeyEvent(ushort virtualKey, ushort flags, long timestamp)
    {
        var inputGap = timestamp - _lastInput;
        if (_lastInput != 0 && inputGap > StaleInputMs)
        {
            if (isKeyDown is null)
            {
                _otherKeysDown.Clear();
                _ctrlDown = false;
            }
            _pressCount = 0;
        }
        _lastInput = timestamp;

        var isUp = (flags & KeyBreak) != 0;
        if (!IsLeftControl(virtualKey, flags))
        {
            // 右 Ctrl 统一记为 0xA3，状态核对时才能用 GetAsyncKeyState 单独查询它。
            if (virtualKey == Control)
                virtualKey = RightControl;
            if (isUp)
                _otherKeysDown.Remove(virtualKey);
            else if (_otherKeysDown.Add(virtualKey))
            {
                // 三击要求连续按左 Ctrl：中间按下任何其他键（含右 Ctrl）都重新计数。
                if (_ctrlDown)
                    _isolated = false;
                _pressCount = 0;
            }
            return false;
        }

        if (!isUp && isKeyDown is not null && (inputGap > 100 || !_ctrlDown))
        {
            _otherKeysDown.RemoveWhere(key => !isKeyDown(key));
            if (_ctrlDown && !isKeyDown(LeftControl)) _ctrlDown = false;
        }
        if (_ctrlDown == !isUp) return false;
        _ctrlDown = !isUp;

        if (_ctrlDown)
        {
            _isolated = _otherKeysDown.Count == 0;
            if (!_isolated)
                _pressCount = 0;
            return false;
        }

        if (!_isolated)
            return false;

        if (_pressCount > 0 && timestamp - _lastRelease > ClickWindowMs)
            _pressCount = 0;
        _lastRelease = timestamp;
        if (++_pressCount < 3)
            return false;

        _pressCount = 0;
        return true;
    }
}
