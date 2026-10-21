using Xunit;

namespace STranslate.Tests;

/// <summary>
/// 真实窗口与渲染时序类测试：依赖交互桌面环境（帧推进、DPI、显示器状态），
/// 在非交互会话与 CI 中必然失败且不代表回归，默认跳过；设置 ST_UI_TESTS=1 显式启用。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InteractiveUiFactAttribute : FactAttribute
{
    public InteractiveUiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ST_UI_TESTS") != "1")
            Skip = "交互 UI 测试默认跳过；设置环境变量 ST_UI_TESTS=1 启用";
    }
}

/// <summary>
/// <see cref="InteractiveUiFactAttribute"/> 的 Theory 版本。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InteractiveUiTheoryAttribute : TheoryAttribute
{
    public InteractiveUiTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("ST_UI_TESTS") != "1")
            Skip = "交互 UI 测试默认跳过；设置环境变量 ST_UI_TESTS=1 启用";
    }
}
