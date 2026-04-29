using System.IO;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Npc;

/// <summary>
/// Хранит пути к мужскому/женскому варианту базового спрайта актёра.
/// Спаунер NPC, проставив пол через <see cref="IdentityComponent"/>, вызывает
/// <see cref="ApplyForGender"/>, и компонент подменяет текстуру и набор анимаций
/// в существующем <see cref="SpriteComponent"/> на нужный гендерный вариант.
///
/// Пути относительные к папке прототипа.
/// </summary>
[RegisterComponent("genderedAppearance")]
public class GenderedAppearanceComponent : Component, IPrototypeInitializable
{
    [DataField("maleSprite")]
    public string? MaleSprite { get; set; }

    [DataField("femaleSprite")]
    public string? FemaleSprite { get; set; }

    [DataField("maleAnimations")]
    public string? MaleAnimations { get; set; }

    [DataField("femaleAnimations")]
    public string? FemaleAnimations { get; set; }

    private string? _directoryPath;
    private AssetManager? _assets;

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        _directoryPath = proto.DirectoryPath;
        _assets = assets;
    }

    public void ApplyForGender(Gender gender)
    {
        if (Owner == null || _assets == null || string.IsNullOrWhiteSpace(_directoryPath))
            return;

        var sprite = Owner.GetComponent<SpriteComponent>();
        if (sprite == null)
            return;

        var spritePath = gender == Gender.Female ? FemaleSprite : MaleSprite;
        var animPath = gender == Gender.Female ? FemaleAnimations : MaleAnimations;

        if (!string.IsNullOrWhiteSpace(spritePath))
        {
            var fullSpritePath = Path.Combine(_directoryPath, spritePath);
            var tex = _assets.LoadFromFile(fullSpritePath);
            if (tex != null)
            {
                sprite.Texture = tex;
                sprite.Source = spritePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(animPath))
        {
            var fullAnimPath = Path.Combine(_directoryPath, animPath);
            var animSet = AnimationSet.LoadFromFile(fullAnimPath);
            if (animSet != null)
            {
                if (!string.IsNullOrEmpty(animSet.TexturePath))
                {
                    var animTexPath = Path.Combine(_directoryPath, animSet.TexturePath);
                    var animTex = _assets.LoadFromFile(animTexPath);
                    if (animTex != null)
                    {
                        sprite.Texture = animTex;
                        animSet.TexturePath = animTexPath;
                    }
                }

                sprite.SetAnimations(animSet, "idle_down");
                if (sprite.AnimationPlayer?.IsPlaying == true)
                    sprite.SourceRect = sprite.AnimationPlayer.GetSourceRect();
            }
        }
    }
}
