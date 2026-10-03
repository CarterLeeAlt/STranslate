using STranslate.Helpers;

namespace STranslate.Tests;

public class TripleCtrlGestureDetectorTests
{
    private const ushort Ctrl = 0x11;
    private const ushort LeftCtrl = 0xA2;
    private const ushort RightCtrl = 0xA3;
    private const ushort KeyBreak = 1;
    private const ushort Extended = 2;

    [Fact]
    public void DetectsThreeCompleteShortPressesBetweenPollingSamples()
    {
        var detector = new TripleCtrlGestureDetector();

        Assert.False(detector.OnKeyEvent(Ctrl, 0, 100));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 108));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 160));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 168));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 220));
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 228));
    }

    [Fact]
    public void DoesNotCountRepeatedDownOrShortcutWithCtrl()
    {
        var detector = new TripleCtrlGestureDetector();

        Assert.False(detector.OnKeyEvent(Ctrl, 0, 100));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 110));
        Assert.False(detector.OnKeyEvent(0x43, 0, 115));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 130));
        Assert.False(detector.OnKeyEvent(0x43, KeyBreak, 135));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 180));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 190));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 240));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 250));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 300));
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 310));
    }

    [Fact]
    public void DoesNotCountCtrlWhenAnotherKeyWasAlreadyHeld()
    {
        var detector = new TripleCtrlGestureDetector();

        detector.OnKeyEvent(0x43, 0, 90);
        detector.OnKeyEvent(Ctrl, 0, 100);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 120));
        detector.OnKeyEvent(0x43, KeyBreak, 130);
        detector.OnKeyEvent(Ctrl, 0, 150);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 160));
        detector.OnKeyEvent(Ctrl, 0, 200);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 210));
        detector.OnKeyEvent(Ctrl, 0, 250);
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 260));
    }

    [Fact]
    public void TracksRightCtrlAndRejectsTwoCtrlsHeldTogether()
    {
        var detector = new TripleCtrlGestureDetector();

        Assert.False(detector.OnKeyEvent(Ctrl, Extended, 100));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak | Extended, 110));
        Assert.False(detector.OnKeyEvent(LeftCtrl, 0, 150));
        Assert.False(detector.OnKeyEvent(RightCtrl, 0, 155));
        Assert.False(detector.OnKeyEvent(LeftCtrl, KeyBreak, 160));
        Assert.False(detector.OnKeyEvent(RightCtrl, KeyBreak, 165));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 200));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 210));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 250));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 260));
        Assert.False(detector.OnKeyEvent(Ctrl, 0, 300));
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 310));
    }

    [Fact]
    public void RecoversAfterMissingOtherKeyReleaseAndLongInputGap()
    {
        var detector = new TripleCtrlGestureDetector();

        detector.OnKeyEvent(0x43, 0, 100);
        detector.OnKeyEvent(Ctrl, 0, 6_100);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 6_110));
        detector.OnKeyEvent(Ctrl, 0, 6_160);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 6_170));
        detector.OnKeyEvent(Ctrl, 0, 6_220);
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 6_230));
    }

    [Fact]
    public void ReleasesStaleHeldKeyWithoutRequiringFiveSecondsOfSilence()
    {
        var physicallyHeld = new HashSet<ushort>();
        var detector = new TripleCtrlGestureDetector(physicallyHeld.Contains);
        detector.OnKeyEvent(0x43, 0, 100);
        for (var i = 0; i < 3; i++)
        {
            detector.OnKeyEvent(Ctrl, 0, 300 + i * 100);
            Assert.Equal(i == 2, detector.OnKeyEvent(Ctrl, KeyBreak, 350 + i * 100));
        }
    }

    [Theory]
    [InlineData(300)]
    [InlineData(6_300)]
    public void PreservesActuallyHeldKeyDuringStateReconciliation(long start)
    {
        var physicallyHeld = new HashSet<ushort> { 0x43 };
        var detector = new TripleCtrlGestureDetector(physicallyHeld.Contains);
        detector.OnKeyEvent(0x43, 0, 100);
        for (var i = 0; i < 3; i++)
        {
            detector.OnKeyEvent(Ctrl, 0, start + i * 100);
            Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, start + 50 + i * 100));
        }
    }

    [Fact]
    public void CompleteSecondGestureIsNotDiscardedAfterFirstTrigger()
    {
        var detector = new TripleCtrlGestureDetector();
        for (var i = 0; i < 3; i++)
        {
            detector.OnKeyEvent(Ctrl, 0, 100 + i * 40);
            Assert.Equal(i == 2, detector.OnKeyEvent(Ctrl, KeyBreak, 110 + i * 40));
        }
        for (var i = 0; i < 3; i++)
        {
            detector.OnKeyEvent(Ctrl, 0, 300 + i * 40);
            Assert.Equal(i == 2, detector.OnKeyEvent(Ctrl, KeyBreak, 310 + i * 40));
        }
    }

    [Fact]
    public void ResetsExpiredWindowAndRejectsCopyEventsWithoutBlockingNextGesture()
    {
        var detector = new TripleCtrlGestureDetector();

        detector.OnKeyEvent(Ctrl, 0, 100);
        detector.OnKeyEvent(Ctrl, KeyBreak, 110);
        detector.OnKeyEvent(Ctrl, 0, 650);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 660));
        detector.OnKeyEvent(Ctrl, 0, 700);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 710));
        detector.OnKeyEvent(Ctrl, 0, 750);
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 760));
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 770));
        detector.OnKeyEvent(Ctrl, 0, 775);
        detector.OnKeyEvent(0x43, 0, 776);
        detector.OnKeyEvent(0x43, KeyBreak, 777);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 778));
        detector.OnKeyEvent(Ctrl, 0, 800);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 810));
        detector.OnKeyEvent(Ctrl, 0, 850);
        Assert.False(detector.OnKeyEvent(Ctrl, KeyBreak, 860));
        detector.OnKeyEvent(Ctrl, 0, 900);
        Assert.True(detector.OnKeyEvent(Ctrl, KeyBreak, 910));
    }
}
