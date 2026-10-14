using STranslate.Plugin.Ocr.RapidOcrBuiltIn;

namespace STranslate.Tests;

/// <summary>
/// 框坐标按 RapidOcrNet 的输出约定构造：四个角点从图片左上角起顺时针；
/// 竖长框（高 ≥ 1.5 倍宽）未翻转时由下往上读，翻转后由上往下读。
/// </summary>
public class RapidOcrReadingOrderTests
{
    private static TextLineBox Box(string text, float left, float top, float right, float bottom, bool flipped = false) =>
        new(text, [(left, top), (right, top), (right, bottom), (left, bottom)], flipped);

    private static string[] Texts(IEnumerable<TextLineBox> lines) => lines.Select(l => l.Text).ToArray();

    [Fact]
    public void HorizontalLines_AreSortedTopToBottom()
    {
        var lines = new[]
        {
            Box("第三行", 10, 60, 400, 76),
            Box("第一行", 10, 12, 400, 28),
            Box("第二行", 10, 36, 400, 52),
        };

        Assert.Equal(["第一行", "第二行", "第三行"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void UpsideDownLines_StartFromTheBottomLine()
    {
        // 整体倒置 180°：第一行在图片最下方，方向分类器判定翻转
        var lines = new[]
        {
            Box("第二行", 12, 12, 300, 30, flipped: true),
            Box("第一行", 12, 35, 528, 55, flipped: true),
        };

        Assert.Equal(["第一行", "第二行"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void ClockwiseRotatedLines_StartFromTheRightColumn()
    {
        // 顺时针旋转 90°：文字由上往下读，第一行在最右边
        var lines = new[]
        {
            Box("第二行", 10, 11, 32, 184, flipped: true),
            Box("第一行", 33, 11, 58, 527, flipped: true),
        };

        Assert.Equal(["第一行", "第二行"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void CounterClockwiseRotatedLines_StartFromTheLeftColumn()
    {
        // 逆时针旋转 90°：文字由下往上读，第一行在最左边
        var lines = new[]
        {
            Box("第二行", 33, 358, 57, 526),
            Box("第一行", 9, 10, 33, 526),
        };

        Assert.Equal(["第一行", "第二行"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void VerticalColumns_AreReadRightToLeft()
    {
        var lines = new[]
        {
            Box("第三列", 10, 11, 34, 83, flipped: true),
            Box("第二列", 38, 11, 64, 302, flipped: true),
            Box("第一列", 68, 13, 93, 302, flipped: true),
        };

        Assert.Equal(["第一列", "第二列", "第三列"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void TwoTextColumns_AreReadColumnByColumn()
    {
        // 双栏正文：两栏行高一致、逐行对齐，仍应先读完左栏
        var lines = new[]
        {
            Box("左1", 18, 18, 420, 36), Box("右1", 450, 18, 900, 36),
            Box("左2", 18, 40, 420, 58), Box("右2", 450, 40, 900, 58),
            Box("左3", 18, 62, 300, 80), Box("右3", 450, 62, 700, 80),
        };

        Assert.Equal(["左1", "左2", "左3", "右1", "右2", "右3"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void MenuWithShortcuts_IsReadRowByRow()
    {
        // 菜单项与快捷键之间空白很宽，但都是短文本，不视为分栏
        var lines = new[]
        {
            Box("新建窗口", 16, 8, 70, 24), Box("Ctrl+N", 190, 8, 236, 24),
            Box("打开文件", 16, 32, 70, 48), Box("Ctrl+O", 190, 32, 236, 48),
            Box("退出", 16, 56, 44, 72),
        };

        Assert.Equal(["新建窗口", "Ctrl+N", "打开文件", "Ctrl+O", "退出"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void HeaderAboveColumns_IsReadFirst()
    {
        var lines = new[]
        {
            Box("左1", 18, 80, 420, 98), Box("右1", 450, 80, 900, 98),
            Box("左2", 18, 102, 420, 120), Box("右2", 450, 102, 900, 120),
            Box("标题", 300, 20, 600, 50),
        };

        Assert.Equal(["标题", "左1", "左2", "右1", "右2"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void SideLabel_DoesNotSkewParagraphOrder()
    {
        // 横排正文左侧有一个逆时针旋转的竖向标签；少数框的方向不能把整体阅读方向拉偏
        var lines = new[]
        {
            Box("侧边栏标签", 11, 10, 32, 91),
            Box("快，使用体验越好。", 48, 79, 167, 94),
            Box("屏幕取词翻译需要先识别图片中的文字，再把结果交给翻译服务。识别速度越", 48, 56, 525, 72),
        };

        Assert.Equal(
            ["侧边栏标签", "屏幕取词翻译需要先识别图片中的文字，再把结果交给翻译服务。识别速度越", "快，使用体验越好。"],
            Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void TiltedLines_KeepReadingOrder()
    {
        // 倾斜 30°：第二行整体在第一行右上方的投影区间内，按旋转后的坐标仍应排在后面
        var lines = new[]
        {
            new TextLineBox("第二行", [(18, 27), (168, 114), (157, 133), (7, 46)], false),
            new TextLineBox("第一行", [(32, 6), (481, 265), (470, 286), (19, 27)], false),
        };

        Assert.Equal(["第一行", "第二行"], Texts(ReadingOrder.Sort(lines)));
    }

    [Fact]
    public void FragmentsOnTheSameLine_AreReadLeftToRight()
    {
        // 粗体、斜体等样式变化会把一行拆成多个框
        var lines = new[]
        {
            Box("例如访问项目主页。", 200, 10, 380, 30),
            Box("支持粗体", 10, 11, 80, 29),
            Box("斜体和链接，", 90, 10, 190, 30),
            Box("Bold, italic and links.", 10, 36, 220, 54),
        };

        Assert.Equal(["支持粗体", "斜体和链接，", "例如访问项目主页。", "Bold, italic and links."], Texts(ReadingOrder.Sort(lines)));
    }

    [Theory]
    [InlineData(false, false, 1, 0)]   // 横排
    [InlineData(false, true, -1, 0)]   // 横排倒置
    [InlineData(true, false, 0, -1)]   // 竖长框，由下往上
    [InlineData(true, true, 0, 1)]     // 竖长框，由上往下
    public void ReadingDirection_FollowsRapidOcrCropConvention(bool tall, bool flipped, double expectedX, double expectedY)
    {
        var box = tall ? Box("x", 0, 0, 20, 200, flipped) : Box("x", 0, 0, 200, 20, flipped);

        var (x, y) = ReadingOrder.ReadingDirection(box);

        Assert.Equal(expectedX, x, 6);
        Assert.Equal(expectedY, y, 6);
    }

    [Fact]
    public void EmptyAndSingleInput_AreReturnedAsIs()
    {
        Assert.Empty(ReadingOrder.Sort([]));
        Assert.Equal(["只有一行"], Texts(ReadingOrder.Sort([Box("只有一行", 0, 0, 100, 20)])));
    }
}
