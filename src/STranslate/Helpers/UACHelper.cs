using Microsoft.Win32;
using STranslate.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace STranslate.Helpers;

internal class UACHelper
{
    /// <summary>
    /// 使用 Host 进程按指定模式拉起程序，可选等待旧进程退出后再启动。
    /// </summary>
    /// <param name="mode">启动模式。</param>
    /// <param name="delay">启动延时（秒）。</param>
    /// <param name="waitPid">等待退出的旧进程 PID。</param>
    /// <param name="waitTimeoutSec">等待旧进程退出超时时间（秒）。</param>
    public static void Run(StartMode mode, int delay = 0, int? waitPid = null, int waitTimeoutSec = 6)
    {
        var modeStr = mode switch
        {
            StartMode.Normal => "direct",
            StartMode.Admin => "elevated",
            StartMode.SkipUACAdmin => "task",
            _ => throw new InvalidOperationException("Unsupported start mode for admin")
        };
        var target = mode switch
        {
            StartMode.Normal => DataLocation.AppExePath,
            StartMode.Admin => DataLocation.AppExePath,
            StartMode.SkipUACAdmin => Constant.TaskName,
            _ => throw new InvalidOperationException("Unsupported start mode for admin")
        };

        string[] args = ["start", "-m", modeStr, "-t", target];
        if (delay > 0)
        {
            args = [.. args, "-d", delay.ToString()];
        }

        if (waitPid is > 0)
        {
            var normalizedTimeoutSec = waitTimeoutSec > 0 ? waitTimeoutSec : 1;
            args = [.. args, "--wait-pid", waitPid.Value.ToString(), "--wait-timeout", normalizedTimeoutSec.ToString()];
        }

        Utilities.ExecuteProgram(DataLocation.HostExePath, args);
    }

    /// <summary>
    ///     确保“跳过 UAC”用的提权任务存在且为最新配置（无触发器，由 Host 按需运行）
    /// </summary>
    public static void Create()
    {
        var result = EnsureTask(new TaskConfiguration
        {
            ExecutablePath = DataLocation.AppExePath,
            TaskName = Constant.TaskName,
            HighestPrivilege = true
        });
        if (!result.Success)
            Serilog.Log.Warning("跳过 UAC 任务创建失败: {Message} {Error}", result.Message, result.Error);
    }

    public static void Delete()
    {
        var result = DeleteTaskWithElevation(Constant.TaskName);
        if (!result.Success)
            Serilog.Log.Warning("跳过 UAC 任务删除失败: {Message} {Error}", result.Message, result.Error);
    }

    public static bool Exist()
    {
        return TaskExists(Constant.TaskName).Success;
    }

    /// <summary>
    ///     确保开机自启任务存在且为最新配置：当前用户登录后延迟启动，管理员类模式直接以最高权限运行（不再经由程序自身提权重启）
    /// </summary>
    /// <param name="highestPrivilege">是否以最高权限运行</param>
    /// <returns>true: 任务已就绪 false: 注册失败</returns>
    public static bool EnsureAutoStartTask(bool highestPrivilege)
    {
        // exe 被勾选“以管理员身份运行此程序”时，以普通权限启动会因需要提权而失败（启动文件夹/Run 项也会被系统静默跳过），
        // 此时不论启动模式都只能以最高权限运行
        if (!highestPrivilege && HasRunAsAdminCompatFlag(DataLocation.AppExePath))
        {
            Serilog.Log.Information("程序已被设置为“以管理员身份运行”（兼容性 RUNASADMIN），开机自启任务改用最高权限");
            highestPrivilege = true;
        }

        var result = EnsureTask(new TaskConfiguration
        {
            ExecutablePath = DataLocation.AppExePath,
            TaskName = Constant.AutoStartTaskName,
            Description = "Start STranslate when the user logs on",
            HighestPrivilege = highestPrivilege,
            LogonTrigger = true,
            LogonDelay = AutoStartLogonDelay
        });
        if (result.Success)
            Serilog.Log.Information("开机自启任务已就绪（最高权限：{Highest}）: {Message}", highestPrivilege, result.Message);
        else
            Serilog.Log.Warning("开机自启任务注册失败: {Message} {Error}", result.Message, result.Error);
        return result.Success;
    }

    /// <summary>
    ///     删除开机自启任务（不存在时直接返回成功）
    /// </summary>
    public static bool RemoveAutoStartTask()
    {
        if (!TaskExists(Constant.AutoStartTaskName).Success)
            return true;

        var result = DeleteTaskWithElevation(Constant.AutoStartTaskName);
        if (!result.Success)
            Serilog.Log.Warning("开机自启任务删除失败: {Message} {Error}", result.Message, result.Error);
        return result.Success;
    }

    #region TaskScheduler

    private const string DefaultAuthorPrefix = "STranslate";
    private const string DefaultUserSid = "S-1-5-21-0-0-0-1001";
    private const string TaskXmlNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>
    ///     登录后延迟启动，等任务栏与托盘就绪（与 Clash Verge 等常驻托盘程序的做法一致）
    /// </summary>
    private static readonly TimeSpan AutoStartLogonDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     ERROR_CANCELLED：用户在 UAC 弹窗中选择了“否”
    /// </summary>
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// 创建任务计划（无触发器，手动执行）
    /// </summary>

    public static TaskOperationResult CreateTask(string exePath, string taskName, string? author = null)
    {
        var config = new TaskConfiguration
        {
            ExecutablePath = exePath,
            TaskName = taskName,
            Author = author
        };
        return CreateTask(config);
    }

    /// <summary>
    /// 创建任务计划（使用配置对象）
    /// </summary>
    public static TaskOperationResult CreateTask(TaskConfiguration config)
    {
        try
        {
            // 参数验证
            var validationResult = ValidateTaskConfiguration(config);
            if (!validationResult.Success)
                return validationResult;

            // 检查文件是否存在
            if (!File.Exists(config.ExecutablePath))
                return TaskOperationResult.CreateFailure($"找不到可执行文件: {config.ExecutablePath}");

            // 设置默认值
            config.WorkingDirectory ??= Path.GetDirectoryName(config.ExecutablePath);
            config.Author ??= $"{DefaultAuthorPrefix} - {Environment.UserName.ToLower()}";

            // 创建XML配置并执行（/F 覆盖同名任务；最高权限任务在非管理员进程中会经 UAC 提权创建）
            return CreateTaskFromConfiguration(config);
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure("创建任务时发生异常", ex.Message, ex);
        }
    }

    /// <summary>
    /// 删除任务计划
    /// </summary>
    public static TaskOperationResult DeleteTask(string taskName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(taskName))
                return TaskOperationResult.CreateFailure("任务名称不能为空");

            if (!IsRunAsAdministrator())
                return TaskOperationResult.CreateFailure("需要管理员权限才能删除任务计划");

            return ExecuteSchTasksCommand($"/Delete /TN \"{taskName}\" /F", "删除任务");
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure("删除任务时发生异常", ex.Message, ex);
        }
    }

    /// <summary>
    /// 检查任务是否存在
    /// </summary>
    public static TaskOperationResult TaskExists(string taskName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(taskName))
                return TaskOperationResult.CreateFailure("任务名称不能为空");

            var result = ExecuteSchTasksCommand($"/Query /TN \"{taskName}\"", "查询任务");
            result.Message = result.Success ? "任务存在" : "任务不存在";
            return result;
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure("查询任务时发生异常", ex.Message, ex);
        }
    }

    /// <summary>
    /// 运行指定的任务计划
    /// </summary>
    public static TaskOperationResult RunTask(string taskName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(taskName))
                return TaskOperationResult.CreateFailure("任务名称不能为空");

            return ExecuteSchTasksCommand($"/Run /TN \"{taskName}\"", "运行任务");
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure("运行任务时发生异常", ex.Message, ex);
        }
    }

    /// <summary>
    /// 获取任务详细信息
    /// </summary>
    public static TaskOperationResult GetTaskInfo(string taskName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(taskName))
                return TaskOperationResult.CreateFailure("任务名称不能为空");

            return ExecuteSchTasksCommand($"/Query /TN \"{taskName}\" /FO LIST /V", "获取任务信息");
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure("获取任务信息时发生异常", ex.Message, ex);
        }
    }

    /// <summary>
    /// 列出所有任务（可选择性过滤）
    /// </summary>
    public static TaskOperationResult ListTasks(string? filter = null)
    {
        try
        {
            string args = "/Query /FO LIST";
            if (!string.IsNullOrWhiteSpace(filter))
                args += $" /TN \"{filter}\"";

            return ExecuteSchTasksCommand(args, "列出任务");
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure("列出任务时发生异常", ex.Message, ex);
        }
    }

    #region 私有方法

    private static TaskOperationResult ValidateTaskConfiguration(TaskConfiguration config)
    {
        if (config == null)
            return TaskOperationResult.CreateFailure("配置对象不能为空");

        if (string.IsNullOrWhiteSpace(config.ExecutablePath))
            return TaskOperationResult.CreateFailure("可执行文件路径不能为空");

        if (string.IsNullOrWhiteSpace(config.TaskName))
            return TaskOperationResult.CreateFailure("任务名称不能为空");

        if (config.TaskName.Contains('"') || config.TaskName.Contains('\\'))
            return TaskOperationResult.CreateFailure("任务名称包含非法字符");

        return TaskOperationResult.CreateSuccess("配置验证通过");
    }

    private static bool IsRunAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.ToString() ?? DefaultUserSid;
        }
        catch
        {
            return DefaultUserSid;
        }
    }

    private static TaskOperationResult CreateTaskFromConfiguration(TaskConfiguration config)
    {
        string? tempXmlPath = null;
        try
        {
            // 生成XML内容
            string xmlContent = CreateTaskXml(config);
            tempXmlPath = Path.Combine(DataLocation.TempDirectory, $"Task_{Guid.NewGuid():N}.xml");

            // 写入临时文件 - 使用 Unicode 编码（UTF-16LE）
            File.WriteAllText(tempXmlPath, xmlContent, Encoding.Unicode);

            // 执行创建命令
            return ExecuteSchTasksWithElevation(
                $"/Create /TN \"{config.TaskName}\" /XML \"{tempXmlPath}\" /F",
                "创建任务",
                elevateFirst: config.HighestPrivilege && !IsRunAsAdministrator());
        }
        finally
        {
            // 清理临时文件
            if (tempXmlPath != null && File.Exists(tempXmlPath))
            {
                try
                {
                    File.Delete(tempXmlPath);
                }
                catch
                {
                    // 忽略清理失败
                }
            }
        }
    }

    private static TaskOperationResult ExecuteSchTasksCommand(string arguments, string operationName)
    {
        using var process = CreateSchTasksProcess(arguments);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        // 异步读取输出，避免死锁
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();

        // 开始异步读取
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        string output = outputBuilder.ToString().Trim();
        string error = errorBuilder.ToString().Trim();
        bool success = process.ExitCode == 0;

        return new TaskOperationResult
        {
            Success = success,
            Message = success ? $"{operationName}成功" : $"{operationName}失败 (ExitCode: {process.ExitCode})",
            Output = output,
            Error = error,
            ExitCode = process.ExitCode
        };
    }

    /// <summary>
    ///     任务已存在且配置与期望一致时不做任何事，否则（重新）创建；用于启动时自愈，避免每次启动都触发提权
    /// </summary>
    private static TaskOperationResult EnsureTask(TaskConfiguration config)
    {
        var validationResult = ValidateTaskConfiguration(config);
        if (!validationResult.Success)
            return validationResult;

        if (IsTaskUpToDate(config))
            return TaskOperationResult.CreateSuccess("任务已是最新配置");

        return CreateTask(config);
    }

    /// <summary>
    ///     读取已注册任务的 XML，检查程序路径、运行级别、运行时限、登录触发器是否与期望一致
    /// </summary>
    private static bool IsTaskUpToDate(TaskConfiguration config)
    {
        var query = ExecuteSchTasksCommand($"/Query /TN \"{config.TaskName}\" /XML", "查询任务配置");
        if (!query.Success || string.IsNullOrWhiteSpace(query.Output))
            return false;

        try
        {
            XNamespace ns = TaskXmlNamespace;
            var doc = XDocument.Parse(query.Output);

            var command = doc.Descendants(ns + "Command").FirstOrDefault()?.Value.Trim().Trim('"');
            if (!string.Equals(command, config.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                return false;

            // 导出的 XML 中 LeastPrivilege 为默认值，会省略 RunLevel 元素
            var runLevel = doc.Descendants(ns + "RunLevel").FirstOrDefault()?.Value ?? "LeastPrivilege";
            if (runLevel != GetRunLevel(config))
                return false;

            var principalUser = doc.Descendants(ns + "Principal").Elements(ns + "UserId").FirstOrDefault()?.Value;
            if (!string.Equals(principalUser, GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase))
                return false;

            // 缺省 ExecutionTimeLimit 即 PT72H，常驻程序会在 72 小时后被强制结束
            var settings = doc.Descendants(ns + "Settings").FirstOrDefault();
            var timeLimit = settings?.Element(ns + "ExecutionTimeLimit")?.Value;
            if (timeLimit is null || XmlConvert.ToTimeSpan(timeLimit) != config.ExecutionTimeLimit)
                return false;
            if (settings?.Element(ns + "Enabled")?.Value == "false")
                return false;

            var logonTrigger = doc.Descendants(ns + "LogonTrigger").FirstOrDefault();
            if (config.LogonTrigger != (logonTrigger is not null))
                return false;
            if (logonTrigger is not null)
            {
                if (logonTrigger.Element(ns + "Enabled")?.Value == "false")
                    return false;
                if (!string.Equals(logonTrigger.Element(ns + "UserId")?.Value, GetCurrentUserName(), StringComparison.OrdinalIgnoreCase))
                    return false;
                var delay = logonTrigger.Element(ns + "Delay")?.Value;
                if ((delay is null ? TimeSpan.Zero : XmlConvert.ToTimeSpan(delay)) != config.LogonDelay)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TaskOperationResult DeleteTaskWithElevation(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
            return TaskOperationResult.CreateFailure("任务名称不能为空");

        return ExecuteSchTasksWithElevation($"/Delete /TN \"{taskName}\" /F", "删除任务", elevateFirst: false);
    }

    /// <summary>
    ///     执行 schtasks；非管理员进程执行失败（如无权覆盖/删除管理员创建的任务）时经 UAC 提权重试
    /// </summary>
    /// <param name="elevateFirst">已知需要管理员权限时直接提权执行，避免一次必然失败的尝试</param>
    private static TaskOperationResult ExecuteSchTasksWithElevation(string arguments, string operationName, bool elevateFirst)
    {
        if (elevateFirst)
            return ExecuteSchTasksElevated(arguments, operationName);

        var result = ExecuteSchTasksCommand(arguments, operationName);
        if (result.Success || IsRunAsAdministrator())
            return result;

        return ExecuteSchTasksElevated(arguments, operationName);
    }

    private static TaskOperationResult ExecuteSchTasksElevated(string arguments, string operationName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null)
                return TaskOperationResult.CreateFailure($"{operationName}失败：无法启动提权进程");

            if (!process.WaitForExit(30000))
                return TaskOperationResult.CreateFailure($"{operationName}失败：提权进程超时");

            var success = process.ExitCode == 0;
            return new TaskOperationResult
            {
                Success = success,
                Message = success ? $"{operationName}成功（已提权）" : $"{operationName}失败 (ExitCode: {process.ExitCode})",
                ExitCode = process.ExitCode
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return TaskOperationResult.CreateFailure($"{operationName}失败：用户取消了 UAC 提权", ex.Message, ex);
        }
        catch (Exception ex)
        {
            return TaskOperationResult.CreateFailure($"{operationName}时发生异常", ex.Message, ex);
        }
    }

    private static string GetCurrentUserName()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
        catch
        {
            return $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
    }

    /// <summary>
    ///     检查 exe 是否在兼容性设置中被勾选“以管理员身份运行此程序”（当前用户或所有用户）
    /// </summary>
    private static bool HasRunAsAdminCompatFlag(string exePath)
    {
        const string layersPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(layersPath);
                if (key is null)
                    continue;

                var matched = key.GetValueNames()
                    .Where(name => string.Equals(name, exePath, StringComparison.OrdinalIgnoreCase))
                    .Any(name => key.GetValue(name) is string layers &&
                                 layers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                     .Contains("RUNASADMIN", StringComparer.OrdinalIgnoreCase));
                if (matched)
                    return true;
            }
            catch
            {
                // 读取失败按未设置处理
            }
        }

        return false;
    }

    private static string GetRunLevel(TaskConfiguration config) =>
        config.HighestPrivilege ? "HighestAvailable" : "LeastPrivilege";

    private static Process CreateSchTasksProcess(string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"chcp 65001 > nul && schtasks {arguments}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
    }

    private static string CreateTaskXml(TaskConfiguration config)
    {
        string userSid = GetCurrentUserSid();

        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        xml.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        xml.AppendLine("  <RegistrationInfo>");
        xml.AppendLine($"    <Author>{SecurityElement.Escape(config.Author!)}</Author>");
        if (!string.IsNullOrWhiteSpace(config.Description))
            xml.AppendLine($"    <Description>{SecurityElement.Escape(config.Description)}</Description>");
        xml.AppendLine($"    <URI>\\{SecurityElement.Escape(config.TaskName)}</URI>");
        xml.AppendLine($"    <Date>{DateTime.Now:yyyy-MM-ddTHH:mm:ss}</Date>");
        xml.AppendLine("  </RegistrationInfo>");
        if (config.LogonTrigger)
        {
            xml.AppendLine("  <Triggers>");
            xml.AppendLine("    <LogonTrigger>");
            xml.AppendLine("      <Enabled>true</Enabled>");
            xml.AppendLine($"      <UserId>{SecurityElement.Escape(GetCurrentUserName())}</UserId>");
            if (config.LogonDelay > TimeSpan.Zero)
                xml.AppendLine($"      <Delay>{XmlConvert.ToString(config.LogonDelay)}</Delay>");
            xml.AppendLine("    </LogonTrigger>");
            xml.AppendLine("  </Triggers>");
        }
        else
        {
            xml.AppendLine("  <Triggers />");
        }
        xml.AppendLine("  <Principals>");
        xml.AppendLine("    <Principal id=\"Author\">");
        xml.AppendLine($"      <UserId>{userSid}</UserId>");
        xml.AppendLine("      <LogonType>InteractiveToken</LogonType>");
        xml.AppendLine($"      <RunLevel>{GetRunLevel(config)}</RunLevel>");
        xml.AppendLine("    </Principal>");
        xml.AppendLine("  </Principals>");
        xml.AppendLine("  <Settings>");
        xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        xml.AppendLine($"    <DisallowStartIfOnBatteries>{config.DisallowStartIfOnBatteries.ToString().ToLower()}</DisallowStartIfOnBatteries>");
        xml.AppendLine($"    <StopIfGoingOnBatteries>{config.StopIfGoingOnBatteries.ToString().ToLower()}</StopIfGoingOnBatteries>");
        xml.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
        xml.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
        xml.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
        xml.AppendLine("    <IdleSettings>");
        xml.AppendLine("      <StopOnIdleEnd>false</StopOnIdleEnd>");
        xml.AppendLine("      <RestartOnIdle>false</RestartOnIdle>");
        xml.AppendLine("    </IdleSettings>");
        xml.AppendLine($"    <AllowStartOnDemand>{config.AllowStartOnDemand.ToString().ToLower()}</AllowStartOnDemand>");
        xml.AppendLine("    <Enabled>true</Enabled>");
        xml.AppendLine($"    <Hidden>{config.Hidden.ToString().ToLower()}</Hidden>");
        xml.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
        xml.AppendLine("    <WakeToRun>false</WakeToRun>");
        xml.AppendLine($"    <ExecutionTimeLimit>{XmlConvert.ToString(config.ExecutionTimeLimit)}</ExecutionTimeLimit>");
        xml.AppendLine($"    <Priority>{(int)config.Priority}</Priority>");
        xml.AppendLine("  </Settings>");
        xml.AppendLine("  <Actions Context=\"Author\">");
        xml.AppendLine("    <Exec>");
        xml.AppendLine($"      <Command>{SecurityElement.Escape(config.ExecutablePath)}</Command>");
        xml.AppendLine($"      <WorkingDirectory>{SecurityElement.Escape(config.WorkingDirectory!)}</WorkingDirectory>");
        xml.AppendLine("    </Exec>");
        xml.AppendLine("  </Actions>");
        xml.AppendLine("</Task>");

        return xml.ToString();
    }

    #endregion

    /// <summary>
    /// 操作结果模型
    /// </summary>
    public class TaskOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public Exception? Exception { get; set; }

        public static TaskOperationResult CreateSuccess(string message = "操作成功", string output = "")
            => new() { Success = true, Message = message, Output = output };

        public static TaskOperationResult CreateFailure(string message, string error = "", Exception? ex = null)
            => new() { Success = false, Message = message, Error = error, Exception = ex };
    }

    /// <summary>
    /// 任务计划配置
    /// </summary>
    public class TaskConfiguration
    {
        public string ExecutablePath { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? WorkingDirectory { get; set; }
        public TaskPriority Priority { get; set; } = TaskPriority.Normal;

        /// <summary>
        ///     以最高权限运行（HighestAvailable），否则为 LeastPrivilege
        /// </summary>
        public bool HighestPrivilege { get; set; }

        /// <summary>
        ///     添加当前用户的登录触发器
        /// </summary>
        public bool LogonTrigger { get; set; }

        /// <summary>
        ///     登录触发后的延迟
        /// </summary>
        public TimeSpan LogonDelay { get; set; } = TimeSpan.Zero;

        /// <summary>
        ///     运行时限，0 表示不限：本程序是常驻程序，不能被任务计划按时限强制结束
        /// </summary>
        public TimeSpan ExecutionTimeLimit { get; set; } = TimeSpan.Zero;
        public bool AllowStartOnDemand { get; set; } = true;
        public bool DisallowStartIfOnBatteries { get; set; } = false;
        public bool StopIfGoingOnBatteries { get; set; } = false;
        public bool Hidden { get; set; } = false;
    }

    public enum TaskPriority
    {
        Critical = 0,
        High = 1,
        Normal = 4,
        Low = 7,
        Idle = 10
    }

    #endregion

    #region Administrators

    public static bool IsUserAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    #endregion
}
