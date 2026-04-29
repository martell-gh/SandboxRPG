using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

internal static class UIDrawHelper
{
    /// <summary>
    /// Draw background using 9-slice if available, tiled texture, or flat color.
    /// </summary>
    public static void DrawBackground(
        SpriteBatch sb,
        Texture2D pixel,
        Rectangle bounds,
        Color? flatColor,
        Texture2D? texture,
        Color textureTint,
        bool tileTexture,
        NineSlice? nineSlice = null,
        Color? nineSliceTint = null)
    {
        // Priority: NineSlice > Texture > Flat color
        if (nineSlice != null)
        {
            nineSlice.Draw(sb, bounds, nineSliceTint ?? Color.White);
            return;
        }

        if (texture != null)
        {
            if (tileTexture)
                DrawTiledTexture(sb, texture, bounds, textureTint);
            else
                sb.Draw(texture, bounds, textureTint);
            return;
        }

        if (flatColor.HasValue)
            sb.Draw(pixel, bounds, flatColor.Value);
    }

    public static void DrawTiledTexture(SpriteBatch sb, Texture2D texture, Rectangle bounds, Color tint)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var tileWidth = Math.Max(1, texture.Width);
        var tileHeight = Math.Max(1, texture.Height);

        for (var y = bounds.Y; y < bounds.Bottom; y += tileHeight)
        {
            var drawHeight = Math.Min(tileHeight, bounds.Bottom - y);

            for (var x = bounds.X; x < bounds.Right; x += tileWidth)
            {
                var drawWidth = Math.Min(tileWidth, bounds.Right - x);
                var source = new Rectangle(0, 0, drawWidth, drawHeight);
                var destination = new Rectangle(x, y, drawWidth, drawHeight);
                sb.Draw(texture, destination, source, tint);
            }
        }
    }

    public static void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle bounds, Color borderColor, int thickness = 1)
    {
        if (borderColor.A == 0 || thickness <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        sb.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), borderColor);
        sb.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), borderColor);
        sb.Draw(pixel, new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), borderColor);
        sb.Draw(pixel, new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), borderColor);
    }
}
