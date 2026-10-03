using STranslate.Plugin;
using STranslate.Plugin.Translate.DeepSeek;
using System.Text.Json;

namespace STranslate.Tests;

public class DeepSeekProtocolTests
{
    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/v1", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://example.com/custom", "https://example.com/custom")]
    [InlineData("https://example.com/custom#", "https://example.com/custom")]
    public void BuildFinalUrl_AppendsChatCompletionsAndPreservesCustomPaths(string url, string expected)
    {
        Assert.Equal(expected, DeepSeekProtocol.BuildFinalUrl(url));
    }

    [Fact]
    public void CreateRequest_ThinkingOff_ExplicitlyDisablesAndKeepsTemperature()
    {
        var json = DeepSeekProtocol.CreateRequest("deepseek-flash", CreateMessages(), 0.7, thinking: false);

        Assert.Equal("deepseek-flash", json["model"]?.ToString());
        Assert.True(json["stream"]?.GetValue<bool>());
        Assert.Equal("system", json["messages"]?[0]?["role"]?.ToString());
        // 服务端默认开启思考，关闭必须显式发送。
        Assert.Equal("disabled", json["thinking"]?["type"]?.ToString());
        Assert.Equal(0.7, json["temperature"]?.GetValue<double>());
        Assert.Null(json["reasoning_effort"]);
    }

    [Fact]
    public void CreateRequest_ThinkingOn_UsesMediumEffortWithoutTemperature()
    {
        var json = DeepSeekProtocol.CreateRequest("deepseek-flash", CreateMessages(), 0.7, thinking: true);

        Assert.Equal("enabled", json["thinking"]?["type"]?.ToString());
        Assert.Equal("medium", json["reasoning_effort"]?.ToString());
        Assert.Null(json["temperature"]);
    }

    [Fact]
    public void CreateRequest_SerializesToSnakeCaseFieldsUnchanged()
    {
        // HttpService 以驼峰策略序列化请求体，JsonObject 的键不受命名策略影响。
        var json = DeepSeekProtocol.CreateRequest("m", CreateMessages(), 0.5, thinking: true);
        var text = JsonSerializer.Serialize(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"reasoning_effort\":\"medium\"", text);
        Assert.Contains("\"thinking\":{\"type\":\"enabled\"}", text);
    }

    [Theory]
    [InlineData("""data: {"choices":[{"index":0,"delta":{"content":null,"reasoning_content":"We"}}]}""")]
    [InlineData("""data: {"choices":[{"index":0,"delta":{"role":"assistant","content":null,"reasoning_content":""}}]}""")]
    [InlineData("data: [DONE]")]
    [InlineData(": keep-alive")]
    [InlineData("")]
    public void ParseStreamLine_IgnoresReasoningAndNonContentLines(string line)
    {
        var streamEvent = DeepSeekProtocol.ParseStreamLine(line);

        Assert.Null(streamEvent.TextDelta);
        Assert.Null(streamEvent.ErrorMessage);
    }

    [Fact]
    public void ParseStreamLine_ReturnsContentDelta()
    {
        var streamEvent = DeepSeekProtocol.ParseStreamLine(
            """data: {"choices":[{"index":0,"delta":{"content":"早上","reasoning_content":null}}]}""");

        Assert.Equal("早上", streamEvent.TextDelta);
    }

    [Fact]
    public void ParseStreamLine_ReturnsServiceError()
    {
        var streamEvent = DeepSeekProtocol.ParseStreamLine(
            """{"error":{"message":"Authentication Fails","type":"authentication_error"}}""");

        Assert.Equal("Authentication Fails", streamEvent.ErrorMessage);
    }

    [Fact]
    public void LegacySettings_DefaultToThinkingOffAndFlashModel()
    {
        var settings = JsonSerializer.Deserialize<Settings>("{}");

        Assert.NotNull(settings);
        Assert.False(settings.Thinking);
        Assert.Equal("deepseek-flash", settings.Model);
        Assert.Equal("https://api.deepseek.com/", settings.Url);
    }

    private static List<PromptItem> CreateMessages() =>
    [
        new("system", "Translate."),
        new("user", "Hello world")
    ];
}
