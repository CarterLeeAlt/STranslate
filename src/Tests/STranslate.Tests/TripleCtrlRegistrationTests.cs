using STranslate.Helpers;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace STranslate.Tests;

public class TripleCtrlRegistrationTests
{
    [Fact]
    public void RegistrationSurvivesMainWindowLifecycleAndRecoversAfterRemoval()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using (var main = new HwndSource(new HwndSourceParameters("TripleCtrlRegistrationTest")))
                {
                    CtrlSameCHelper.Start();
                    CtrlSameCHelper.Start();
                    Assert.True(CtrlSameCHelper.HasKeyboardRegistration());
                }
                Assert.True(CtrlSameCHelper.HasKeyboardRegistration());
                var device = new RawInputDevice { UsagePage = 1, Usage = 6, Flags = 1 };
                Assert.True(RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawInputDevice>()));
                Assert.False(CtrlSameCHelper.HasKeyboardRegistration());
                Assert.True(CtrlSameCHelper.EnsureKeyboardRegistration());
                Assert.True(CtrlSameCHelper.HasKeyboardRegistration());
                CtrlSameCHelper.Stop();
                Assert.False(CtrlSameCHelper.HasKeyboardRegistration());
                CtrlSameCHelper.Start();
                Assert.True(CtrlSameCHelper.HasKeyboardRegistration());
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                CtrlSameCHelper.Stop();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "原始键盘注册测试未结束");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

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
}
