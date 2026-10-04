using System.Windows.Input;

namespace STranslate.Helpers;

internal enum HoldKeyAction
{
    None,
    Press,
    Release
}

/// <summary>
/// 低级键盘钩子对一次按键事件的处理结果。
/// </summary>
/// <param name="Suppress">是否阻止按键传给前台应用。</param>
/// <param name="Action">需要触发的按住键回调。</param>
internal readonly record struct HoldKeyDecision(bool Suppress, HoldKeyAction Action = HoldKeyAction.None);

/// <summary>
/// 增量翻译按住键的判定：只维护物理按键状态并给出拦截与触发决策，不调用系统 API，便于测试。
/// </summary>
internal sealed class HoldKeyGate
{
    /// <summary>
    /// 距最近一次按住键事件（按下或自动重复）多久之内按下的其他键会被吞掉。
    /// 单独按住时键盘每隔几百毫秒发送重复事件；超过窗口说明按住键可能已在钩子之外松开（如焦点切到管理员窗口），不再吞键。
    /// </summary>
    internal const long SwallowWindowMs = 1_500;

    private static readonly HoldKeyDecision Pass = new(false);
    private static readonly HoldKeyDecision Suppress = new(true);

    private readonly HashSet<Key> _pressedKeys = [];
    /// <summary>按住键生效期间新按下的其他按键，整次按压都不传给前台应用。</summary>
    private readonly HashSet<Key> _swallowedKeys = [];
    /// <summary>与修饰键组合按下的按住键（如 Alt+F4），整次按压原样放行且不触发功能。</summary>
    private bool _holdKeyPassthrough;
    private bool _holdActive;
    private long _lastHoldKeyEvent;

    internal Key HoldKey { get; private set; } = Key.None;

    internal void SetHoldKey(Key key)
    {
        HoldKey = key;
        _holdKeyPassthrough = false;
        _holdActive = false;
        _swallowedKeys.Clear();
    }

    internal void Reset()
    {
        _pressedKeys.Clear();
        _holdKeyPassthrough = false;
        _holdActive = false;
        _swallowedKeys.Clear();
    }

    /// <param name="key">按下的键。</param>
    /// <param name="timestamp">事件时间（毫秒）。</param>
    /// <param name="isOtherModifierDown">除按住键自身外是否有修饰键按下。</param>
    /// <param name="shouldSkip">当前是否跳过全局热键（全屏忽略、禁用全局热键等）。</param>
    internal HoldKeyDecision OnKeyDown(Key key, long timestamp, Func<Key, bool> isOtherModifierDown, Func<bool> shouldSkip)
    {
        var isRepeated = !_pressedKeys.Add(key);

        if (key == HoldKey && key != Key.None)
        {
            if (!isRepeated && isOtherModifierDown(key))
                _holdKeyPassthrough = true;
            if (_holdKeyPassthrough || shouldSkip())
                return Pass;

            _lastHoldKeyEvent = timestamp;
            if (isRepeated)
                return Suppress;

            _holdActive = true;
            return new(true, HoldKeyAction.Press);
        }

        if (_swallowedKeys.Contains(key))
            return Suppress;

        // 按住键已被拦截，前台应用看不到它；习惯性按右 Ctrl+C 会只剩字母 c 替换掉选中文本，因此整次吞掉。
        if (!isRepeated && _holdActive && !IsModifierKey(key) && timestamp - _lastHoldKeyEvent <= SwallowWindowMs)
        {
            _swallowedKeys.Add(key);
            return Suppress;
        }

        return Pass;
    }

    internal HoldKeyDecision OnKeyUp(Key key)
    {
        _pressedKeys.Remove(key);

        if (key == HoldKey && key != Key.None)
        {
            // 只有触发过 Press 的按压才拦截松开并配对 Release，保证前台应用收到的按下/松开成对。
            var wasActive = _holdActive;
            _holdActive = false;
            _holdKeyPassthrough = false;
            return wasActive ? new(true, HoldKeyAction.Release) : Pass;
        }

        return _swallowedKeys.Remove(key) ? Suppress : Pass;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
