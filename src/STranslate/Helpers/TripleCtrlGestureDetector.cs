namespace STranslate.Helpers;

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
    private bool _leftDown;
    private bool _rightDown;
    private bool _isolated;
    private int _pressCount;
    private long _lastRelease;
    private long _lastInput;

    internal int PressCount => _pressCount;
    internal bool CtrlDown => _leftDown || _rightDown;
    internal bool Isolated => _isolated;

    internal bool OnKeyEvent(ushort virtualKey, ushort flags, long timestamp)
    {
        var inputGap = timestamp - _lastInput;
        if (_lastInput != 0 && inputGap > StaleInputMs)
        {
            if (isKeyDown is null)
            {
                _otherKeysDown.Clear();
                _leftDown = false;
                _rightDown = false;
            }
            _pressCount = 0;
        }
        _lastInput = timestamp;

        var isUp = (flags & KeyBreak) != 0;
        var isCtrl = virtualKey is Control or LeftControl or RightControl;
        if (!isCtrl)
        {
            if (isUp)
                _otherKeysDown.Remove(virtualKey);
            else if (_otherKeysDown.Add(virtualKey) && CtrlDown)
            {
                _isolated = false;
                _pressCount = 0;
            }
            return false;
        }

        if (!isUp && isKeyDown is not null && (inputGap > 100 || !CtrlDown))
        {
            _otherKeysDown.RemoveWhere(key => !isKeyDown(key));
            if (_leftDown && !isKeyDown(LeftControl)) _leftDown = false;
            if (_rightDown && !isKeyDown(RightControl)) _rightDown = false;
        }
        var right = virtualKey == RightControl || (virtualKey == Control && (flags & Extended) != 0);
        var wasDown = CtrlDown;
        if (right)
        {
            if (_rightDown == !isUp) return false;
            _rightDown = !isUp;
        }
        else
        {
            if (_leftDown == !isUp) return false;
            _leftDown = !isUp;
        }

        if (!wasDown && CtrlDown)
        {
            _isolated = _otherKeysDown.Count == 0;
            if (!_isolated)
                _pressCount = 0;
            return false;
        }

        if (wasDown && CtrlDown)
        {
            _isolated = false;
            _pressCount = 0;
            return false;
        }

        if (!wasDown || !_isolated)
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
