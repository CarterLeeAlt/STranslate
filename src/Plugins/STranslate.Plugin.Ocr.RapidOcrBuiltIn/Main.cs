using STranslate.Plugin.Ocr.RapidOcrBuiltIn.View;
using STranslate.Plugin.Ocr.RapidOcrBuiltIn.ViewModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;

namespace STranslate.Plugin.Ocr.RapidOcrBuiltIn;

public class Main : IOcrPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private RapidOcrEngine? _engine;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    /// <summary>
    /// PP-OCRv6 单一多语言模型覆盖的语言（中、繁、日与拉丁字母语言），识别时不需要区分语言。
    /// 越南文不列入：模型字典没有带声调的越南字母。
    /// </summary>
    public IEnumerable<LangEnum> SupportedLanguages =>
    [
        LangEnum.Auto,
        LangEnum.ChineseSimplified,
        LangEnum.ChineseTraditional,
        LangEnum.Cantonese,
        LangEnum.English,
        LangEnum.Japanese,
        LangEnum.French,
        LangEnum.Spanish,
        LangEnum.German,
        LangEnum.Italian,
        LangEnum.Turkish,
        LangEnum.PortuguesePortugal,
        LangEnum.PortugueseBrazil,
        LangEnum.Indonesian,
        LangEnum.Malay,
        LangEnum.NorwegianBokmal,
        LangEnum.NorwegianNynorsk,
        LangEnum.Swedish,
        LangEnum.Polish,
        LangEnum.Dutch,
        LangEnum.Uzbek,
    ];

    public bool SupportBoxPoints() => true;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, ApplyIdleTimeout);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();

        var pluginDirectory = Path.GetDirectoryName(typeof(Main).Assembly.Location) ?? AppContext.BaseDirectory;
        _engine = new RapidOcrEngine(Path.Combine(pluginDirectory, "models"));
        ApplyIdleTimeout();
    }

    private void ApplyIdleTimeout()
    {
        if (_engine != null)
            _engine.IdleTimeout = TimeSpan.FromMinutes(Math.Max(0, Settings.IdleUnloadMinutes));
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }

    public string? GetLanguage(LangEnum langEnum) => null;

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var engine = _engine ?? throw new InvalidOperationException("RapidOCR plugin is not initialized");
        var stopwatch = Stopwatch.StartNew();
        var lines = await engine.RecognizeAsync(request.ImageData, cancellationToken);

        var result = new OcrResult();
        foreach (var line in ReadingOrder.Sort(lines))
        {
            var content = new OcrContent { Text = line.Text };
            foreach (var (x, y) in line.Points)
                content.BoxPoints.Add(new BoxPoint(x, y));
            result.OcrContents.Add(content);
        }
        result.Duration = stopwatch.Elapsed;

        if (_viewModel != null)
            _viewModel.UseCount++;
        else
        {
            Settings.UseCount++;
            Context.SaveSettingStorage<Settings>();
        }
        return result;
    }
}
