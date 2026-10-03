using System.Text.Json;
using System.Text.Json.Nodes;

namespace STranslate.Plugin.Ocr.DeepSeek;

internal static class DeepSeekOcrProtocol
{
    private const string ChatCompletionsPath = "/chat/completions";

    /// <summary>
    /// 开启思考时使用的推理强度。DeepSeek 实际档位为 low/high/max，
    /// 官方映射表将 medium 归入 high；显式发送 medium 以对应"中等"语义。
    /// </summary>
    internal const string ThinkingReasoningEffort = "medium";

    internal static string BuildFinalUrl(string url) =>
        UrlHelper.BuildFinalUrl(url, ChatCompletionsPath);

    /// <summary>
    /// 构造识别请求：最后一条提示词作为用户文本与图片一起发送，其余提示词原样作为前置消息。
    /// </summary>
    internal static JsonObject CreateRequest(
        string model,
        IReadOnlyList<PromptItem> prompts,
        string imageDataUrl,
        bool thinking)
    {
        if (prompts.Count == 0)
            throw new InvalidOperationException("Prompt配置为空");

        var messages = new JsonArray();
        foreach (var item in prompts.Take(prompts.Count - 1))
            messages.Add(new JsonObject { ["role"] = item.Role, ["content"] = item.Content });

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = prompts[^1].Content },
                new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = imageDataUrl }
                }
            }
        });

        var request = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            // 服务端默认开启思考，关闭时也必须显式声明，否则开关不生效。
            ["thinking"] = new JsonObject { ["type"] = thinking ? "enabled" : "disabled" }
        };

        if (thinking)
            request["reasoning_effort"] = ThinkingReasoningEffort;

        return request;
    }

    /// <summary>
    /// 读取非流式响应正文；思考内容在 reasoning_content 中，不计入识别结果。
    /// </summary>
    internal static string ParseResponse(string response)
    {
        var root = JsonNode.Parse(response) ?? throw new InvalidOperationException($"反序列化失败: {response}");

        var errorMessage = root["error"]?["message"]?.ToString();
        if (!string.IsNullOrWhiteSpace(errorMessage))
            throw new InvalidOperationException(errorMessage);

        var content = root["choices"]?[0]?["message"]?["content"];
        return content?.GetValueKind() == JsonValueKind.String
            ? content.GetValue<string>()
            : string.Empty;
    }
}
