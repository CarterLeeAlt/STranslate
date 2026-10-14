namespace STranslate.Plugin.Ocr.RapidOcrBuiltIn;

public class Settings
{
    /// <summary>空闲多少分钟后释放模型以回收内存；0 表示模型常驻。</summary>
    public int IdleUnloadMinutes { get; set; } = 10;

    public long UseCount { get; set; } = 0;
}
