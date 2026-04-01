using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.Rendering;

public class Camera
{
    private readonly GraphicsDevice _graphics;

    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Zoom { get; set; } = 1f;
    public float Rotation { get; set; } = 0f;

    // границы мира (опционально)
    public Rectangle? WorldBounds { get; set; }

    public Camera(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public Matrix GetViewMatrix()
    {
        var viewport = _graphics.Viewport;

        return Matrix.CreateTranslation(new Vector3(-Position, 0f))
             * Matrix.CreateRotationZ(Rotation)
             * Matrix.CreateScale(Zoom, Zoom, 1f)
             * Matrix.CreateTranslation(new Vector3(viewport.Width / 2f, viewport.Height / 2f, 0f));
    }

    // перевод экранных координат в мировые (для клика мышью)
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var inverse = Matrix.Invert(GetViewMatrix());
        return Vector2.Transform(screenPos, inverse);
    }

    // перевод мировых координат в экранные
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        return Vector2.Transform(worldPos, GetViewMatrix());
    }

    // плавное следование за целью
    public void Follow(Vector2 target, float smoothing = 0.1f)
    {
        Position = Vector2.Lerp(Position, target, smoothing);

        // клампим к границам мира если заданы
        if (WorldBounds.HasValue)
        {
            var viewport = _graphics.Viewport;
            var halfW = viewport.Width / 2f / Zoom;
            var halfH = viewport.Height / 2f / Zoom;

            Position = new Vector2(
                Math.Clamp(Position.X, WorldBounds.Value.Left + halfW, WorldBounds.Value.Right - halfW),
                Math.Clamp(Position.Y, WorldBounds.Value.Top + halfH, WorldBounds.Value.Bottom - halfH)
            );
        }
    }
}