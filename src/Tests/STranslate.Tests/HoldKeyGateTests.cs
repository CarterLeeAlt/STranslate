using STranslate.Helpers;
using System.Windows.Input;

namespace STranslate.Tests;

public class HoldKeyGateTests
{
    private static readonly HoldKeyDecision Pass = new(false);
    private static readonly HoldKeyDecision Suppress = new(true);
    private static readonly HoldKeyDecision Press = new(true, HoldKeyAction.Press);
    private static readonly HoldKeyDecision Release = new(true, HoldKeyAction.Release);

    private static HoldKeyGate CreateGate(Key holdKey = Key.RightCtrl)
    {
        var gate = new HoldKeyGate();
        gate.SetHoldKey(holdKey);
        return gate;
    }

    private static HoldKeyDecision Down(HoldKeyGate gate, Key key, long timestamp = 0,
        bool otherModifierDown = false, bool skip = false) =>
        gate.OnKeyDown(key, timestamp, _ => otherModifierDown, () => skip);

    [Fact]
    public void SolitaryHoldKeyPressIsSuppressedAndPaired()
    {
        var gate = CreateGate();

        Assert.Equal(Press, Down(gate, Key.RightCtrl, 0));
        Assert.Equal(Suppress, Down(gate, Key.RightCtrl, 500));
        Assert.Equal(Suppress, Down(gate, Key.RightCtrl, 533));
        Assert.Equal(Release, gate.OnKeyUp(Key.RightCtrl));
        Assert.Equal(Press, Down(gate, Key.RightCtrl, 1_000));
    }

    [Fact]
    public void HoldKeyItselfIsExcludedFromModifierCheck()
    {
        var gate = CreateGate();
        Key? checkedFor = null;

        gate.OnKeyDown(Key.RightCtrl, 0, key => { checkedFor = key; return false; }, () => false);

        Assert.Equal(Key.RightCtrl, checkedFor);
    }

    [Fact]
    public void ComboWithOtherModifierPassesThroughWholePress()
    {
        var gate = CreateGate(Key.F4);

        Assert.Equal(Pass, Down(gate, Key.F4, 0, otherModifierDown: true));
        Assert.Equal(Pass, Down(gate, Key.F4, 500));
        Assert.Equal(Pass, gate.OnKeyUp(Key.F4));
        Assert.Equal(Press, Down(gate, Key.F4, 1_000));
    }

    [Fact]
    public void SkippedPressPassesThroughWithoutActions()
    {
        var gate = CreateGate();

        Assert.Equal(Pass, Down(gate, Key.RightCtrl, 0, skip: true));
        Assert.Equal(Pass, Down(gate, Key.C, 100));
        Assert.Equal(Pass, gate.OnKeyUp(Key.C));
        Assert.Equal(Pass, gate.OnKeyUp(Key.RightCtrl));
    }

    [Fact]
    public void ActivePressStillReleasesWhenSkipStartsMidHold()
    {
        var gate = CreateGate();

        Assert.Equal(Press, Down(gate, Key.RightCtrl, 0));
        Assert.Equal(Pass, Down(gate, Key.RightCtrl, 500, skip: true));
        Assert.Equal(Release, gate.OnKeyUp(Key.RightCtrl));
    }

    [Fact]
    public void SwallowsWholePressOfKeyStartedDuringHold()
    {
        var gate = CreateGate();

        Down(gate, Key.RightCtrl, 0);
        Assert.Equal(Suppress, Down(gate, Key.C, 150));
        Assert.Equal(Suppress, Down(gate, Key.C, 700));
        Assert.Equal(Release, gate.OnKeyUp(Key.RightCtrl));
        Assert.Equal(Suppress, Down(gate, Key.C, 750));
        Assert.Equal(Suppress, gate.OnKeyUp(Key.C));
        Assert.Equal(Pass, Down(gate, Key.C, 800));
        Assert.Equal(Pass, gate.OnKeyUp(Key.C));
    }

    [Fact]
    public void ModifiersAndKeysHeldBeforeTheHoldKeyPassThrough()
    {
        var gate = CreateGate();

        Assert.Equal(Pass, Down(gate, Key.W, 0));
        Down(gate, Key.RightCtrl, 100);
        Assert.Equal(Pass, Down(gate, Key.W, 130));
        Assert.Equal(Pass, Down(gate, Key.LeftShift, 150));
        Assert.Equal(Pass, gate.OnKeyUp(Key.LeftShift));
        Assert.Equal(Pass, gate.OnKeyUp(Key.W));
    }

    [Fact]
    public void StopsSwallowingWhenHoldKeyEventsGoStale()
    {
        var gate = CreateGate();

        Down(gate, Key.RightCtrl, 0);
        Assert.Equal(Pass, Down(gate, Key.C, HoldKeyGate.SwallowWindowMs + 1));
        Assert.Equal(Pass, gate.OnKeyUp(Key.C));
        Down(gate, Key.RightCtrl, 5_000);
        Assert.Equal(Suppress, Down(gate, Key.V, 5_000 + HoldKeyGate.SwallowWindowMs));
    }

    [Fact]
    public void ClearedHoldKeyInterceptsNothing()
    {
        var gate = CreateGate();
        gate.SetHoldKey(Key.None);

        Assert.Equal(Pass, Down(gate, Key.RightCtrl, 0));
        Assert.Equal(Pass, Down(gate, Key.C, 100));
        Assert.Equal(Pass, gate.OnKeyUp(Key.C));
        Assert.Equal(Pass, gate.OnKeyUp(Key.RightCtrl));
    }
}
