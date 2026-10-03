using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using STranslate.Plugin;
using STranslate.Plugin.Translate.DeepSeek;
using STranslate.Plugin.Translate.DeepSeek.ViewModel;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;

namespace STranslate.Tests;

/// <summary>
/// 经设置页 ViewModel 拨动思考开关，再走插件真实翻译路径，验证开关作用到实际请求。
/// </summary>
public class DeepSeekPluginTests(ITestOutputHelper output)
{
    private const string LiveKeyVariable = "DEEPSEEK_API_KEY";

    [Fact]
    public async Task ThinkingToggle_ChangesRequestAndKeepsReasoningOutOfTranslation()
    {
        var http = HttpRecorder.Create(_ =>
        [
            """data: {"choices":[{"index":0,"delta":{"role":"assistant","content":null,"reasoning_content":""}}]}""",
            """data: {"choices":[{"index":0,"delta":{"content":null,"reasoning_content":"The user wants"}}]}""",
            """data: {"choices":[{"index":0,"delta":{"content":"你好","reasoning_content":null}}]}""",
            """data: {"choices":[{"index":0,"delta":{"content":"，世界","reasoning_content":null}}]}""",
            "data: [DONE]"
        ]);
        var (main, viewModel, context) = CreatePlugin(http);

        viewModel.Thinking = true;
        var thinkingResult = await TranslateAsync(main);
        var thinkingRequest = http.Requests[^1];

        viewModel.Thinking = false;
        var plainResult = await TranslateAsync(main);
        var plainRequest = http.Requests[^1];

        Assert.Equal("你好，世界", thinkingResult.Text);
        Assert.Equal("你好，世界", plainResult.Text);
        Assert.Equal("enabled", thinkingRequest["thinking"]?["type"]?.ToString());
        Assert.Equal("medium", thinkingRequest["reasoning_effort"]?.ToString());
        Assert.Null(thinkingRequest["temperature"]);
        Assert.Equal("disabled", plainRequest["thinking"]?["type"]?.ToString());
        Assert.Null(plainRequest["reasoning_effort"]);
        Assert.Null(plainRequest["temperature"]);
        Assert.True(context.SaveCount >= 2, "开关切换后应保存插件配置");
        Assert.All(http.Urls, url => Assert.Equal("https://api.deepseek.com/chat/completions", url));
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

        var http = HttpRecorder.CreateLive();
        var (main, viewModel, _) = CreatePlugin(http, apiKey);

        viewModel.Thinking = true;
        var thinkingResult = await TranslateAsync(main);
        var thinkingReasoning = http.CountReasoningChunks();

        http.ResponseLines.Clear();
        viewModel.Thinking = false;
        var plainResult = await TranslateAsync(main);
        var plainReasoning = http.CountReasoningChunks();

        output.WriteLine($"开启思考：推理片段 {thinkingReasoning}，译文 {thinkingResult.Text}");
        output.WriteLine($"关闭思考：推理片段 {plainReasoning}，译文 {plainResult.Text}");
        Assert.False(string.IsNullOrWhiteSpace(thinkingResult.Text));
        Assert.False(string.IsNullOrWhiteSpace(plainResult.Text));
        Assert.True(thinkingReasoning > 0, "开启思考时服务端应返回推理内容");
        Assert.Equal(0, plainReasoning);
    }

    private static async Task<TranslateResult> TranslateAsync(Main main)
    {
        var result = new TranslateResult();
        await main.TranslateAsync(new TranslateRequest("Good morning, world.", LangEnum.English, LangEnum.ChineseSimplified), result);
        return result;
    }

    private static (Main Main, SettingsViewModel ViewModel, FakePluginContext Context) CreatePlugin(HttpRecorder http, string apiKey = "test-key")
    {
        var settings = new Settings { ApiKey = apiKey };
        var context = new FakePluginContext(settings, http.Service);
        var main = new Main();
        main.Init(context);
        return (main, new SettingsViewModel(context, settings, main), context);
    }

    private sealed class FakePluginContext(Settings settings, IHttpService httpService) : IPluginContext
    {
        public int SaveCount { get; private set; }
        public PluginMetaData MetaData => throw new NotSupportedException();
        public ILogger Logger => NullLogger.Instance;
        public string GetTranslation(string key) => key;
        public IHttpService HttpService => httpService;
        public IAudioPlayer AudioPlayer => throw new NotSupportedException();
        public ISnackbar Snackbar => throw new NotSupportedException();
        public INotification Notification => throw new NotSupportedException();
        public ImageQuality ImageQuality => default;
        public System.Windows.Window GetPromptEditWindow(System.Collections.ObjectModel.ObservableCollection<Prompt> prompts, List<string>? roles = default) => throw new NotSupportedException();
        public T LoadSettingStorage<T>() where T : new() => settings is T typed ? typed : new T();
        public void SaveSettingStorage<T>() where T : new() => SaveCount++;
        public void ApplyTheme(System.Windows.Window window) { }
        public void Dispose() { }
    }

    /// <summary>
    /// 只实现插件用到的 StreamPostAsyncEnumerable，记录请求体与原始 SSE 行。
    /// </summary>
    public class HttpRecorder : DispatchProxy
    {
        private Func<JsonObject, IReadOnlyList<string>>? _cannedResponse;
        private HttpClient? _client;

        public List<JsonObject> Requests { get; } = [];
        public List<string> Urls { get; } = [];
        public List<string> ResponseLines { get; } = [];
        public IHttpService Service => (IHttpService)(object)this;

        internal static HttpRecorder Create(Func<JsonObject, IReadOnlyList<string>> cannedResponse)
        {
            var recorder = (HttpRecorder)(object)Create<IHttpService, HttpRecorder>();
            recorder._cannedResponse = cannedResponse;
            return recorder;
        }

        internal static HttpRecorder CreateLive()
        {
            var recorder = (HttpRecorder)(object)Create<IHttpService, HttpRecorder>();
            recorder._client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            return recorder;
        }

        internal int CountReasoningChunks() => ResponseLines.Count(line =>
        {
            var payload = line.StartsWith("data:", StringComparison.Ordinal) ? line[5..].Trim() : line;
            if (!payload.StartsWith('{')) return false;
            var reasoning = JsonNode.Parse(payload)?["choices"]?[0]?["delta"]?["reasoning_content"];
            return reasoning?.GetValueKind() == JsonValueKind.String && reasoning.GetValue<string>().Length > 0;
        });

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IHttpService.StreamPostAsyncEnumerable) || args is not [string url, object content, Options options, CancellationToken token])
                throw new NotSupportedException(targetMethod?.Name);

            var request = JsonNode.Parse(JsonSerializer.Serialize(content))!.AsObject();
            Requests.Add(request);
            Urls.Add(url);
            return _client is null
                ? Replay(_cannedResponse!(request))
                : SendLiveAsync(url, request, options, token);
        }

        private async IAsyncEnumerable<string> Replay(IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                ResponseLines.Add(line);
                yield return line;
            }
            await Task.CompletedTask;
        }

        private async IAsyncEnumerable<string> SendLiveAsync(string url, JsonObject request, Options options, [EnumeratorCancellation] CancellationToken token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json")
            };
            foreach (var (name, value) in options.Headers ?? [])
                message.Headers.TryAddWithoutValidation(name, value);

            using var response = await _client!.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
            var body = response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(token);
            if (body is not null)
                throw new HttpRequestException($"{(int)response.StatusCode}: {body}");

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
            while (await reader.ReadLineAsync(token) is { } line)
            {
                ResponseLines.Add(line);
                yield return line;
            }
        }
    }
}
