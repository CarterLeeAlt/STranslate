using System.Text.Json;
using System.Text.Json.Nodes;

namespace STranslate.Plugin.Translate.DeepSeek;

internal static class DeepSeekProtocol
{
    private const string ChatCompletionsPath = "/chat/completions";

    /// <summary>
    /// 开启思考时使用的推理强度。DeepSeek 实际档位为 low/high/max，
    /// 官方映射表将 medium 归入 high；显式发送 medium 以对应"中等"语义。
    /// </summary>
    internal const string ThinkingReasoningEffort = "medium";

    internal static string BuildFinalUrl(string url) =>
        UrlHelper.BuildFinalUrl(url, ChatCompletionsPath);

    internal static JsonObject CreateRequest(
        string model,
        IReadOnlyCollection<PromptItem> messages,
        double temperature,
        bool thinking)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["messages"] = JsonSerializer.SerializeToNode(messages),
            ["stream"] = true,
            // 服务端默认开启思考，关闭时也必须显式声明，否则开关不生效。
            ["thinking"] = new JsonObject { ["type"] = thinking ? "enabled" : "disabled" }
        };

        if (thinking)
            request["reasoning_effort"] = ThinkingReasoningEffort;
        else
            // 思考模式不支持 temperature，仅在关闭思考时发送。
            request["temperature"] = temperature;

        return request;
    }

    /// <summary>
    /// 解析一行 SSE。思考阶段只返回 reasoning_content，正文在 content 中，这里只取正文。
    /// </summary>
    internal static DeepSeekStreamEvent ParseStreamLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return default;

        var payload = line.StartsWith("data:", StringComparison.Ordinal)
            ? line["data:".Length..].Trim()
            : line.Trim();

        if (payload.Length == 0 || payload.Equals("[DONE]", StringComparison.Ordinal) || !payload.StartsWith('{'))
            return default;

        JsonNode? parsedData;
        try
        {
            parsedData = JsonNode.Parse(payload);
        }
        catch
        {
            // SSE 中可能混入非 JSON 的状态行（如 keep-alive 注释）。
            return default;
        }

        if (parsedData is null)
            return default;

        var errorMessage = parsedData["error"]?["message"]?.ToString();
        if (!string.IsNullOrWhiteSpace(errorMessage))
            return new DeepSeekStreamEvent(null, errorMessage);

        var textDelta = parsedData["choices"] is JsonArray { Count: > 0 } choices
            ? choices[0]?["delta"]?["content"]?.GetValueKind() == JsonValueKind.String
                ? choices[0]!["delta"]!["content"]!.GetValue<string>()
                : null
            : null;

        return string.IsNullOrEmpty(textDelta)
            ? default
            : new DeepSeekStreamEvent(textDelta, null);
    }
}

internal readonly record struct DeepSeekStreamEvent(string? TextDelta, string? ErrorMessage);
