#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SandboxGame.UI;

/// <summary>
/// HSV color picker widget: горизонтальная hue-полоса + квадрат saturation/value.
/// Текстура hue-полосы строится один раз; SV-квадрат регенерируется при смене hue.
/// </summary>
public sealed class HsvColorPicker
{
    private readonly GraphicsDevice _graphics;
    private Texture2D? _hueStrip;
    private Texture2D? _svSquare;
    private int _svSquareHueDeg = -1;
    private const int SvSize = 128;
    private const int HueStripSize = 360;

    public HsvColorPicker(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    /// <summary>Состояние пикера для одной "сессии" редактирования цвета.</summary>
    public sealed class Picker
    {
        public float Hue;        // 0..360
        public float Saturation; // 0..1
        public float Value;      // 0..1
        public Rectangle HueRect;
        public Rectangle SvRect;
    }

    public Picker BuildPicker(Color startColor, Rectangle bounds)
    {
        RgbToHsv(startColor.R, startColor.G, startColor.B, out var h, out var s, out var v);
        var p = new Picker { Hue = h, Saturation = s, Value = v };
        Layout(p, bounds);
        return p;
    }

    public void Layout(Picker p, Rectangle bounds)
    {
        // hue strip сверху на всю ширину, под ней SV-квадрат справа + индикатор слева.
        var hueHeight = 18;
        var gap = 8;
        p.HueRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, hueHeight);
        var sv = Math.Min(bounds.Height - hueHeight - gap, bounds.Width);
        p.SvRect = new Rectangle(bounds.X, bounds.Y + hueHeight + gap, sv, sv);
    }

    public void Draw(SpriteBatch sb, Picker p, Texture2D pixel)
    {
        EnsureHueStrip();
        EnsureSvSquare((int)Math.Round(p.Hue) % 360);

        sb.Draw(_hueStrip!, p.HueRect, Color.White);
        // hue marker
        var hueX = p.HueRect.X + (int)(p.Hue / 360f * p.HueRect.Width);
        sb.Draw(pixel, new Rectangle(hueX - 1, p.HueRect.Y - 2, 3, p.HueRect.Height + 4), Color.White);
        sb.Draw(pixel, new Rectangle(hueX, p.HueRect.Y - 1, 1, p.HueRect.Height + 2), Color.Black);

        sb.Draw(_svSquare!, p.SvRect, Color.White);
        // sv marker (small ring drawn via 4 lines)
        var sx = p.SvRect.X + (int)(p.Saturation * p.SvRect.Width);
        var sy = p.SvRect.Y + (int)((1f - p.Value) * p.SvRect.Height);
        var ring = new Rectangle(sx - 4, sy - 4, 8, 8);
        DrawRing(sb, pixel, ring, Color.White);
        ring.Inflate(-1, -1);
        DrawRing(sb, pixel, ring, Color.Black);
    }

    /// <summary>Если клик/drag внутри одной из зон, обновляет HSV. Возвращает true, если изменилось.</summary>
    public bool HandleMouse(Picker p, Point mousePos, bool leftDown)
    {
        if (!leftDown)
            return false;

        if (p.HueRect.Contains(mousePos))
        {
            var t = (float)(mousePos.X - p.HueRect.X) / p.HueRect.Width;
            p.Hue = Math.Clamp(t, 0f, 1f) * 360f;
            return true;
        }

        if (p.SvRect.Contains(mousePos))
        {
            var sx = (float)(mousePos.X - p.SvRect.X) / p.SvRect.Width;
            var sy = (float)(mousePos.Y - p.SvRect.Y) / p.SvRect.Height;
            p.Saturation = Math.Clamp(sx, 0f, 1f);
            p.Value = 1f - Math.Clamp(sy, 0f, 1f);
            return true;
        }

        return false;
    }

    public Color CurrentColor(Picker p)
    {
        HsvToRgb(p.Hue, p.Saturation, p.Value, out var r, out var g, out var b);
        return new Color(r, g, b);
    }

    private void EnsureHueStrip()
    {
        if (_hueStrip != null)
            return;

        var data = new Color[HueStripSize];
        for (var x = 0; x < HueStripSize; x++)
        {
            HsvToRgb(x, 1f, 1f, out var r, out var g, out var b);
            data[x] = new Color(r, g, b);
        }

        _hueStrip = new Texture2D(_graphics, HueStripSize, 1);
        _hueStrip.SetData(data);
    }

    private void EnsureSvSquare(int hueDeg)
    {
        if (_svSquare != null && _svSquareHueDeg == hueDeg)
            return;

        if (_svSquare == null)
            _svSquare = new Texture2D(_graphics, SvSize, SvSize);

        var data = new Color[SvSize * SvSize];
        for (var y = 0; y < SvSize; y++)
        {
            var v = 1f - y / (float)(SvSize - 1);
            for (var x = 0; x < SvSize; x++)
            {
                var s = x / (float)(SvSize - 1);
                HsvToRgb(hueDeg, s, v, out var r, out var g, out var b);
                data[y * SvSize + x] = new Color(r, g, b);
            }
        }

        _svSquare.SetData(data);
        _svSquareHueDeg = hueDeg;
    }

    private static void DrawRing(SpriteBatch sb, Texture2D pixel, Rectangle r, Color c)
    {
        sb.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, 1), c);
        sb.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        sb.Draw(pixel, new Rectangle(r.X, r.Y, 1, r.Height), c);
        sb.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    private static void RgbToHsv(byte r, byte g, byte b, out float h, out float s, out float v)
    {
        var rf = r / 255f;
        var gf = g / 255f;
        var bf = b / 255f;
        var max = MathF.Max(rf, MathF.Max(gf, bf));
        var min = MathF.Min(rf, MathF.Min(gf, bf));
        var d = max - min;

        v = max;
        s = max <= 0f ? 0f : d / max;

        if (d <= 0f) { h = 0f; return; }

        if (max == rf) h = 60f * (((gf - bf) / d) % 6f);
        else if (max == gf) h = 60f * (((bf - rf) / d) + 2f);
        else h = 60f * (((rf - gf) / d) + 4f);

        if (h < 0f) h += 360f;
    }

    private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
    {
        h = ((h % 360f) + 360f) % 360f;
        var c = v * s;
        var x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        var m = v - c;

        float rf, gf, bf;
        if (h < 60f) { rf = c; gf = x; bf = 0f; }
        else if (h < 120f) { rf = x; gf = c; bf = 0f; }
        else if (h < 180f) { rf = 0f; gf = c; bf = x; }
        else if (h < 240f) { rf = 0f; gf = x; bf = c; }
        else if (h < 300f) { rf = x; gf = 0f; bf = c; }
        else { rf = c; gf = 0f; bf = x; }

        r = (byte)Math.Clamp((int)MathF.Round((rf + m) * 255f), 0, 255);
        g = (byte)Math.Clamp((int)MathF.Round((gf + m) * 255f), 0, 255);
        b = (byte)Math.Clamp((int)MathF.Round((bf + m) * 255f), 0, 255);
    }
}
