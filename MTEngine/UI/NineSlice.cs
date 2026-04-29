using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

/// <summary>
/// 9-slice (9-patch) sprite renderer.
/// Splits a texture into 9 regions: 4 corners, 4 edges, 1 center.
/// Corners stay fixed, edges tile, center tiles.
/// Scale controls the drawn size of border regions — smaller scale = smaller corners,
/// more edge tiling. Like UE's "Draw as Box" with adjustable margin.
/// </summary>
public class NineSlice
{
    /// <summary>Source texture.</summary>
    public Texture2D Texture { get; }

    /// <summary>Border margins in source texture pixels (left, top, right, bottom).</summary>
    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }

    /// <summary>
    /// Scale multiplier for drawn border size.
    /// 1.0 = borders match source pixel size.
    /// 0.5 = borders drawn at half size (corners smaller, more edge repetitions).
    /// 2.0 = borders drawn at double size.
    /// </summary>
    public float Scale { get; set; } = 1f;

    public NineSlice(Texture2D texture, int left, int top, int right, int bottom)
    {
        Texture = texture;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public NineSlice(Texture2D texture, int border)
        : this(texture, border, border, border, border) { }

    /// <summary>
    /// Draw the 9-slice into the given rectangle.
    /// </summary>
    public void Draw(SpriteBatch sb, Rectangle dest, Color tint)
    {
        if (dest.Width <= 0 || dest.Height <= 0) return;

        var tw = Texture.Width;
        var th = Texture.Height;

        // Scaled border sizes for destination
        var dl = (int)(Left * Scale);
        var dt = (int)(Top * Scale);
        var dr = (int)(Right * Scale);
        var db = (int)(Bottom * Scale);

        // Clamp if destination is too small for borders
        var maxH = dest.Width;
        var maxV = dest.Height;
        if (dl + dr > maxH) { var ratio = (float)maxH / (dl + dr); dl = (int)(dl * ratio); dr = maxH - dl; }
        if (dt + db > maxV) { var ratio = (float)maxV / (dt + db); dt = (int)(dt * ratio); db = maxV - dt; }

        // Source rectangles (in texture pixels)
        var srcTopLeft = new Rectangle(0, 0, Left, Top);
        var srcTopRight = new Rectangle(tw - Right, 0, Right, Top);
        var srcBottomLeft = new Rectangle(0, th - Bottom, Left, Bottom);
        var srcBottomRight = new Rectangle(tw - Right, th - Bottom, Right, Bottom);

        var srcTop = new Rectangle(Left, 0, tw - Left - Right, Top);
        var srcBottom = new Rectangle(Left, th - Bottom, tw - Left - Right, Bottom);
        var srcLeftEdge = new Rectangle(0, Top, Left, th - Top - Bottom);
        var srcRightEdge = new Rectangle(tw - Right, Top, Right, th - Top - Bottom);

        var srcCenter = new Rectangle(Left, Top, tw - Left - Right, th - Top - Bottom);

        // Destination positions
        var cx = dest.X + dl;          // center X start
        var cy = dest.Y + dt;          // center Y start
        var cw = dest.Width - dl - dr; // center width
        var ch = dest.Height - dt - db; // center height

        // 4 corners (stretched to scaled border size)
        sb.Draw(Texture, new Rectangle(dest.X, dest.Y, dl, dt), srcTopLeft, tint);
        sb.Draw(Texture, new Rectangle(dest.Right - dr, dest.Y, dr, dt), srcTopRight, tint);
        sb.Draw(Texture, new Rectangle(dest.X, dest.Bottom - db, dl, db), srcBottomLeft, tint);
        sb.Draw(Texture, new Rectangle(dest.Right - dr, dest.Bottom - db, dr, db), srcBottomRight, tint);

        // 4 edges (tiled)
        if (cw > 0 && dt > 0) TileHorizontal(sb, srcTop, new Rectangle(cx, dest.Y, cw, dt), tint);
        if (cw > 0 && db > 0) TileHorizontal(sb, srcBottom, new Rectangle(cx, dest.Bottom - db, cw, db), tint);
        if (ch > 0 && dl > 0) TileVertical(sb, srcLeftEdge, new Rectangle(dest.X, cy, dl, ch), tint);
        if (ch > 0 && dr > 0) TileVertical(sb, srcRightEdge, new Rectangle(dest.Right - dr, cy, dr, ch), tint);

        // Center (tiled)
        if (cw > 0 && ch > 0) TileRect(sb, srcCenter, new Rectangle(cx, cy, cw, ch), tint);
    }

    private void TileHorizontal(SpriteBatch sb, Rectangle src, Rectangle dest, Color tint)
    {
        if (src.Width <= 0 || src.Height <= 0) return;
        var tileW = Math.Max(1, (int)(src.Width * Scale));
        for (var x = dest.X; x < dest.Right; x += tileW)
        {
            var drawW = Math.Min(tileW, dest.Right - x);
            var srcW = (int)(drawW / Scale);
            if (srcW <= 0) srcW = 1;
            if (srcW > src.Width) srcW = src.Width;
            sb.Draw(Texture, new Rectangle(x, dest.Y, drawW, dest.Height),
                new Rectangle(src.X, src.Y, srcW, src.Height), tint);
        }
    }

    private void TileVertical(SpriteBatch sb, Rectangle src, Rectangle dest, Color tint)
    {
        if (src.Width <= 0 || src.Height <= 0) return;
        var tileH = Math.Max(1, (int)(src.Height * Scale));
        for (var y = dest.Y; y < dest.Bottom; y += tileH)
        {
            var drawH = Math.Min(tileH, dest.Bottom - y);
            var srcH = (int)(drawH / Scale);
            if (srcH <= 0) srcH = 1;
            if (srcH > src.Height) srcH = src.Height;
            sb.Draw(Texture, new Rectangle(dest.X, y, dest.Width, drawH),
                new Rectangle(src.X, src.Y, src.Width, srcH), tint);
        }
    }

    private void TileRect(SpriteBatch sb, Rectangle src, Rectangle dest, Color tint)
    {
        if (src.Width <= 0 || src.Height <= 0) return;
        var tileW = Math.Max(1, (int)(src.Width * Scale));
        var tileH = Math.Max(1, (int)(src.Height * Scale));

        for (var y = dest.Y; y < dest.Bottom; y += tileH)
        {
            var drawH = Math.Min(tileH, dest.Bottom - y);
            var srcH = (int)(drawH / Scale);
            if (srcH <= 0) srcH = 1;
            if (srcH > src.Height) srcH = src.Height;

            for (var x = dest.X; x < dest.Right; x += tileW)
            {
                var drawW = Math.Min(tileW, dest.Right - x);
                var srcW = (int)(drawW / Scale);
                if (srcW <= 0) srcW = 1;
                if (srcW > src.Width) srcW = src.Width;
                sb.Draw(Texture, new Rectangle(x, y, drawW, drawH),
                    new Rectangle(src.X, src.Y, srcW, srcH), tint);
            }
        }
    }
}
