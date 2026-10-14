using STranslate.Helpers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace STranslate.Core;

/// <summary>
///     启动追踪：在日志系统就绪前记录每次进程启动的来源与去向（单实例退出、提权重启、正常运行），
///     用于排查开机自启等“进程起来又悄悄退出”的问题
/// </summary>
internal static class StartupTrace
{
    private const long MaxFileBytes = 256 * 1024;
    private const string FileName = "startup-trace.log";

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(DataLocation.LogDirectory);
            var filePath = Path.Combine(DataLocation.LogDirectory, FileName);
            var file = new FileInfo(filePath);
            if (file.Exists && file.Length > MaxFileBytes)
                file.Delete();

            File.AppendAllText(
                filePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // 追踪失败不能影响启动
        }
    }

    public static string DescribeProcess()
    {
        var args = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        return $"elevated={UACHelper.IsUserAdministrator()} parent={DescribeParentProcess()} " +
               $"args=[{args}] cwd={Environment.CurrentDirectory}";
    }

    private static string DescribeParentProcess()
    {
        try
        {
            var info = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(
                Process.GetCurrentProcess().Handle, 0, ref info, Marshal.SizeOf(info), out _);
            if (status != 0)
                return "unknown";

            var parentId = info.InheritedFromUniqueProcessId.ToInt32();
            try
            {
                using var parent = Process.GetProcessById(parentId);
                return $"{parent.ProcessName}({parentId})";
            }
            catch
            {
                return $"exited({parentId})";
            }
        }
        catch
        {
            return "unknown";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation processInformation,
        int processInformationLength, out int returnLength);
}
