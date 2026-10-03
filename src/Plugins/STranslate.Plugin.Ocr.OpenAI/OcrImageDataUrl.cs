using System.IO;
using System.Windows.Media.Imaging;

namespace STranslate.Plugin.Ocr.OpenAI;

internal static class OcrImageDataUrl
{
    /// <summary>
    /// 按文件头生成图片 data URL。OpenAI 视觉输入只接受 png/jpeg/webp/gif，
    /// "高"图片质量产生的 BMP 会被拒绝，因此无损转为 PNG 后发送。
    /// </summary>
    internal static string Build(byte[] imageData)
    {
        var (mediaType, data) = imageData switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => ("image/png", imageData),
            [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", imageData),
            [0x47, 0x49, 0x46, 0x38, ..] => ("image/gif", imageData),
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ("image/webp", imageData),
            _ => ("image/png", ConvertToPng(imageData))
        };
        return $"data:{mediaType};base64,{Convert.ToBase64String(data)}";
    }

    private static byte[] ConvertToPng(byte[] imageData)
    {
        using var input = new MemoryStream(imageData);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
