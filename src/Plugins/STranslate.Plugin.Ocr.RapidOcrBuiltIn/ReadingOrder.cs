namespace STranslate.Plugin.Ocr.RapidOcrBuiltIn;

/// <summary>
/// RapidOCR 识别出的一行文本。<see cref="Points"/> 为图片像素坐标的四个角点，
/// 按 RapidOcrNet 约定从左上角起顺时针排列（与文字方向无关）。
/// </summary>
/// <param name="Text">识别文本。</param>
/// <param name="Points">四个角点 (x, y)。</param>
/// <param name="Flipped">方向分类器判定裁剪图需要旋转 180°（RapidOcrNet 的 AngleIndex == 1）。</param>
public sealed record TextLineBox(string Text, (float X, float Y)[] Points, bool Flipped);

/// <summary>
/// 把 RapidOCR 按纵坐标输出的文本行整理成阅读顺序。
/// <para>
/// RapidOCR 没有版面分析：倒置、旋转 90°/270° 与竖排时行序会反，双栏正文与整页截图会逐行交错。
/// 这里先由每个框的几何方向与方向分类结果求出阅读方向 u，取行推进方向 v = u 顺时针转 90°
/// （横排向下、竖排向左，因此竖排自然是从右到左），把所有框换到 (u, v) 坐标系后做递归 XY 切分：
/// 能切出“正文栏”时按栏输出，否则按行输出（菜单、表格保持逐行读）。
/// </para>
/// </summary>
public static class ReadingOrder
{
    /// <summary>与 RapidOcrNet 一致：裁剪图高宽比达到此值时先旋转 90° 再识别。</summary>
    private const float VerticalCropRatio = 1.5f;

    /// <summary>正文栏判定：栏内行框宽度中位数至少为行高中位数的这么多倍。</summary>
    private const double TextColumnWidthInLineHeights = 6;

    /// <summary>栏间空白至少为行高中位数的这么多倍才视为分栏。</summary>
    private const double ColumnGapInLineHeights = 1.0;

    /// <summary>判断行间空白时只取行框中间这一比例的高度，避免外扩后的相邻行框互相重叠。</summary>
    private const double RowCoreRatio = 0.6;

    public static List<TextLineBox> Sort(IReadOnlyList<TextLineBox> lines)
    {
        if (lines.Count <= 1) return [.. lines];

        var (ux, uy) = DominantDirection(lines);
        // v = u 顺时针旋转 90°（图片坐标 y 轴向下）
        double vx = -uy, vy = ux;

        var items = lines.Select((l, i) => Project(l, i, ux, uy, vx, vy)).ToList();
        var lineHeight = Median(items.Select(it => it.Height));
        var ordered = new List<Item>(items.Count);
        OrderRegion(items, Math.Max(lineHeight, 1), ordered);
        return ordered.Select(it => lines[it.Index]).ToList();
    }

    /// <summary>单个框的阅读方向（单位向量）。</summary>
    public static (double X, double Y) ReadingDirection(TextLineBox line)
    {
        var p = line.Points;
        double e01x = p[1].X - p[0].X, e01y = p[1].Y - p[0].Y;
        double e12x = p[2].X - p[1].X, e12y = p[2].Y - p[1].Y;
        double width = Math.Sqrt(e01x * e01x + e01y * e01y);
        double height = Math.Sqrt(e12x * e12x + e12y * e12y);

        double dx, dy;
        if (height >= VerticalCropRatio * width)
        {
            // 竖长框：RapidOcrNet 把裁剪图逆时针转 90° 后识别，未翻转时文字由下往上读
            dx = -e12x; dy = -e12y;
        }
        else
        {
            dx = e01x; dy = e01y;
        }
        if (line.Flipped) { dx = -dx; dy = -dy; }

        var len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-6 ? (1, 0) : (dx / len, dy / len);
    }

    /// <summary>
    /// 主阅读方向：先按文字长度加权投票选出多数方向，再只对与它相差 30° 以内的框求平均，
    /// 避免少数竖排标签或方向分类出错的框把整体方向拉偏。
    /// </summary>
    private static (double X, double Y) DominantDirection(IReadOnlyList<TextLineBox> lines)
    {
        const double sameDirectionCos = 0.866; // cos 30°
        var dirs = lines.Select(l => (Dir: ReadingDirection(l), Weight: (double)Math.Max(1, l.Text.Length))).ToList();

        (double X, double Y) best = dirs[0].Dir;
        var bestScore = double.MinValue;
        foreach (var (candidate, _) in dirs)
        {
            var score = dirs.Where(d => Dot(d.Dir, candidate) > sameDirectionCos).Sum(d => d.Weight);
            if (score > bestScore) { bestScore = score; best = candidate; }
        }

        double sx = 0, sy = 0;
        foreach (var (dir, weight) in dirs.Where(d => Dot(d.Dir, best) > sameDirectionCos))
        {
            sx += dir.X * weight;
            sy += dir.Y * weight;
        }
        var len = Math.Sqrt(sx * sx + sy * sy);
        return len < 1e-6 ? best : (sx / len, sy / len);
    }

    private static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;

    private static Item Project(TextLineBox line, int index, double ux, double uy, double vx, double vy)
    {
        double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
        foreach (var (x, y) in line.Points)
        {
            var u = x * ux + y * uy;
            var v = x * vx + y * vy;
            minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
        }
        return new Item(index, minU, maxU, minV, maxV);
    }

    private static void OrderRegion(List<Item> items, double lineHeight, List<Item> output)
    {
        if (items.Count <= 1)
        {
            output.AddRange(items);
            return;
        }

        // 1. 分栏：沿阅读方向有足够宽的空白，且至少一栏像正文
        var columns = SplitByGaps(items, it => it.MinU, it => it.MaxU, ColumnGapInLineHeights * lineHeight, splitAtWidestOnly: false);
        if (columns.Count > 1 && columns.Any(c => IsTextColumn(c, lineHeight)))
        {
            foreach (var column in columns) OrderRegion(column, lineHeight, output);
            return;
        }

        // 2. 分行块：在行推进方向上找最宽的空白切一刀，递归处理（标题栏与下方多栏内容先分开）
        var bands = SplitByGaps(items, it => it.CoreMinV, it => it.CoreMaxV, 0, splitAtWidestOnly: true);
        if (bands.Count > 1)
        {
            foreach (var band in bands) OrderRegion(band, lineHeight, output);
            return;
        }

        // 3. 无法再切：按行聚类后逐行从前往后
        foreach (var row in GroupRows(items, lineHeight))
            output.AddRange(row.OrderBy(it => it.MinU));
    }

    private static bool IsTextColumn(List<Item> column, double lineHeight) =>
        column.Count >= 2 && Median(column.Select(it => it.MaxU - it.MinU)) >= TextColumnWidthInLineHeights * lineHeight;

    /// <summary>
    /// 把区间投影合并后按空白切分。splitAtWidestOnly 时只在最宽的空白处切成两组，否则在所有达到阈值的空白处切开。
    /// </summary>
    private static List<List<Item>> SplitByGaps(List<Item> items, Func<Item, double> start, Func<Item, double> end, double minGap, bool splitAtWidestOnly)
    {
        var sorted = items.OrderBy(start).ToList();
        var cuts = new List<(int Index, double Gap)>();
        var reach = end(sorted[0]);
        for (var i = 1; i < sorted.Count; i++)
        {
            var gap = start(sorted[i]) - reach;
            if (gap > minGap) cuts.Add((i, gap));
            reach = Math.Max(reach, end(sorted[i]));
        }
        if (cuts.Count == 0) return [items];
        if (splitAtWidestOnly) cuts = [cuts.MaxBy(c => c.Gap)];

        var groups = new List<List<Item>>();
        var from = 0;
        foreach (var (index, _) in cuts)
        {
            groups.Add(sorted.GetRange(from, index - from));
            from = index;
        }
        groups.Add(sorted.GetRange(from, sorted.Count - from));
        return groups;
    }

    private static List<List<Item>> GroupRows(List<Item> items, double lineHeight)
    {
        var rows = new List<List<Item>>();
        foreach (var item in items.OrderBy(it => it.CenterV))
        {
            var row = rows.LastOrDefault();
            if (row != null && Math.Abs(item.CenterV - row.Average(r => r.CenterV)) < 0.5 * lineHeight)
                row.Add(item);
            else
                rows.Add([item]);
        }
        return rows;
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    private sealed record Item(int Index, double MinU, double MaxU, double MinV, double MaxV)
    {
        public double Height => MaxV - MinV;
        public double CenterV => (MinV + MaxV) / 2;
        public double CoreMinV => CenterV - Height * RowCoreRatio / 2;
        public double CoreMaxV => CenterV + Height * RowCoreRatio / 2;
    }
}
