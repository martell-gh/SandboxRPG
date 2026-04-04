using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UIImage : UIElement
{
    public Texture2D? Texture { get; set; }
    public Rectangle? SourceRect { get; set; }
    public Color Tint { get; set; } = Color.White;

    /// <summary>Path to texture file, resolved by UIManager at load time.</summary>
    public string? TexturePath { get; set; }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible || Texture == null) return;
        sb.Draw(Texture, Bounds, SourceRect, Tint);
    }
}
