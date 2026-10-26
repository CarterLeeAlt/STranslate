using System.IO;

namespace STranslate.Core;

public class DataLocation
{
    public static readonly string PortableDataPath =
        Path.Combine(Constant.ProgramDirectory, Constant.PortableFolderName);

    public static readonly string AppExePath = Path.Combine(Constant.ProgramDirectory, Constant.AppName + ".exe");
    public static readonly string HostExePath = Path.Combine(Constant.ProgramDirectory, Constant.HostExeName);
    public static readonly string StartupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    public static readonly string InfoFilePath = Path.Combine(Constant.ProgramDirectory, Constant.InfoFileName);
    public static readonly string BackupFilePath = Path.Combine(Constant.ProgramDirectory, Constant.BackupFileName);

    public static string VersionLogDirectory => Path.Combine(LogDirectory, Constant.Version);
    public static string LogDirectory => Path.Combine(DataDirectory(), Constant.Logs);
    public static readonly string CacheDirectory = Path.Combine(DataDirectory(), Constant.Cache);
    public static readonly string SettingsDirectory = Path.Combine(DataDirectory(), Constant.Settings);
    public static readonly string PluginSettingsDirectory = Path.Combine(SettingsDirectory, Constant.Plugins);
    public static readonly string PluginCacheDirectory = Path.Combine(DataDirectory(), Constant.Cache, Constant.Plugins);
    public static readonly string PluginsDirectory = Path.Combine(DataDirectory(), Constant.Plugins);
    public static readonly string TempDirectory = Path.Combine(DataDirectory(), "Temp");

    public static readonly string DbConnectionString = $"Data Source={CacheDirectory}\\history.db";

    /// <summary>
    ///     Plugin搜索目录
    /// </summary>
    public static readonly string[] PluginDirectories = [Constant.PreinstalledDirectory, PluginsDirectory];

    /// <summary>
    ///     所有用户数据固定写在程序目录下的 PortableConfig，不再回退到 %APPDATA%。
    /// </summary>
    public static string DataDirectory()
    {
        Directory.CreateDirectory(PortableDataPath);
        return PortableDataPath;
    }

    /// <summary>
    ///     清空上次运行遗留的临时文件（插件解压、光标、计划任务 XML 等），再重建空目录。
    /// </summary>
    public static void ResetTempDirectory()
    {
        try
        {
            if (Directory.Exists(TempDirectory))
                Directory.Delete(TempDirectory, true);
        }
        catch
        {
            // 删除失败不阻塞启动，残留留待下次启动继续清理
        }

        Directory.CreateDirectory(TempDirectory);
    }
}
