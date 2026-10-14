using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;
using System.IO;

namespace STranslate.Plugin.Ocr.RapidOcrBuiltIn;

/// <summary>
/// 管理 RapidOCR 模型的生命周期：首次识别时加载，空闲超时后释放；识别请求串行执行。
/// </summary>
/// <remarks>
/// 参数来自 2026-10 与微信 OCR 的对比测试：
/// RapidOcrNet 的 <see cref="RapidOcrOptions.PPOCRv6"/> 预设会把短边放大到 736 px，小截图约 1 s、峰值内存 2.4 GB；
/// 短边放大上限 384、识别 4 路并行、关闭 ONNX 内存池后，小截图约 230 ms、峰值约 540 MB，准确率不降。
/// 上限低于 384 会漏检小字（两行希腊字母图的小写行），高于 384 变慢且竖排变差。
/// </remarks>
internal sealed class RapidOcrEngine(string modelDirectory) : IDisposable
{
    private static readonly RapidOcrOptions Options = RapidOcrOptions.PPOCRv6 with
    {
        LimitSideLen = 384,
        RecMaxDegreeOfParallelism = 4,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private RapidOcr? _ocr;
    private SessionOptions? _sessionOptions;
    private Timer? _idleTimer;
    private TimeSpan _idleTimeout = TimeSpan.FromMinutes(10);
    private bool _disposed;

    public bool IsLoaded => _ocr != null;

    /// <summary>空闲多久后释放模型；<see cref="TimeSpan.Zero"/> 表示常驻。</summary>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set
        {
            _idleTimeout = value;
            if (_ocr != null) RestartIdleTimer();
        }
    }

    public async Task<List<TextLineBox>> RecognizeAsync(byte[] imageData, CancellationToken cancellationToken)
    {
        // SKBitmap.Decode(byte[]) 遇到无法识别的数据会抛出含糊的 ArgumentNullException，先显式创建解码器
        using var data = SKData.CreateCopy(imageData);
        using var codec = SKCodec.Create(data)
                          ?? throw new InvalidDataException("RapidOCR: unsupported or corrupted image data");
        using var bitmap = SKBitmap.Decode(codec)
                           ?? throw new InvalidDataException("RapidOCR: unsupported or corrupted image data");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            var ocr = _ocr ?? Load();
            var result = await ocr.DetectAsync(bitmap, Options, cancellationToken).ConfigureAwait(false);
            return result.TextBlocks
                .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                .Select(b => new TextLineBox(b.Text, b.BoxPoints.Select(p => ((float)p.X, (float)p.Y)).ToArray(), b.AngleIndex == 1))
                .ToList();
        }
        finally
        {
            if (!_disposed) RestartIdleTimer();
            _gate.Release();
        }
    }

    private RapidOcr Load()
    {
        // 线程数取逻辑核数的一半（约等于物理核数），最多 8：再多收益很小，反而抢占前台程序
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        var sessionOptions = RapidOcr.GetDefaultSessionOptions(threads);
        // 内存池会按出现过的最大输入保留内存，截图尺寸变化大时峰值翻倍，常驻托盘程序更看重内存
        sessionOptions.EnableCpuMemArena = false;

        var models = RapidOcrModelSet.PPOCRv6Small with
        {
            DetModelPath = Path.Combine(modelDirectory, "PP-OCRv6_det_small.onnx"),
            RecModelPath = Path.Combine(modelDirectory, "PP-OCRv6_rec_small.onnx"),
            KeysPath = Path.Combine(modelDirectory, "ppocrv6_dict.txt"),
            ClsModelPath = Path.Combine(modelDirectory, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
        };

        var ocr = new RapidOcr();
        try
        {
            ocr.InitModels(models, sessionOptions);
        }
        catch
        {
            ocr.Dispose();
            sessionOptions.Dispose();
            throw;
        }
        _ocr = ocr;
        _sessionOptions = sessionOptions;
        return ocr;
    }

    private void RestartIdleTimer()
    {
        if (_idleTimeout <= TimeSpan.Zero)
        {
            _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        _idleTimer ??= new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite);
        _idleTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>释放模型；正在识别时跳过，识别结束后会重新计时。</summary>
    private void Unload()
    {
        if (!_gate.Wait(0)) return;
        try
        {
            ReleaseModel();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReleaseModel()
    {
        _ocr?.Dispose();
        _ocr = null;
        _sessionOptions?.Dispose();
        _sessionOptions = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _gate.Wait();
        try
        {
            _disposed = true;
            _idleTimer?.Dispose();
            _idleTimer = null;
            ReleaseModel();
        }
        finally
        {
            _gate.Release();
        }
    }
}
