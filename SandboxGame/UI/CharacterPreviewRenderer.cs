#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Npc;
using MTEngine.Rendering;

namespace SandboxGame.UI;

/// <summary>
/// Рисует живой превью персонажа в окне создания: применяет draft к существующему player-entity
/// и отрисовывает body-спрайт + волосы по текущему idle-кадру с цветовыми тинтами.
/// </summary>
public sealed class CharacterPreviewRenderer
{
    private readonly Func<Entity?> _playerProvider;
    private readonly Action<Entity, PlayerCharacterDraft> _applyDraft;
    private readonly Func<IReadOnlyDictionary<string, string>> _startingOutfitProvider;
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;

    public CharacterPreviewRenderer(
        Func<Entity?> playerProvider,
        Action<Entity, PlayerCharacterDraft> applyDraft,
        Func<IReadOnlyDictionary<string, string>> startingOutfitProvider,
        PrototypeManager prototypes,
        AssetManager assets)
    {
        _playerProvider = playerProvider;
        _applyDraft = applyDraft;
        _startingOutfitProvider = startingOutfitProvider;
        _prototypes = prototypes;
        _assets = assets;
    }

    public bool Render(SpriteBatch spriteBatch, Rectangle rect, PlayerCharacterDraft draft, Vector2 facing)
    {
        var player = _playerProvider();
        if (player == null)
            return RenderFromPrototype(spriteBatch, rect, draft, facing);

        _applyDraft(player, draft);

        var sprite = player.GetComponent<SpriteComponent>();
        if (sprite?.Texture == null)
            return false;

        // Выбираем idle-анимацию по направлению, чтобы превью можно было поворачивать.
        sprite.PlayDirectionalIdle(facing == Vector2.Zero ? new Vector2(0, 1) : facing);
        if (sprite.AnimationPlayer?.IsPlaying == true)
            sprite.SourceRect = sprite.AnimationPlayer.GetSourceRect();

        var src = sprite.SourceRect ?? new Rectangle(0, 0, sprite.Width, sprite.Height);
        var scale = ChooseIntegerScale(rect, src);
        var center = new Vector2(rect.Center.X, rect.Center.Y);

        DrawAt(spriteBatch, sprite.Texture, src, sprite.Origin, sprite.Color, center, scale);
        DrawStartingOutfit(spriteBatch, player, draft, center, scale);

        var hair = player.GetComponent<HairAppearanceComponent>();
        if (hair != null)
        {
            // Синхронизируем причёску с текущим idle-клипом тела.
            hair.SyncAnimation(_prototypes, _assets, player, sprite, 0f);
            if (hair.TryGetDrawData(_prototypes, _assets, player, out var hd))
                DrawAt(spriteBatch, hd.Texture, hd.SourceRect, hd.Origin, hd.Color, center + hd.Offset * scale, scale);
        }

        return true;
    }

    private bool RenderFromPrototype(SpriteBatch spriteBatch, Rectangle rect, PlayerCharacterDraft draft, Vector2 facing)
    {
        var proto = _prototypes.GetEntity("player");
        if (proto?.Components?["sprite"] is not JsonObject spriteData)
            return false;

        var source = ReadString(spriteData, "source");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(proto.DirectoryPath))
            return false;

        var texture = _assets.LoadFromFile(Path.Combine(proto.DirectoryPath, source));
        if (texture == null)
            return false;

        var clip = ResolveIdleClip(facing);
        var src = ResolveAnimationFrame(proto, clip)
                  ?? new Rectangle(
                      ReadInt(spriteData, "srcX", 0),
                      ReadInt(spriteData, "srcY", 0),
                      ReadInt(spriteData, "width", 32),
                      ReadInt(spriteData, "height", 32));
        var origin = new Vector2(src.Width / 2f, src.Height / 2f);
        var scale = ChooseIntegerScale(rect, src);
        var center = new Vector2(rect.Center.X, rect.Center.Y);
        var skin = AssetManager.ParseHexColor(draft.SkinColor, Color.White);

        DrawAt(spriteBatch, texture, src, origin, skin, center, scale);
        DrawStartingOutfit(spriteBatch, null, draft, center, scale, clip);
        DrawHairFromPrototype(spriteBatch, rect, draft, clip, center, scale);
        return true;
    }

    private void DrawStartingOutfit(SpriteBatch spriteBatch, Entity? owner, PlayerCharacterDraft draft, Vector2 center, float scale, string? fallbackClip = null)
    {
        var outfit = _startingOutfitProvider();
        foreach (var slotId in new[] { "torso", "pants", "shoes", "back" })
        {
            if (!outfit.TryGetValue(slotId, out var prototypeId) || string.IsNullOrWhiteSpace(prototypeId))
                continue;

            DrawWearableFromPrototype(spriteBatch, prototypeId, owner, draft, center, scale, fallbackClip);
        }
    }

    private void DrawWearableFromPrototype(SpriteBatch spriteBatch, string prototypeId, Entity? owner, PlayerCharacterDraft draft, Vector2 center, float scale, string? fallbackClip)
    {
        var proto = _prototypes.GetEntity(prototypeId);
        if (proto?.Components?["wearable"] is not JsonObject wearable || string.IsNullOrWhiteSpace(proto.DirectoryPath))
            return;

        var female = string.Equals(draft.Gender, "Female", StringComparison.OrdinalIgnoreCase);
        var animSource = female
            ? FirstNonEmpty(ReadString(wearable, "equippedAnimationsFemale"), ReadString(wearable, "equippedAnimations"))
            : FirstNonEmpty(ReadString(wearable, "equippedAnimationsMale"), ReadString(wearable, "equippedAnimations"));
        var spriteSource = female
            ? FirstNonEmpty(ReadString(wearable, "equippedSpriteFemale"), ReadString(wearable, "equippedSprite"))
            : FirstNonEmpty(ReadString(wearable, "equippedSpriteMale"), ReadString(wearable, "equippedSprite"));

        var clip = owner != null ? ResolveIdleClip(FacingFromOwner(owner)) : FirstNonEmpty(fallbackClip ?? "", "idle_down");
        var animPath = ResolveFile(proto, animSource);
        var textureSource = ResolveAnimationTexturePath(animPath) ?? spriteSource;
        if (string.IsNullOrWhiteSpace(textureSource))
            return;

        var texture = _assets.LoadFromFile(Path.Combine(proto.DirectoryPath, textureSource));
        if (texture == null)
            return;

        var src = ResolveAnimationFrame(animPath, clip)
                  ?? new Rectangle(
                      ReadInt(wearable, "equippedSrcX", 0),
                      ReadInt(wearable, "equippedSrcY", 0),
                      ReadInt(wearable, "equippedWidth", 32),
                      ReadInt(wearable, "equippedHeight", 32));
        var color = AssetManager.ParseHexColor(
            ReadString(wearable, "color"),
            AssetManager.ParseHexColor(ReadString(proto.Components?["sprite"] as JsonObject, "color"), Color.White));

        DrawAt(spriteBatch, texture, src, new Vector2(src.Width / 2f, src.Height / 2f), color, center, scale);
    }

    private void DrawHairFromPrototype(SpriteBatch spriteBatch, Rectangle rect, PlayerCharacterDraft draft, string clip, Vector2 center, float scale)
    {
        if (string.IsNullOrWhiteSpace(draft.HairStyleId))
            return;

        var styleProto = _prototypes.GetEntity(draft.HairStyleId);
        if (styleProto?.Components?["hairStyle"] is not JsonObject style || string.IsNullOrWhiteSpace(styleProto.DirectoryPath))
            return;

        var animPath = ResolveFile(styleProto, ReadString(style, "animations"));
        var textureSource = ResolveAnimationTexturePath(animPath) ?? ReadString(style, "sprite");
        if (string.IsNullOrWhiteSpace(textureSource))
            return;

        var texture = _assets.LoadFromFile(Path.Combine(styleProto.DirectoryPath, textureSource));
        if (texture == null)
            return;

        var src = ResolveAnimationFrame(animPath, clip)
                  ?? new Rectangle(
                      ReadInt(style, "srcX", 0),
                      ReadInt(style, "srcY", 0),
                      ReadInt(style, "width", 32),
                      ReadInt(style, "height", 32));
        var origin = new Vector2(
            ReadFloat(style, "originX", src.Width / 2f),
            ReadFloat(style, "originY", src.Height / 2f));
        var offset = new Vector2(
            ReadFloat(style, "offsetX", 0f),
            ReadFloat(style, "offsetY", 0f));
        var color = AssetManager.ParseHexColor(draft.HairColor, new Color(76, 49, 31));
        DrawAt(spriteBatch, texture, src, origin, color, center + offset * scale, scale);
    }

    private static float ChooseIntegerScale(Rectangle rect, Rectangle src)
    {
        if (src.Width <= 0 || src.Height <= 0)
            return 1f;

        var sx = rect.Width * 0.82f / src.Width;
        var sy = rect.Height * 0.82f / src.Height;
        var s = MathF.Min(sx, sy);
        return MathF.Max(1f, MathF.Floor(s));
    }

    private static string ResolveIdleClip(Vector2 facing)
    {
        if (Math.Abs(facing.X) > Math.Abs(facing.Y))
            return facing.X < 0 ? "idle_left" : "idle_right";

        return facing.Y < 0 ? "idle_up" : "idle_down";
    }

    private static Vector2 FacingFromOwner(Entity? owner)
    {
        var sprite = owner?.GetComponent<SpriteComponent>();
        var clip = sprite?.AnimationPlayer?.CurrentClipName ?? "";
        return clip switch
        {
            "idle_left" => new Vector2(-1, 0),
            "idle_right" => new Vector2(1, 0),
            "idle_up" => new Vector2(0, -1),
            _ => new Vector2(0, 1)
        };
    }

    private static Rectangle? ResolveAnimationFrame(EntityPrototype proto, string clip)
        => ResolveAnimationFrame(proto.AnimationsPath, clip);

    private static Rectangle? ResolveAnimationFrame(string? animationPath, string clip)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath))
            return null;

        var set = AnimationSet.LoadFromFile(animationPath);
        var frame = set?.GetClip(clip)?.Frames.FirstOrDefault();
        if (frame == null)
            frame = set?.GetClip("idle_down")?.Frames.FirstOrDefault()
                    ?? set?.GetAllClips().FirstOrDefault()?.Frames.FirstOrDefault();

        return frame?.SourceRect;
    }

    private static string? ResolveAnimationTexturePath(string? animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath))
            return null;

        return AnimationSet.LoadFromFile(animationPath)?.TexturePath;
    }

    private static string ResolveFile(EntityPrototype proto, string source)
        => string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(proto.DirectoryPath)
            ? ""
            : Path.Combine(proto.DirectoryPath, source);

    private static string ReadString(JsonObject? obj, string key)
        => obj?[key]?.GetValue<string>() ?? "";

    private static int ReadInt(JsonObject obj, string key, int fallback)
        => obj[key] != null && int.TryParse(obj[key]!.ToString(), out var value) ? value : fallback;

    private static float ReadFloat(JsonObject obj, string key, float fallback)
        => obj[key] != null && float.TryParse(obj[key]!.ToString(), out var value) ? value : fallback;

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static void DrawAt(SpriteBatch sb, Texture2D tex, Rectangle src, Vector2 origin, Color color, Vector2 center, float scale)
    {
        sb.Draw(
            texture: tex,
            position: center,
            sourceRectangle: src,
            color: color,
            rotation: 0f,
            origin: origin,
            scale: scale,
            effects: SpriteEffects.None,
            layerDepth: 0f);
    }
}
