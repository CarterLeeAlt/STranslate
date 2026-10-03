using CommunityToolkit.Mvvm.ComponentModel;
using STranslate.Plugin.Ocr.DeepSeek.View;
using STranslate.Plugin.Ocr.DeepSeek.ViewModel;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace STranslate.Plugin.Ocr.DeepSeek;

public class Main : ObservableObject, IOcrPlugin, ILlm
{
    private const string DefaultModel = "deepseek-flash";

    /// <summary>
    /// 验证用的 1×1 白色 PNG，只确认接口可接收图片输入。
    /// </summary>
    private const string ValidationImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=";

    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public IEnumerable<LangEnum> SupportedLanguages => Enum.GetValues<LangEnum>();

    public ObservableCollection<Prompt> Prompts { get; set; } = [];

    public Prompt? SelectedPrompt
    {
        get => Prompts.FirstOrDefault(p => p.IsEnabled);
        set => SelectPrompt(value);
    }

    public void SelectPrompt(Prompt? prompt)
    {
        if (prompt == null) return;

        // 更新所有 Prompt 的 IsEnabled 状态
        foreach (var p in Prompts)
        {
            p.IsEnabled = p == prompt;
        }

        OnPropertyChanged(nameof(SelectedPrompt));

        // 保存到配置
        Settings.Prompts = [.. Prompts.Select(p => p.Clone())];
        Context.SaveSettingStorage<Settings>();
    }

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings, this);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();

        // 加载 Prompt 列表
        Settings.Prompts.ForEach(Prompts.Add);
    }

    public void Dispose() => _viewModel?.Dispose();

    public string? GetLanguage(LangEnum langEnum) => null;

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        // 按实际格式发送；"高"质量的 BMP 会无损转为 PNG。
        var imageDataUrl = DeepSeekOcrProtocol.BuildImageDataUrl(request.ImageData);

        var text = await SendAsync(imageDataUrl, ConvertLanguage(request.Language), cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return new OcrResult().Fail(Context.GetTranslation("STranslate_Plugin_Ocr_DeepSeek_NoTextOutput"));

        var result = new OcrResult();
        foreach (var line in text.Split("\n"))
            result.OcrContents.Add(new OcrContent { Text = line });

        return result;
    }

    internal Task ValidateApiAsync(CancellationToken cancellationToken = default) =>
        SendAsync($"data:image/png;base64,{ValidationImageBase64}", ConvertLanguage(LangEnum.Auto), cancellationToken);

    private async Task<string> SendAsync(string imageDataUrl, string language, CancellationToken cancellationToken)
    {
        var url = DeepSeekOcrProtocol.BuildFinalUrl(Settings.Url);
        var model = string.IsNullOrWhiteSpace(Settings.Model) ? DefaultModel : Settings.Model.Trim();

        var prompts = (Prompts.FirstOrDefault(x => x.IsEnabled) ?? throw new Exception("请先完善Prompt配置"))
            .Clone()
            .Items
            .ToList();
        foreach (var item in prompts)
            item.Content = item.Content.Replace("$target", language);

        var content = DeepSeekOcrProtocol.CreateRequest(model, prompts, imageDataUrl, Settings.Thinking);
        var option = new Options
        {
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + Settings.ApiKey }
            }
        };

        var response = await Context.HttpService.PostAsync(url, content, option, cancellationToken);
        return DeepSeekOcrProtocol.ParseResponse(response);
    }

    private string ConvertLanguage(LangEnum langEnum) => langEnum switch
    {
        LangEnum.Auto => "Requires you to identify automatically",
        LangEnum.ChineseSimplified => "Simplified Chinese",
        LangEnum.ChineseTraditional => "Traditional Chinese",
        LangEnum.Cantonese => "Cantonese",
        LangEnum.English => "English",
        LangEnum.Japanese => "Japanese",
        LangEnum.Korean => "Korean",
        LangEnum.French => "French",
        LangEnum.Spanish => "Spanish",
        LangEnum.Russian => "Russian",
        LangEnum.German => "German",
        LangEnum.Italian => "Italian",
        LangEnum.Turkish => "Turkish",
        LangEnum.PortuguesePortugal => "Portuguese",
        LangEnum.PortugueseBrazil => "Portuguese",
        LangEnum.Vietnamese => "Vietnamese",
        LangEnum.Indonesian => "Indonesian",
        LangEnum.Thai => "Thai",
        LangEnum.Malay => "Malay",
        LangEnum.Arabic => "Arabic",
        LangEnum.Hindi => "Hindi",
        LangEnum.MongolianCyrillic => "Mongolian",
        LangEnum.MongolianTraditional => "Mongolian",
        LangEnum.Khmer => "Central Khmer",
        LangEnum.NorwegianBokmal => "Norwegian Bokmål",
        LangEnum.NorwegianNynorsk => "Norwegian Nynorsk",
        LangEnum.Persian => "Persian",
        LangEnum.Swedish => "Swedish",
        LangEnum.Polish => "Polish",
        LangEnum.Dutch => "Dutch",
        LangEnum.Ukrainian => "Ukrainian",
        LangEnum.Uzbek => "Uzbek",
        LangEnum.Uyghur => "Uyghur",
        _ => "Requires you to identify automatically"
    };
}
