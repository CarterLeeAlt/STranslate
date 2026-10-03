namespace STranslate.Plugin.Translate.DeepSeek;

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Url { get; set; } = "https://api.deepseek.com/";
    public string Model { get; set; } = "deepseek-flash";
    public List<string> Models { get; set; } =
    [
        "deepseek-flash",
    ];
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// 思考模式；DeepSeek 服务端默认开启，因此关闭时也必须显式发送 disabled。
    /// </summary>
    public bool Thinking { get; set; } = false;

    public List<Prompt> Prompts { get; set; } =
    [
        new("翻译",
        [
            new PromptItem("system", "你是一位精通源文本语言与目标语言及其文化的翻译专家。"),
            new PromptItem("user", """"
                源文本：
                """
                $content
                """

                ## 翻译要求
                1. 忠实于源文本，确保每个句子都得到准确、流畅的翻译。
                2. 不得遗漏源文本的任何内容或细节。
                3. 准确翻译大额数字，并符合目标语言的表达习惯。

                ## 任务
                1. 仔细分析并深入理解源文本的内容、上下文、语境、情感，以及与目标语言之间的文化细微差异。
                2. 根据上述翻译要求，将源文本从 $source 准确翻译为 $target。
                3. 确保译文对目标受众来说准确、自然、流畅；必要时调整表达方式，以符合目标语言的文化和语言习惯。

                注意：不要输出任何额外内容，只能输出译文。这一点非常关键。
                """"),
        ], true),
        new("润色",
        [
            new PromptItem("system", "You are a professional, authentic text polishing engine. You only return the polished text, without any explanations."),
            new PromptItem("user", "Please polish the following text in $source (avoid explaining the original text):\r\n\r\n$content"),
        ]),
        new("总结",
        [
            new PromptItem("system", "You are a professional, authentic text summarization engine. You only return the summarized text, without any explanations."),
            new PromptItem("user", "Please summarize the following text in $source (avoid explaining the original text):\r\n\r\n$content"),
        ]),
    ];
}
