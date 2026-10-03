using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using STranslate.Plugin;
using STranslate.Plugin.Ocr.DeepSeek;
using STranslate.Plugin.Ocr.DeepSeek.ViewModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit.Abstractions;

namespace STranslate.Tests;

public class DeepSeekOcrTests(ITestOutputHelper output)
{
    private const string LiveKeyVariable = "DEEPSEEK_API_KEY";
    private const string ImageUrl = "data:image/png;base64,AAAA";

    [Theory]
    [InlineData("https://api.deepseek.com/", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/v1", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://example.com/custom#", "https://example.com/custom")]
    public void BuildFinalUrl_AppendsChatCompletions(string url, string expected)
    {
        Assert.Equal(expected, DeepSeekOcrProtocol.BuildFinalUrl(url));
    }

    [Fact]
    public void CreateRequest_SendsImageWithLastPromptAndKeepsEarlierPrompts()
    {
        var json = DeepSeekOcrProtocol.CreateRequest("deepseek-flash",
            [new PromptItem("system", "rules"), new PromptItem("user", "read it")], ImageUrl, thinking: false);

        var messages = json["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]?["role"]?.ToString());
        Assert.Equal("rules", messages[0]?["content"]?.ToString());
        Assert.Equal("user", messages[1]?["role"]?.ToString());
        Assert.Equal("read it", messages[1]?["content"]?[0]?["text"]?.ToString());
        Assert.Equal(ImageUrl, messages[1]?["content"]?[1]?["image_url"]?["url"]?.ToString());
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
        Assert.Null(json["reasoning_effort"]);
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void CreateRequest_ThinkingOn_UsesMediumEffort()
    {
        var json = DeepSeekOcrProtocol.CreateRequest("deepseek-flash", [new PromptItem("user", "read it")], ImageUrl, thinking: true);

        Assert.Single(json["messages"]!.AsArray());
        Assert.Equal("enabled", json["thinking"]?["type"]?.ToString());
        Assert.Equal("medium", json["reasoning_effort"]?.ToString());
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void ParseResponse_ReturnsContentAndIgnoresReasoning()
    {
        var text = DeepSeekOcrProtocol.ParseResponse(
            """{"choices":[{"message":{"role":"assistant","content":"Hello\n世界","reasoning_content":"thinking..."}}]}""");

        Assert.Equal("Hello\n世界", text);
    }

    [Fact]
    public void ParseResponse_ThrowsServiceError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DeepSeekOcrProtocol.ParseResponse(
            """{"error":{"message":"Authentication Fails","type":"authentication_error"}}"""));

        Assert.Equal("Authentication Fails", ex.Message);
    }

    [Fact]
    public void DefaultSettings_PresetFlashModelWithThinkingOff()
    {
        var settings = JsonSerializer.Deserialize<Settings>("{}")!;

        Assert.Equal("deepseek-flash", settings.Model);
        Assert.Equal(["deepseek-flash"], new Settings().Models);
        Assert.False(settings.Thinking);
    }

    [Fact]
    public async Task ThinkingToggle_ChangesRequestAndSplitsRecognizedLines()
    {
        var http = PostRecorder.Create(
            """{"choices":[{"message":{"content":"Hello OCR\n第二行","reasoning_content":"..."}}]}""");
        var (main, viewModel, context) = CreatePlugin(http);

        viewModel.Thinking = true;
        var thinkingResult = await main.RecognizeAsync(new OcrRequest([1, 2, 3], LangEnum.Auto), CancellationToken.None);
        var thinkingRequest = http.Requests[^1];

        viewModel.Thinking = false;
        await main.RecognizeAsync(new OcrRequest([1, 2, 3], LangEnum.Auto), CancellationToken.None);
        var plainRequest = http.Requests[^1];

        Assert.Equal(["Hello OCR", "第二行"], thinkingResult.OcrContents.Select(c => c.Text));
        Assert.Equal("enabled", thinkingRequest["thinking"]?["type"]?.ToString());
        Assert.Equal("medium", thinkingRequest["reasoning_effort"]?.ToString());
        Assert.Equal("disabled", plainRequest["thinking"]?["type"]?.ToString());
        Assert.Null(plainRequest["reasoning_effort"]);
        Assert.Null(plainRequest["temperature"]);
        Assert.StartsWith("data:image/png;base64,", plainRequest["messages"]?.AsArray()[^1]?["content"]?[1]?["image_url"]?["url"]?.ToString());
        Assert.True(context.SaveCount >= 2, "开关切换后应保存插件配置");
        Assert.All(http.Urls, url => Assert.Equal("https://api.deepseek.com/chat/completions", url));
    }

    [Fact]
    public async Task HighImageQuality_IsRejectedBeforeRequest()
    {
        var http = PostRecorder.Create("{}");
        var (main, _, _) = CreatePlugin(http, imageQuality: ImageQuality.High);

        var result = await main.RecognizeAsync(new OcrRequest([1], LangEnum.Auto), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ThinkingToggle_TakesEffectAgainstLiveApi()
    {
        var apiKey = Environment.GetEnvironmentVariable(LiveKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"已跳过：未设置环境变量 {LiveKeyVariable}");
            return;
        }

        var image = RenderTextImage("Hello OCR 2026", "深度求索文本识别");
        var http = PostRecorder.CreateLive();
        var (main, viewModel, _) = CreatePlugin(http, apiKey);
        main.SelectPrompt(main.Prompts.First(p => p.Name == "文本识别"));

        viewModel.Thinking = true;
        var thinking = await main.RecognizeAsync(new OcrRequest(image, LangEnum.Auto), CancellationToken.None);
        var thinkingTokens = http.LastReasoningTokens;

        viewModel.Thinking = false;
        var plain = await main.RecognizeAsync(new OcrRequest(image, LangEnum.Auto), CancellationToken.None);
        var plainTokens = http.LastReasoningTokens;

        output.WriteLine($"开启思考：推理 token {thinkingTokens}，识别 {thinking.Text.Replace('\n', '|')}");
        output.WriteLine($"关闭思考：推理 token {plainTokens}，识别 {plain.Text.Replace('\n', '|')}");
        Assert.Contains("Hello OCR 2026", thinking.Text);
        Assert.Contains("深度求索", plain.Text);
        Assert.True(thinkingTokens > 0, "开启思考时应产生推理 token");
        Assert.Equal(0, plainTokens);
    }

    private static (Main Main, SettingsViewModel ViewModel, FakeOcrContext Context) CreatePlugin(
        PostRecorder http, string apiKey = "test-key", ImageQuality imageQuality = ImageQuality.Medium)
    {
        var settings = new Settings { ApiKey = apiKey };
        var context = new FakeOcrContext(settings, http.Service, imageQuality);
        var main = new Main();
        main.Init(context);
        return (main, new SettingsViewModel(context, settings, main), context);
    }

    /// <summary>
    /// 在 STA 线程用 WPF 渲染含中英文的测试图片。
    /// </summary>
    private static byte[] RenderTextImage(params string[] lines)
    {
        byte[]? png = null;
        var thread = new Thread(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 520, 60 * lines.Length + 20));
                for (var i = 0; i < lines.Length; i++)
                {
                    var text = new FormattedText(lines[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei"), 28, Brushes.Black, 1.0);
                    dc.DrawText(text, new Point(12, 12 + 60 * i));
                }
            }
            var bitmap = new RenderTargetBitmap(520, 60 * lines.Length + 20, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            png = stream.ToArray();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return png!;
    }

    private sealed class FakeOcrContext(Settings settings, IHttpService httpService, ImageQuality imageQuality) : IPluginContext
    {
        public int SaveCount { get; private set; }
        public PluginMetaData MetaData => throw new NotSupportedException();
        public ILogger Logger => NullLogger.Instance;
        public string GetTranslation(string key) => key;
        public IHttpService HttpService => httpService;
        public IAudioPlayer AudioPlayer => throw new NotSupportedException();
        public ISnackbar Snackbar => throw new NotSupportedException();
        public INotification Notification => throw new NotSupportedException();
        public ImageQuality ImageQuality => imageQuality;
        public Window GetPromptEditWindow(System.Collections.ObjectModel.ObservableCollection<Prompt> prompts, List<string>? roles = default) => throw new NotSupportedException();
        public T LoadSettingStorage<T>() where T : new() => settings is T typed ? typed : new T();
        public void SaveSettingStorage<T>() where T : new() => SaveCount++;
        public void ApplyTheme(Window window) { }
        public void Dispose() { }
    }

    /// <summary>
    /// 只实现插件用到的 PostAsync(url, content, options, token)，记录请求体。
    /// </summary>
    public class PostRecorder : DispatchProxy
    {
        private string? _cannedResponse;
        private HttpClient? _client;

        public List<JsonObject> Requests { get; } = [];
        public List<string> Urls { get; } = [];
        public int LastReasoningTokens { get; private set; }
        public IHttpService Service => (IHttpService)(object)this;

        internal static PostRecorder Create(string cannedResponse)
        {
            var recorder = (PostRecorder)(object)Create<IHttpService, PostRecorder>();
            recorder._cannedResponse = cannedResponse;
            return recorder;
        }

        internal static PostRecorder CreateLive()
        {
            var recorder = (PostRecorder)(object)Create<IHttpService, PostRecorder>();
            recorder._client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            return recorder;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IHttpService.PostAsync) || args is not [string url, object content, Options options, CancellationToken token])
                throw new NotSupportedException(targetMethod?.Name);

            var request = JsonNode.Parse(JsonSerializer.Serialize(content))!.AsObject();
            Requests.Add(request);
            Urls.Add(url);
            return _client is null ? Task.FromResult(_cannedResponse!) : SendLiveAsync(url, request, options, token);
        }

        private async Task<string> SendLiveAsync(string url, JsonObject request, Options options, CancellationToken token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json")
            };
            foreach (var (name, value) in options.Headers ?? [])
                message.Headers.TryAddWithoutValidation(name, value);

            using var response = await _client!.SendAsync(message, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"{(int)response.StatusCode}: {body}");

            LastReasoningTokens = JsonNode.Parse(body)?["usage"]?["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? 0;
            return body;
        }
    }
}
