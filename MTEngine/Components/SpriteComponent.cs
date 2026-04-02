using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Components;

[RegisterComponent("sprite")]
public class SpriteComponent : Component, IPrototypeInitializable
{
    public Texture2D? Texture { get; set; }
    public Rectangle? SourceRect { get; set; }
    public Color Color { get; set; } = Color.White;
    public Vector2 Origin { get; set; } = Vector2.Zero;

    [DataField("layerDepth")]
    public float LayerDepth { get; set; } = 0f;

    [DataField("ySort")]
    public bool YSort { get; set; } = false;

    [DataField("sortOffsetY")]
    public float SortOffsetY { get; set; } = 0f;

    public bool Visible { get; set; } = true;

    [DataField("source")]
    public string? Source { get; set; }

    [DataField("color")]
    public string ColorHex
    {
        get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";
        set => Color = AssetManager.ParseHexColor(value, Color.White);
    }

    [DataField("srcX")]
    public int SrcX { get; set; }

    [DataField("srcY")]
    public int SrcY { get; set; }

    [DataField("width")]
    public int Width { get; set; } = 32;

    [DataField("height")]
    public int Height { get; set; } = 32;

    // анимация
    public AnimationSet? AnimationSet { get; set; }
    public AnimationPlayer? AnimationPlayer { get; private set; }

    public SpriteComponent(Texture2D texture)
    {
        Texture = texture;
    }

    public SpriteComponent() { }

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        if (!string.IsNullOrWhiteSpace(Source) && proto.DirectoryPath != null)
        {
            var texturePath = Path.Combine(proto.DirectoryPath, Source);
            Texture = assets.LoadFromFile(texturePath);
        }

        if (Texture == null)
        {
            Texture = new Texture2D(assets.GraphicsDevice, 32, 32);
            var pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.White;
            Texture.SetData(pixels);
        }

        SourceRect = new Rectangle(SrcX, SrcY, Width, Height);
        Origin = new Vector2(Width / 2f, Height / 2f);

        if (!string.IsNullOrWhiteSpace(proto.AnimationsPath))
        {
            var animSet = AnimationSet.LoadFromFile(proto.AnimationsPath);
            if (animSet != null)
            {
                if (!string.IsNullOrEmpty(animSet.TexturePath) && proto.DirectoryPath != null)
                {
                    var animTexPath = Path.Combine(proto.DirectoryPath, animSet.TexturePath);
                    var animTex = assets.LoadFromFile(animTexPath);
                    if (animTex != null)
                    {
                        Texture = animTex;
                        animSet.TexturePath = animTexPath;
                    }
                }

                AnimationSet = animSet;
                AnimationPlayer = new AnimationPlayer();

                if (animSet.HasClip("idle_down"))
                    AnimationPlayer.Play(animSet, "idle_down");
                else if (animSet.HasClip("idle"))
                    AnimationPlayer.Play(animSet, "idle");
                else if (animSet.GetAllClips().Any())
                    AnimationPlayer.Play(animSet.GetAllClips().First());

                if (AnimationPlayer.IsPlaying)
                    SourceRect = AnimationPlayer.GetSourceRect();
            }
        }
    }

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
        if (!AnimationSet.HasClip(clipName)) return;

        AnimationPlayer.Play(AnimationSet, clipName, restart);
    }

    public void UpdateAnimation(float deltaTime)
    {
        AnimationPlayer?.Update(deltaTime);

        if (AnimationPlayer?.IsPlaying == true)
            SourceRect = AnimationPlayer.GetSourceRect();
    }
}
