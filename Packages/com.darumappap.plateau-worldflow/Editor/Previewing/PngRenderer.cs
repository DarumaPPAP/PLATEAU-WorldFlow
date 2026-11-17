using System;
using System.Linq;
using DarumaPPAP.PlateauWorldFlow.ReadOnly;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DarumaPPAP.PlateauWorldFlow.Previewing
{
    /// <summary>
    /// 概略図の形状とラベルをCPU上のピクセルに描き、UnityのPNGエンコーダーで出力する。
    /// </summary>
    internal sealed class PngRenderer
    {
        private const int CanvasWidth = 640;
        private const int CanvasHeight = 448;
        private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.:/ ";
        // 5列×7行。各文字を行単位のビットマスクで保持する。
        private static readonly string[] Glyphs = {
            "0E11111F111111", "1E11111E11111E", "0F10101010100F", "1E11111111111E", "1F10101E10101F", "1F10101E101010",
            "0F10101711110F", "1111111F111111", "0E04040404040E", "0702020202120C", "11121418141211", "1010101010101F",
            "111B1515111111", "11191513111111", "0E11111111110E", "1E11111E101010", "0E11111115120D", "1E11111E141211",
            "0F10100E01011E", "1F040404040404", "1111111111110E", "11111111110A04", "1111111515150A", "11110A040A1111",
            "11110A04040404", "1F01020408101F", "0E11131519110E", "040C040404040E", "0E11010204081F", "1E01010601011E",
            "02060A121F0202", "1F101E0101011E", "0E10101E11110E", "1F010204080808", "0E11110E11110E", "0E11110F01010E",
            "0000001F000000", "00000000000C0C", "000C0C000C0C00", "01010204081010", "00000000000000"
        };

        private readonly Color32[] _pixels = new Color32[CanvasWidth * CanvasHeight];
        private readonly Color32 _ink = new Color32(221, 233, 245, 255);
        private readonly Color32 _available = new Color32(36, 145, 132, 255);
        private readonly Color32 _availableBorder = new Color32(21, 94, 86, 255);
        private readonly Color32 _selected = new Color32(255, 215, 120, 255);
        private readonly Color32 _missing = new Color32(227, 131, 114, 255);
        private readonly Color32 _unverified = new Color32(158, 173, 190, 255);

        /// <summary>
        /// 北を上にした図とDataset・座標・凡例を指定サイズのPNGとして返す。
        /// </summary>
        internal byte[] Draw(Preview preview, int width, int height)
        {
            Fill(0, 0, CanvasWidth, CanvasHeight, new Color32(17, 29, 42, 255));
            Fill(20, 94, 600, 280, new Color32(26, 45, 61, 255));
            Label("SCHEMATIC / NOT A MAP", 20, 19, 3, _ink);
            Label("NOT A 3D CITY MODEL", 20, 49, 1, _unverified);
            Label("DATASET: " + preview.Snapshot.DatasetId, 20, 63, 1, _ink);
            Label("CRS: " + (preview.Extent?.Crs ?? "UNVERIFIED"), 20, 78, 1, _unverified);
            Label("N", 598, 104, 2, _ink);
            Fill(602, 123, 2, 16, _ink);
            Fill(599, 126, 8, 2, _ink);
            if (preview.Extent == null)
            {
                Label("NO DRAWABLE EXTENT", 42, 211, 2, _unverified);
            }
            else
            {
                DrawMap(preview);
                var extent = preview.Extent;
                Label(FormattableString.Invariant($"SW {extent.South:F4} / {extent.West:F4}    NE {extent.North:F4} / {extent.East:F4}"), 22, 358, 1, _ink);
            }

            Legend(20, "AVAILABLE", _available);
            Legend(170, "SELECTED BORDER", _selected);
            int unavailable = preview.Cells.Count(cell => cell.Availability == "UNAVAILABLE");
            int unverified = preview.Cells.Count(cell => cell.Availability == "UNVERIFIED");
            Label("COUNTS ONLY / NOT DRAWN:", 20, 420, 1, _ink);
            Label(FormattableString.Invariant($"UNAVAILABLE: {unavailable}"), 180, 420, 1, _missing);
            Label(FormattableString.Invariant($"UNVERIFIED: {unverified}"), 340, 420, 1, _unverified);

            // 同じ倍率で縮小し、指定された幅と高さの余白で縦横比を維持する。
            float scale = Math.Min(width / (float)CanvasWidth, height / (float)CanvasHeight);
            float offsetX = (width - CanvasWidth * scale) / 2;
            float offsetY = (height - CanvasHeight * scale) / 2;
            var output = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int sourceY = Mathf.FloorToInt((y + 0.5f - offsetY) / scale);
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Mathf.FloorToInt((x + 0.5f - offsetX) / scale);
                    // UnityのPNGエンコーダーに渡す行は下端から並べる。
                    output[(height - 1 - y) * width + x] = sourceX >= 0 && sourceX < CanvasWidth && sourceY >= 0 && sourceY < CanvasHeight ? _pixels[sourceY * CanvasWidth + sourceX] : _pixels[0];
                }
            }

            return ImageConversion.EncodeArrayToPNG(output, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
        }

        private void DrawMap(Preview preview)
        {
            var bounds = preview.Extent;
            double longitudeScale = Math.Cos((bounds.South + bounds.North) * Math.PI / 360);
            double geographicWidth = (bounds.East - bounds.West) * longitudeScale;
            double geographicHeight = bounds.North - bounds.South;
            double scale = Math.Min(530 / geographicWidth, 231 / geographicHeight);
            double left = 320 - geographicWidth * scale / 2;
            double top = 224 - geographicHeight * scale / 2;
            // 粗いメッシュを先に描き、同じ範囲内の細かい境界を後から表示する。
            var located = preview.Cells.Where(cell => cell.Extent != null).OrderByDescending(cell => (cell.Extent.North - cell.Extent.South) * (cell.Extent.East - cell.Extent.West)).ThenBy(cell => cell.Selected);
            foreach (var cell in located)
            {
                var extent = cell.Extent;
                int x = (int)Math.Round(left + (extent.West - bounds.West) * longitudeScale * scale);
                int y = (int)Math.Round(top + (bounds.North - extent.North) * scale);
                int w = Math.Max(1, (int)Math.Round((extent.East - extent.West) * longitudeScale * scale));
                int h = Math.Max(1, (int)Math.Round((extent.North - extent.South) * scale));
                Fill(x, y, w, h, _available);
                if (cell.Selected || (w > 2 && h > 2))
                {
                    int border = cell.Selected && w > 4 && h > 4 ? 2 : 1;
                    var color = cell.Selected ? _selected : _availableBorder;
                    Fill(x, y, w, border, color);
                    Fill(x, y + h - border, w, border, color);
                    Fill(x, y, border, h, color);
                    Fill(x + w - border, y, border, h, color);
                }

                if (w >= cell.Code.Length * 6 + 6 && h >= 15)
                {
                    Label(cell.Code, x + 4, y + 4, 1, _ink);
                }
            }
        }

        private void Legend(int x, string text, Color32 color)
        {
            Fill(x, 394, 12, 12, color);
            Label(text, x + 18, 397, 1, _ink);
        }

        private void Label(string text, int x, int y, int scale, Color32 color)
        {
            foreach (char symbol in text.ToUpperInvariant())
            {
                int index = Characters.IndexOf(symbol == '_' ? '-' : symbol);
                if (index >= 0)
                {
                    for (int row = 0; row < 7; row++)
                    {
                        int bits = Convert.ToInt32(Glyphs[index].Substring(row * 2, 2), 16);
                        for (int column = 0; column < 5; column++)
                        {
                            if ((bits & (1 << (4 - column))) != 0)
                            {
                                Fill(x + column * scale, y + row * scale, scale, scale, color);
                            }
                        }
                    }
                }

                x += 6 * scale;
            }
        }

        private void Fill(int x, int y, int width, int height, Color32 color)
        {
            for (int row = Math.Max(0, y); row < Math.Min(CanvasHeight, y + height); row++)
            {
                for (int column = Math.Max(0, x); column < Math.Min(CanvasWidth, x + width); column++)
                {
                    _pixels[row * CanvasWidth + column] = color;
                }
            }
        }
    }
}
