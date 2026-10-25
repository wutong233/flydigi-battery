// 托盘图标绘制：电池 + 点阵数字、充电指示、未连接态；以及 ICO 编码（托盘图标 / exe 图标共用）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace FlydigiBattery
{
    internal static class IconFactory
    {
        /// <summary>设计栅格 16x16，按实际图标尺寸等比缩放。</summary>
        private const double DesignSize = 16.0;

        /// <summary>3x7 点阵数字；16px 下用 1px 笔画，细黑字在浅色底上仍可辨认。</summary>
        private static readonly string[][] Digits = new string[][]
        {
            new string[] { "###", "#.#", "#.#", "#.#", "#.#", "#.#", "###" }, // 0
            new string[] { ".#.", "##.", ".#.", ".#.", ".#.", ".#.", "###" }, // 1
            new string[] { "###", "..#", "..#", "###", "#..", "#..", "###" }, // 2
            new string[] { "###", "..#", "..#", "###", "..#", "..#", "###" }, // 3
            new string[] { "#.#", "#.#", "#.#", "###", "..#", "..#", "..#" }, // 4
            new string[] { "###", "#..", "#..", "###", "..#", "..#", "###" }, // 5
            new string[] { "###", "#..", "#..", "###", "#.#", "#.#", "###" }, // 6
            new string[] { "###", "..#", "..#", "..#", "..#", "..#", "..#" }, // 7
            new string[] { "###", "#.#", "#.#", "###", "#.#", "#.#", "###" }, // 8
            new string[] { "###", "#.#", "#.#", "###", "..#", "..#", "###" }, // 9
        };

        private static readonly string[] QuestionMark = new string[]
        {
            "###", "..#", ".##", ".#.", "...", ".#.", ".#."
        };

        public static readonly Color ChargingAccent = Color.FromArgb(255, 120, 255, 140);

        private static readonly Color RedLevel = Color.FromArgb(255, 231, 76, 60);
        private static readonly Color YellowLevel = Color.FromArgb(255, 241, 196, 15);
        private static readonly Color GreenLevel = Color.FromArgb(255, 46, 204, 113);
        private static readonly Color UnknownLevel = Color.FromArgb(255, 150, 150, 150);

        /// <summary>0–2 红、3–4 黄、5–6 绿。</summary>
        public static Color LevelColor(int level)
        {
            if (level < 0) { return UnknownLevel; }
            if (level <= 2) { return RedLevel; }
            if (level <= 4) { return YellowLevel; }
            return GreenLevel;
        }

        /// <summary>电池内填充比例：0 档为空，满档为 1，中间按档位线性减少。</summary>
        public static double FillFraction(int level, int maxLevel)
        {
            if (maxLevel <= 0 || level <= 0) { return 0.0; }
            if (level >= maxLevel) { return 1.0; }
            return (double)level / maxLevel;
        }

        public static Bitmap NewCanvas(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            return bmp;
        }

        /// <summary>图标内文字：满电留空，其余显示两位百分比；无效档位显示 ?。</summary>
        internal static string PercentLabel(int level, int maxLevel)
        {
            int percent = Text.PercentOf(level, maxLevel);
            if (percent < 0) { return "?"; }
            return percent >= 100 ? "" : percent.ToString(CultureInfo.InvariantCulture);
        }

        public static Bitmap RenderBattery(int level, int maxLevel, bool plugged, int size)
        {
            Bitmap bmp = NewCanvas(size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.Transparent);

                Color color = LevelColor(level);
                DrawBatteryCore(g, size, color, Lighten(color, 0.55),
                    FillFraction(level, maxLevel), plugged, PercentLabel(level, maxLevel), Color.Black);
            }
            return bmp;
        }

        /// <summary>已连接、但拿不到 7 档电量时的中性图标。</summary>
        public static Bitmap RenderUnknown(int size)
        {
            Bitmap bmp = NewCanvas(size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.Transparent);
                DrawBatteryCore(g, size, UnknownLevel, Color.FromArgb(255, 55, 55, 55), 0.0, false, "?", Color.White);
            }
            return bmp;
        }

        private static void DrawBatteryCore(Graphics g, int size, Color color, Color empty,
            double fillFraction, bool plugged, string text, Color textColor)
        {
            double s = size / DesignSize;
            int cell = (int)Math.Max(2, Math.Round(2.0 * s));
            int nubW = size <= 16 ? 1 : (int)Math.Max(2, Math.Round(1.8 * s));
            int nubH = (int)Math.Max(3, Math.Round(4.0 * s));

            // 电池体：16px 下用满宽度承载两位数字
            int bodyH = cell * 5 + 2;
            int margin = size >= 20 ? 2 : 0;
            int bodyW = Math.Min((int)Math.Round(15.0 * s), size - nubW - margin);
            int bx = Math.Max(0, (int)Math.Round((size - bodyW - nubW) / 2.0));
            int by = Math.Max(0, (int)Math.Round((size - bodyH) / 2.0));
            int bw = bodyW;
            int bh = bodyH;
            int border = (int)Math.Max(1, Math.Round(s));

            // 空电部分用同色系暗色，保证 0 档也能看出红/黄/绿分组
            using (SolidBrush bg = new SolidBrush(empty))
            {
                g.FillRectangle(bg, bx, by, bw, bh);
            }

            int ix = bx + border, iy = by + border;
            int iw = Math.Max(0, bw - border * 2);
            int ih = Math.Max(0, bh - border * 2);
            double frac = Math.Max(0.0, Math.Min(1.0, fillFraction));
            int fw = (int)Math.Round(iw * frac);
            if (fw > 0)
            {
                using (SolidBrush fill = new SolidBrush(color))
                {
                    g.FillRectangle(fill, ix, iy, fw, ih);
                }
            }

            // 外框用亮色，空电时也保留档位颜色
            using (SolidBrush edge = new SolidBrush(color))
            {
                g.FillRectangle(edge, bx, by, bw, border);
                g.FillRectangle(edge, bx, by + bh - border, bw, border);
                g.FillRectangle(edge, bx, by, border, bh);
                g.FillRectangle(edge, bx + bw - border, by, border, bh);
            }

            // 正极凸点：充电时变亮绿
            Color nubColor = plugged ? ChargingAccent : color;
            using (SolidBrush nub = new SolidBrush(nubColor))
            {
                g.FillRectangle(nub, bx + bw, by + (bh - nubH) / 2, nubW, nubH);
            }

            // 百分比数字（3x5 点阵）：按「两位数字」确定字号，单位和十位大小一致
            if (!string.IsNullOrEmpty(text))
            {
                int textCell = cell;
                int layoutChars = Math.Max(2, text.Length);
                while (textCell > 1 &&
                       (MeasureWidth(layoutChars, textCell) > iw || textCell * 7 > ih))
                {
                    textCell--;
                }
                if (textCell > 4) { textCell = 4; }

                float digW = MeasureText(text, textCell);
                float digH = textCell * 7;
                float dx = (float)Math.Round(bx + (bw - digW) / 2);
                float dy = (float)Math.Round(by + (bh - digH) / 2);

                using (SolidBrush textBrush = new SolidBrush(textColor))
                {
                    DrawText(g, text, dx, dy, textCell, textBrush);
                }
            }

            // 底部横线：充电中
            if (plugged)
            {
                using (SolidBrush acc = new SolidBrush(ChargingAccent))
                {
                    int y = by + bh + border;
                    if (y < size) { g.FillRectangle(acc, bx, y, bw, border); }
                }
            }
        }

        private static Color Lighten(Color c, double amount)
        {
            return Color.FromArgb(255,
                (int)Math.Round(c.R + (255 - c.R) * amount),
                (int)Math.Round(c.G + (255 - c.G) * amount),
                (int)Math.Round(c.B + (255 - c.B) * amount));
        }

        private static float MeasureWidth(int chars, int cell)
        {
            if (chars <= 0) { return 0; }
            int advance = cell * 3 + 1;
            return advance * (chars - 1) + cell * 3;
        }

        private static float MeasureText(string text, int cell)
        {
            if (string.IsNullOrEmpty(text)) { return 0; }
            return MeasureWidth(text.Length, cell);
        }

        private static void DrawText(Graphics g, string text, float x, float y, int cell, Brush brush)
        {
            int advance = cell * 3 + 1;
            for (int i = 0; i < text.Length; i++)
            {
                string[] glyph = null;
                int d = text[i] - '0';
                if (d >= 0 && d <= 9) { glyph = Digits[d]; }
                else if (text[i] == '?') { glyph = QuestionMark; }
                if (glyph == null) { continue; }
                DrawGlyph(g, glyph, x + i * advance, y, cell, brush);
            }
        }

        private static void DrawGlyph(Graphics g, string[] glyph, float x, float y, int cell, Brush brush)
        {
            for (int r = 0; r < glyph.Length; r++)
            {
                string row = glyph[r];
                for (int c = 0; c < 3; c++)
                {
                    if (row[c] != '#') { continue; }
                    g.FillRectangle(brush, x + c * cell, y + r * cell, cell + 0.3f, cell + 0.3f);
                }
            }
        }

        private static GraphicsPath RoundedPath(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = 2 * r;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>没检测到手柄时的图标：灰色手柄 + 红色斜杠。</summary>
        public static Bitmap RenderDisabled(int size)
        {
            Bitmap bmp = NewCanvas(size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                double s = size / DesignSize;

                using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(255, 138, 138, 138)))
                using (GraphicsPath body = RoundedPath((float)(1.9 * s), (float)(5.2 * s), (float)(12.2 * s), (float)(6.0 * s), (float)(2.6 * s)))
                {
                    g.FillPath(bodyBrush, body);
                    using (GraphicsPath gripL = RoundedPath((float)(1.6 * s), (float)(7.6 * s), (float)(3.6 * s), (float)(5.0 * s), (float)(1.7 * s)))
                    using (GraphicsPath gripR = RoundedPath((float)(10.8 * s), (float)(7.6 * s), (float)(3.6 * s), (float)(5.0 * s), (float)(1.7 * s)))
                    {
                        g.FillPath(bodyBrush, gripL);
                        g.FillPath(bodyBrush, gripR);
                    }
                }

                // 白色描边让灰色机身在小尺寸下也立得住
                using (Pen edge = new Pen(Color.FromArgb(200, 235, 235, 235), (float)(0.7 * s)))
                using (GraphicsPath edgePath = RoundedPath((float)(1.9 * s), (float)(5.2 * s), (float)(12.2 * s), (float)(6.0 * s), (float)(2.6 * s)))
                {
                    g.DrawPath(edge, edgePath);
                }

                // 斜杠：先画白色光晕再画红线
                float x1 = (float)(2.2 * s), y1 = (float)(13.4 * s);
                float x2 = (float)(13.8 * s), y2 = (float)(2.4 * s);
                using (Pen halo = new Pen(Color.FromArgb(235, 255, 255, 255), (float)(3.4 * s)))
                {
                    halo.StartCap = LineCap.Round;
                    halo.EndCap = LineCap.Round;
                    g.DrawLine(halo, x1, y1, x2, y2);
                }
                using (Pen slash = new Pen(Color.FromArgb(255, 231, 76, 60), (float)(2.0 * s)))
                {
                    slash.StartCap = LineCap.Round;
                    slash.EndCap = LineCap.Round;
                    g.DrawLine(slash, x1, y1, x2, y2);
                }
            }
            return bmp;
        }

        /// <summary>按状态渲染：有电量 → 电池；已连接但无电量 → 问号电池；未连接 → 禁用图标。</summary>
        public static Bitmap Render(Status status, int size)
        {
            if (status == null) { return RenderDisabled(size); }
            if (!status.Ok || status.Devices.Count == 0)
            {
                return status.Connected ? RenderUnknown(size) : RenderDisabled(size);
            }
            Device d = status.Devices[0];
            if (!d.HasLevel) { return status.Connected ? RenderUnknown(size) : RenderDisabled(size); }
            return RenderBattery(d.Level, d.LevelMax, d.Plugged, size);
        }

        // ------------------------------------------------------------- ICO 编码 ----

        /// <summary>把一张 32bpp 位图编码成单图 ICO 字节流（含 AND 掩码，兼容所有 Windows 版本）。</summary>
        public static byte[] EncodeIco(Bitmap bitmap)
        {
            int w = bitmap.Width;
            int h = bitmap.Height;
            int xorStride = w * 4;
            int andStride = ((w + 31) / 32) * 4;
            int dibSize = 40 + xorStride * h + andStride * h;

            using (MemoryStream ms = new MemoryStream(6 + 16 + dibSize))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write((ushort)0);   // reserved
                bw.Write((ushort)1);   // type = icon
                bw.Write((ushort)1);   // image count

                bw.Write((byte)(w >= 256 ? 0 : w));
                bw.Write((byte)(h >= 256 ? 0 : h));
                bw.Write((byte)0);     // color count
                bw.Write((byte)0);     // reserved
                bw.Write((ushort)1);   // planes
                bw.Write((ushort)32);  // bit count
                bw.Write((uint)dibSize);
                bw.Write((uint)(6 + 16));

                // BITMAPINFOHEADER（biHeight 为 XOR+AND 两段之和）
                bw.Write((uint)40);
                bw.Write(w);
                bw.Write(h * 2);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)0);     // BI_RGB
                bw.Write((uint)(xorStride * h));
                bw.Write(0);
                bw.Write(0);
                bw.Write((uint)0);
                bw.Write((uint)0);

                // XOR：BGRA，自下而上
                for (int y = h - 1; y >= 0; y--)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        bw.Write(c.B);
                        bw.Write(c.G);
                        bw.Write(c.R);
                        bw.Write(c.A);
                    }
                }
                // AND 掩码：32bpp 用不上，但必须存在
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < andStride; x++) { bw.Write((byte)0); }
                }
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>多尺寸 ICO（exe 图标用）。</summary>
        public static byte[] EncodeIcoMulti(int[] sizes)
        {
            List<Bitmap> bitmaps = new List<Bitmap>();
            List<byte[]> images = new List<byte[]>();
            try
            {
                foreach (int size in sizes)
                {
                    Bitmap bmp = RenderBattery(5, 6, false, size);
                    bitmaps.Add(bmp);
                    images.Add(EncodeIcoImage(bmp));
                }

                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write((ushort)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)images.Count);
                    int offset = 6 + 16 * images.Count;
                    for (int i = 0; i < images.Count; i++)
                    {
                        int size = bitmaps[i].Width;
                        bw.Write((byte)(size >= 256 ? 0 : size));
                        bw.Write((byte)(size >= 256 ? 0 : size));
                        bw.Write((byte)0);
                        bw.Write((byte)0);
                        bw.Write((ushort)1);
                        bw.Write((ushort)32);
                        bw.Write((uint)images[i].Length);
                        bw.Write((uint)offset);
                        offset += images[i].Length;
                    }
                    foreach (byte[] img in images) { bw.Write(img); }
                    bw.Flush();
                    return ms.ToArray();
                }
            }
            finally
            {
                foreach (Bitmap b in bitmaps) { b.Dispose(); }
            }
        }

        private static byte[] EncodeIcoImage(Bitmap bitmap)
        {
            int w = bitmap.Width;
            int h = bitmap.Height;

            // 256px 用 PNG 压缩（Vista+ 支持），否则 256*256*4 = 256KB 会把 exe 撑大
            if (w >= 128)
            {
                using (MemoryStream png = new MemoryStream())
                {
                    bitmap.Save(png, ImageFormat.Png);
                    return png.ToArray();
                }
            }

            int xorStride = w * 4;
            int andStride = ((w + 31) / 32) * 4;
            using (MemoryStream ms = new MemoryStream(40 + xorStride * h + andStride * h))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write((uint)40);
                bw.Write(w);
                bw.Write(h * 2);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)0);
                bw.Write((uint)(xorStride * h));
                bw.Write(0);
                bw.Write(0);
                bw.Write((uint)0);
                bw.Write((uint)0);
                for (int y = h - 1; y >= 0; y--)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                    }
                }
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < andStride; x++) { bw.Write((byte)0); }
                }
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>图标句柄 + 它依赖的字节流（缓存里一起保存，避免流被回收后图标失效）。</summary>
        internal sealed class IconEntry : IDisposable
        {
            public Icon Icon;
            public MemoryStream Stream;

            public void Dispose()
            {
                if (Icon != null) { Icon.Dispose(); Icon = null; }
                if (Stream != null) { Stream.Dispose(); Stream = null; }
            }
        }

        public static IconEntry CreateIcon(Bitmap bitmap)
        {
            byte[] bytes = EncodeIco(bitmap);
            MemoryStream stream = new MemoryStream(bytes);
            IconEntry entry = new IconEntry();
            entry.Stream = stream;
            entry.Icon = new Icon(stream);
            return entry;
        }

        /// <summary>图标对照表（--preview）：每行一种尺寸，列出 0–6 档、充电态、未知态、未连接态。</summary>
        public static Bitmap RenderSheet(int[] sizes, out int cellSize)
        {
            cellSize = 40;
            int cols = 10;
            int rows = sizes.Length;
            int cell = 0;
            foreach (int s in sizes) { if (s > cell) { cell = s; } }
            cell += 8;
            cellSize = cell;

            Bitmap sheet = new Bitmap(cols * cell, rows * cell);
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(255, 32, 32, 32));
                for (int r = 0; r < rows; r++)
                {
                    int size = sizes[r];
                    int x = (cell - size) / 2;
                    int y = r * cell + (cell - size) / 2;
                    int col = 0;
                    for (int lv = 0; lv <= 6; lv++)
                    {
                        using (Bitmap b = RenderBattery(lv, 6, false, size))
                        {
                            g.DrawImageUnscaled(b, x + col * cell, y);
                        }
                        col++;
                    }
                    using (Bitmap b = RenderBattery(4, 6, true, size))
                    {
                        g.DrawImageUnscaled(b, x + col * cell, y);
                    }
                    col++;
                    using (Bitmap b = RenderUnknown(size))
                    {
                        g.DrawImageUnscaled(b, x + col * cell, y);
                    }
                    col++;
                    using (Bitmap b = RenderDisabled(size))
                    {
                        g.DrawImageUnscaled(b, x + col * cell, y);
                    }
                }
            }
            return sheet;
        }
    }
}
