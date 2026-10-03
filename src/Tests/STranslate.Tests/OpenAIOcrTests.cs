using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using STranslate.Plugin;
using STranslate.Plugin.Ocr.OpenAI;
using System.Windows;
using System.Windows.Media.Imaging;

namespace STranslate.Tests;

public class OpenAIOcrTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    public void Build_PassesSupportedFormatsThrough(byte[] image, string mediaType)
    {
        Assert.Equal($"data:{mediaType};base64,{Convert.ToBase64String(image)}", OcrImageDataUrl.Build(image));
    }

    [Fact]
    public async Task HighImageQuality_BmpIsSentAsLosslessPng()
    {
        // "高"图片质量由 BmpBitmapEncoder 产生 BMP，OpenAI 视觉输入不接受 BMP，需转为 PNG。
        var bmp = DeepSeekOcrTests.RenderTextImage(() => new BmpBitmapEncoder(), "BMP");
        var http = DeepSeekOcrTests.PostRecorder.Create("""{"choices":[{"message":{"content":"BMP"}}]}""");
        var main = new Main();
        main.Init(new FakeOcrContext(new Settings { ApiKey = "test-key" }, http.Service, ImageQuality.High));

        var result = await main.RecognizeAsync(new OcrRequest(bmp, LangEnum.Auto), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("BMP", result.Text);
        var url = http.Requests[^1]["messages"]!.AsArray()[^1]?["content"]?[1]?["image_url"]?["url"]?.ToString();
        Assert.StartsWith("data:image/png;base64,", url);
        Assert.Equal(PngHeader, Convert.FromBase64String(url!["data:image/png;base64,".Length..])[..8]);
    }

    private sealed class FakeOcrContext(Settings settings, IHttpService httpService, ImageQuality imageQuality) : IPluginContext
    {
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
        public void SaveSettingStorage<T>() where T : new() { }
        public void ApplyTheme(Window window) { }
        public void Dispose() { }
    }
}
