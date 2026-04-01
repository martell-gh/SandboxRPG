using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Components;

public class SpriteComponent : Component
{
    public Texture2D? Texture { get; set; }
    public Rectangle? SourceRect { get; set; }
    public Color Color { get; set; } = Color.White;
    public Vector2 Origin { get; set; } = Vector2.Zero;
    public float LayerDepth { get; set; } = 0f;
    public bool Visible { get; set; } = true;

    // анимация
    public AnimationSet? AnimationSet { get; set; }
    public AnimationPlayer? AnimationPlayer { get; private set; }

    public SpriteComponent(Texture2D texture)
    {
        Texture = texture;
    }

    public SpriteComponent() { }

    // установить анимацию и сразу заиграть клип
    public void SetAnimations(AnimationSet set, string defaultClip = "idle")
    {
        AnimationSet = set;
        AnimationPlayer = new AnimationPlayer();
        if (set.HasClip(defaultClip))
            AnimationPlayer.Play(set, defaultClip);
        else if (set.GetAllClips().Any())
            AnimationPlayer.Play(set.GetAllClips().First());
    }

    public void PlayClip(string clipName, bool restart = false)
    {
        if (AnimationSet == null || AnimationPlayer == null) return;
        AnimationPlayer.Play(AnimationSet, clipName, restart);
    }

    // обновить анимацию — вызывать каждый тик
    public void UpdateAnimation(float deltaTime)
    {
        AnimationPlayer?.Update(deltaTime);

        // если анимация играет — берём SourceRect из неё
        if (AnimationPlayer?.IsPlaying == true)
            SourceRect = AnimationPlayer.GetSourceRect();
    }
}