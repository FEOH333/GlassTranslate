using System;

namespace GlassTranslate.Core
{
    /// <summary>
    /// 文本相似度与矩形重叠的轻量实现（防"OCR 闪烁变体"重复翻译用）。
    /// OCR 对同一行常产出微小变体（多/少一个空格、l↔I、句尾标点有无），
    /// 精确键不同但实为同句——用"位置重叠 + 编辑距离"判定归并。
    /// </summary>
    public static class FuzzyText
    {
        /// <summary>0~1 相似度（1 = 完全相同）。短串快速路径，O(n·m) DP。</summary>
        public static double Similarity(string a, string b)
        {
            if (a == b) return 1.0;
            if (a.Length == 0 || b.Length == 0) return 0.0;
            if (Math.Abs(a.Length - b.Length) > Math.Max(a.Length, b.Length) * 0.5) return 0.0;

            int n = a.Length, m = b.Length;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                char ca = a[i - 1];
                for (int j = 1; j <= m; j++)
                {
                    int cost = ca == b[j - 1] ? 0 : 1;
                    int v = Math.Min(prev[j] + 1, cur[j - 1] + 1);
                    cur[j] = Math.Min(v, prev[j - 1] + cost);
                }
                (prev, cur) = (cur, prev);
            }
            int dist = prev[m];
            return 1.0 - dist / (double)Math.Max(n, m);
        }

        /// <summary>两矩形交并比（0~1）。</summary>
        public static double RectIoU(int ax, int ay, int aw, int ah,
            int bx, int by, int bw, int bh)
        {
            double ix = Math.Max(0, Math.Min(ax + aw, bx + bw) - Math.Max(ax, bx));
            double iy = Math.Max(0, Math.Min(ay + ah, by + bh) - Math.Max(ay, by));
            double inter = ix * iy;
            if (inter <= 0) return 0;
            double union = aw * (double)ah + bw * (double)bh - inter;
            return union <= 0 ? 0 : inter / union;
        }
    }
}
