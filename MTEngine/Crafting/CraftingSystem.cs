using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Systems;
using MTEngine.UI;
using MTEngine.World;
using MTEngine.Wounds;

namespace MTEngine.Crafting;

public class CraftingSystem : GameSystem
{
    private const string RecipeNotePrototypeId = "recipe_note";
    private const float TierOneDiscoverySkill = 72f;
    private const int NearbyIngredientTileRadius = 2;
    private const int CraftingWindowWidth = 640;
    private const int CraftingWindowHeight = 600;
    private const int CraftingRootPadding = 12;
    private const int CraftingRootGap = 8;
    private const int CardPadding = 8;
    private const int CardGap = 6;
    private const float WindowTitleScale = 0.95f;
    private const float HeaderTextScale = 0.9f;
    private const float HintTextScale = 0.8f;
    private const float CardTitleScale = 0.88f;
    private const float CardBodyScale = 0.82f;
    private const float ButtonTextScale = 0.84f;

    private sealed class CraftRecipeView
    {
        public required EntityPrototype Prototype { get; init; }
        public required CraftableComponent Craftable { get; init; }
        public required string RecipeId { get; init; }
        public QualityTierComponent? QualityTier { get; init; }
    }

    private sealed class SmeltableItemView
    {
        public required Entity ItemEntity { get; init; }
        public required ItemComponent Item { get; init; }
        public required CraftableComponent Craftable { get; init; }
        public QualityTierComponent? QualityTier { get; init; }
    }

    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private UIManager _ui = null!;
    private InputManager _input = null!;
    private XmlWindow? _window;
    private UILabel? _stationLabel;
    private UILabel? _skillLabel;
    private UILabel? _hintLabel;
    private UIScrollPanel? _rowsPanel;
    private Texture2D? _windowTexture;
    private Texture2D? _titleTexture;
    private Texture2D? _panelTexture;
    private Texture2D? _buttonTexture;

    private Entity? _actor;
    private Entity? _stationEntity;
    private CraftingStationComponent? _station;

    public override void OnInitialize()
    {
        _ui = ServiceLocator.Get<UIManager>();
        _input = ServiceLocator.Get<InputManager>();
    }

    public void OpenCrafting(Entity actor, Entity stationEntity, CraftingStationComponent station)
    {
        EnsureWindow();

        _actor = actor;
        _stationEntity = stationEntity;
        _station = station;
        if (_rowsPanel != null)
            _rowsPanel.ScrollOffset = 0;

        RebuildRows();
        _window!.Open(GetSafeOpenPosition());
    }

    public override void Update(float deltaTime)
    {
        if (_window?.IsOpen != true)
            return;

        if (_actor == null || !_actor.Active || _stationEntity == null || !_stationEntity.Active)
        {
            _window.Close();
            return;
        }

        if (!IsStationAccessible(_actor, _stationEntity))
            _window.Close();
    }

    private void EnsureWindow()
    {
        if (_window != null)
            return;

        LoadWindowTheme();

        var headerLabelHeight = GetTextLineHeight(HeaderTextScale, 4);
        var hintLabelHeight = GetTextLineHeight(HintTextScale, 6) * 2;
        var closeButtonHeight = GetTextLineHeight(ButtonTextScale, 8);
        var rowsPanelHeight = CraftingWindowHeight
            - XmlWindow.DefaultTitleBarHeight
            - (CraftingRootPadding * 2)
            - (headerLabelHeight * 2)
            - hintLabelHeight
            - closeButtonHeight
            - (CraftingRootGap * 4);

        var window = new XmlWindow
        {
            Id = "craftingWindow",
            Title = "Ремесло",
            Width = CraftingWindowWidth,
            Height = CraftingWindowHeight,
            Position = new Point(760, 60),
            TitleScale = WindowTitleScale,
            BackgroundTexture = _windowTexture,
            TitleTexture = _titleTexture,
            OverrideBackgroundColor = new Color(34, 30, 24, 232),
            OverrideTitleBarColor = new Color(72, 62, 44, 255),
            OverrideTitleTextColor = new Color(176, 222, 255),
            OverrideBorderColor = new Color(104, 90, 62)
        };

        window.Root.Direction = LayoutDirection.Vertical;
        window.Root.Padding = CraftingRootPadding;
        window.Root.Gap = CraftingRootGap;

        _stationLabel = window.AddElement(new UILabel
        {
            Name = "stationLabel",
            Height = headerLabelHeight,
            Scale = HeaderTextScale,
            Color = new Color(186, 214, 235)
        });

        _skillLabel = window.AddElement(new UILabel
        {
            Name = "skillLabel",
            Height = headerLabelHeight,
            Scale = HeaderTextScale,
            Color = new Color(170, 214, 160)
        });

        _hintLabel = window.AddElement(new UILabel
        {
            Name = "hintLabel",
            Height = hintLabelHeight,
            Scale = HintTextScale,
            Color = new Color(176, 170, 154)
        });

        _rowsPanel = window.AddElement(new UIScrollPanel
        {
            Name = "rowsPanel",
            Height = Math.Max(120, rowsPanelHeight),
            BackColor = new Color(16, 16, 14, 210),
            BackgroundTexture = _windowTexture,
            BackgroundTint = new Color(22, 20, 16, 220),
            BorderColor = new Color(90, 82, 60),
            Padding = CardPadding,
            Gap = CardGap,
            ScrollBarColor = new Color(126, 114, 82, 210)
        });

        var closeButton = window.AddElement(new UIButton
        {
            Text = "Закрыть",
            Height = closeButtonHeight,
            TextScale = ButtonTextScale,
            BackgroundTexture = _buttonTexture,
            BackColor = new Color(78, 92, 62),
            HoverColor = new Color(102, 118, 78),
            PressColor = new Color(62, 72, 50),
            BorderColor = new Color(128, 146, 100)
        });
        closeButton.OnClick += () => _window?.Close();
        window.OnClosed += ClearContext;

        _ui.RegisterWindow(window);
        _window = window;
    }

    private void RebuildRows()
    {
        if (_window == null || _rowsPanel == null || _stationLabel == null || _skillLabel == null || _hintLabel == null)
            return;

        _rowsPanel.Clear();
        if (_actor == null || _station == null)
            return;

        var skillType = _station.SkillForStation();
        var skills = _actor.GetComponent<SkillComponent>();
        var currentSkill = skills?.GetSkill(skillType) ?? 0f;

        _stationLabel.Text = $"Станция: {_station.StationName}";
        _skillLabel.Text = $"Навык: {GetSkillTitle(skillType)} {currentSkill:0}";

        if (IsSmeltingStation(_station))
        {
            RebuildSmeltingRows(currentSkill);
            return;
        }

        var allRecipes = GetRecipesForStation(_station.StationId)
            .OrderBy(recipe => recipe.QualityTier?.Tier ?? recipe.Craftable.RecipeTier)
            .ThenBy(recipe => recipe.Craftable.RequiredSkill)
            .ThenBy(recipe => recipe.Prototype.Name)
            .ToList();

        var knownRecipes = _actor.GetComponent<KnownRecipesComponent>();
        var visibleRecipes = allRecipes
            .Where(recipe => !recipe.Craftable.RequiresRecipe || (knownRecipes?.Knows(recipe.RecipeId) ?? false))
            .ToList();

        var hiddenCount = allRecipes.Count - visibleRecipes.Count;
        _hintLabel.Text = hiddenCount > 0
            ? $"Известно рецептов: {visibleRecipes.Count}. Скрыто: {hiddenCount}."
            : $"Известно рецептов: {visibleRecipes.Count}.";

        if (visibleRecipes.Count == 0)
        {
            _rowsPanel.Add(CreateLabel(
                _station.StationId.Equals("smithing", StringComparison.OrdinalIgnoreCase)
                    ? "Пока нет известных кузнечных рецептов. Изучай их через переплавку оружия."
                    : "Пока нет известных рецептов для этой станции.",
                Color.Gray,
                GetTextLineHeight(CardBodyScale, 6) * 2,
                CardBodyScale));
            return;
        }

        foreach (var recipe in visibleRecipes)
            _rowsPanel.Add(BuildRecipeRow(recipe, currentSkill));
    }

    private void RebuildSmeltingRows(float currentSkill)
    {
        if (_rowsPanel == null || _hintLabel == null || _actor == null)
            return;

        var smeltables = GetSmeltableItems(_actor)
            .OrderBy(item => item.QualityTier?.Tier ?? item.Craftable.RecipeTier)
            .ThenBy(item => item.Item.ItemName)
            .ToList();

        _hintLabel.Text = smeltables.Count == 0
            ? "Нечего переплавлять. Нужны кузнечные мечи при себе."
            : $"Можно переплавить: {smeltables.Count}. Тир 1 требует {TierOneDiscoverySkill:0}+ кузнечного дела для находки рецептов.";

        if (smeltables.Count == 0)
        {
            _rowsPanel.Add(CreateLabel("Подходящих мечей при себе нет.", Color.Gray, GetTextLineHeight(CardBodyScale, 6), CardBodyScale));
            return;
        }

        foreach (var smeltable in smeltables)
            _rowsPanel.Add(BuildSmeltRow(smeltable, currentSkill));
    }

    private UIPanel BuildRecipeRow(CraftRecipeView recipe, float currentSkill)
    {
        var titleHeight = GetTextLineHeight(CardTitleScale, 2);
        var bodyHeight = GetTextLineHeight(CardBodyScale, 4);
        var materialHeight = bodyHeight * 2;
        var actionHeight = GetActionRowHeight();
        var root = CreateCardPanel(new Color(36, 40, 30, 220), new Color(96, 104, 78));

        var titleRow = new UIPanel
        {
            Direction = LayoutDirection.Horizontal,
            Height = titleHeight,
            Gap = 6
        };
        titleRow.Add(CreateLabel(recipe.Prototype.Name, new Color(232, 230, 214), titleHeight, CardTitleScale, width: 228));
        titleRow.Add(CreateLabel(GetTierText(recipe), GetTierColor(recipe), titleHeight, CardTitleScale, width: 72));
        titleRow.Add(CreateLabel($"Мин: {recipe.Craftable.RequiredSkill:0}", new Color(210, 194, 146), titleHeight, CardTitleScale, width: 82));
        titleRow.Add(CreateLabel($"Работа: ~{SkillChecks.RollCraftQuality(currentSkill, recipe.Craftable.RequiredSkill) * 100f:0}%", new Color(164, 216, 152), titleHeight, CardTitleScale, width: 184));
        root.Add(titleRow);

        var ingredientSources = CollectIngredientSources(_actor!, _stationEntity);
        var ingredientText = string.Join(", ", recipe.Craftable.Ingredients.Select(ingredient =>
        {
            var have = CountItems(ingredientSources, ingredient.PrototypeId);
            return $"{ResolveEntityName(ingredient.PrototypeId)} {have}/{ingredient.Count}";
        }));
        var anyMissing = recipe.Craftable.Ingredients.Any(ingredient => CountItems(ingredientSources, ingredient.PrototypeId) < ingredient.Count);
        var ingredientColor = anyMissing ? new Color(214, 154, 138) : new Color(196, 192, 180);
        root.Add(CreateLabel($"Материалы: {ingredientText}", ingredientColor, materialHeight, CardBodyScale));

        var infoRow = new UIPanel
        {
            Direction = LayoutDirection.Horizontal,
            Height = actionHeight,
            Gap = 10
        };

        var skillEnough = currentSkill >= recipe.Craftable.RequiredSkill;
        var canCraft = !anyMissing;
        var statusText = !skillEnough
            ? "Навык пока слишком низкий."
            : canCraft ? "Все материалы при себе." : "Не хватает материалов.";
        var statusColor = !skillEnough
            ? Color.IndianRed
            : canCraft ? Color.LightGreen : Color.IndianRed;

        infoRow.Add(CreateLabel(statusText, statusColor, actionHeight, CardBodyScale, width: 430));

        var craftButton = CreateActionButton("Создать");
        craftButton.OnClick += () => CraftRecipe(recipe);
        infoRow.Add(craftButton);

        root.Add(infoRow);
        return root;
    }

    private UIPanel BuildSmeltRow(SmeltableItemView smeltable, float currentSkill)
    {
        var titleHeight = GetTextLineHeight(CardTitleScale, 2);
        var bodyHeight = GetTextLineHeight(CardBodyScale, 4);
        var discoveryHeight = bodyHeight * 2;
        var actionHeight = GetActionRowHeight();
        var root = CreateCardPanel(new Color(48, 38, 28, 220), new Color(118, 92, 66));

        var titleRow = new UIPanel
        {
            Direction = LayoutDirection.Horizontal,
            Height = titleHeight,
            Gap = 6
        };
        titleRow.Add(CreateLabel(smeltable.Item.ItemName, new Color(232, 222, 206), titleHeight, CardTitleScale, width: 250));
        titleRow.Add(CreateLabel(GetTierText(smeltable.QualityTier?.Tier ?? smeltable.Craftable.RecipeTier), GetTierColor(smeltable.QualityTier?.Tier ?? smeltable.Craftable.RecipeTier), titleHeight, CardTitleScale, width: 72));
        titleRow.Add(CreateLabel($"Навык: {currentSkill:0}", new Color(210, 194, 146), titleHeight, CardTitleScale, width: 248));
        root.Add(titleRow);

        root.Add(CreateLabel($"Возврат: {BuildRefundText(smeltable.Craftable)}", new Color(204, 198, 186), bodyHeight, CardBodyScale));

        root.Add(CreateLabel(BuildSmeltingDiscoveryHint(smeltable, currentSkill), new Color(214, 184, 122), discoveryHeight, CardBodyScale));

        var actionRow = new UIPanel
        {
            Direction = LayoutDirection.Horizontal,
            Height = actionHeight,
            Gap = 10
        };

        actionRow.Add(CreateLabel("Переплавка уничтожает меч и возвращает часть материалов.", new Color(178, 172, 160), actionHeight, CardBodyScale, width: 430));

        var button = CreateActionButton("Переплавить");
        button.BackColor = new Color(110, 88, 58);
        button.HoverColor = new Color(136, 108, 72);
        button.PressColor = new Color(86, 68, 46);
        button.BorderColor = new Color(156, 126, 84);
        button.OnClick += () => SmeltItem(smeltable);
        actionRow.Add(button);

        root.Add(actionRow);
        return root;
    }

    private UIPanel CreateCardPanel(Color backgroundTint, Color borderColor)
    {
        return new UIPanel
        {
            Direction = LayoutDirection.Vertical,
            Height = 0,
            Gap = CardGap,
            Padding = CardPadding,
            BackgroundTexture = _panelTexture,
            BackgroundTint = backgroundTint,
            BorderColor = borderColor,
            BorderThickness = 1
        };
    }

    private UILabel CreateLabel(string text, Color color, int height, float scale, int width = 0)
    {
        return new UILabel
        {
            Text = text,
            Color = color,
            Height = height,
            Width = width,
            Scale = scale
        };
    }

    private UIButton CreateActionButton(string text)
    {
        return new UIButton
        {
            Width = 132,
            Height = GetActionRowHeight(),
            Text = text,
            TextScale = ButtonTextScale,
            BackgroundTexture = _buttonTexture,
            BackColor = new Color(78, 92, 62),
            HoverColor = new Color(102, 118, 78),
            PressColor = new Color(62, 72, 50),
            BorderColor = new Color(128, 146, 100)
        };
    }

    private int GetActionRowHeight()
        => GetTextLineHeight(ButtonTextScale, 8);

    private int GetTextLineHeight(float scale, int extraPadding)
    {
        var lineSpacing = _ui.Font?.LineSpacing ?? 18;
        return Math.Max(18, (int)MathF.Ceiling(lineSpacing * scale) + extraPadding);
    }

    private void LoadWindowTheme()
    {
        if (_windowTexture != null && _titleTexture != null && _panelTexture != null && _buttonTexture != null)
            return;

        if (!ServiceLocator.Has<AssetManager>())
            return;

        var assets = ServiceLocator.Get<AssetManager>();
        _windowTexture ??= assets.LoadFromFile(Path.Combine(ContentPaths.AbsoluteTilesRoot, "Wall", "full.png"));
        _titleTexture ??= assets.LoadFromFile(Path.Combine(ContentPaths.AbsoluteTilesRoot, "Wall", "wood3.png"));
        _panelTexture ??= assets.LoadFromFile(Path.Combine(ContentPaths.AbsoluteTilesRoot, "Wall", "wood0.png"));
        _buttonTexture ??= assets.LoadFromFile(Path.Combine(ContentPaths.AbsoluteTilesRoot, "Wall", "wood1.png"));
    }

    private void CraftRecipe(CraftRecipeView recipe)
    {
        if (_actor == null || _station == null)
            return;

        if (recipe.Craftable.RequiresRecipe)
        {
            var knownRecipes = _actor.GetComponent<KnownRecipesComponent>();
            if (!(knownRecipes?.Knows(recipe.RecipeId) ?? false))
            {
                PopupTextSystem.Show(_actor, "Этот рецепт ещё не изучен.", Color.IndianRed, lifetime: 1.3f);
                RebuildRows();
                return;
            }
        }

        var skills = _actor.GetComponent<SkillComponent>();
        var currentSkill = skills?.GetSkill(recipe.Craftable.Skill) ?? 0f;
        if (currentSkill < recipe.Craftable.RequiredSkill)
        {
            PopupTextSystem.Show(_actor, "Навык слишком низкий.", Color.IndianRed, lifetime: 1.2f);
            return;
        }

        if (!TryConsumeIngredients(_actor, recipe.Craftable.Ingredients))
        {
            PopupTextSystem.Show(_actor, "Не хватает материалов.", Color.IndianRed, lifetime: 1.2f);
            RebuildRows();
            return;
        }

        var quality = SkillChecks.RollCraftQuality(currentSkill, recipe.Craftable.RequiredSkill);
        var crafted = ServiceLocator.Get<EntityFactory>().CreateFromPrototype(recipe.Prototype, GetSpawnPosition(_actor));
        if (crafted == null)
        {
            PopupTextSystem.Show(_actor, "Не удалось создать предмет.", Color.IndianRed, lifetime: 1.2f);
            return;
        }

        recipe.QualityTier?.ApplyPresentation(crafted);
        ApplyCraftQuality(crafted, quality);
        TryGiveOrDropItemToActor(_actor, crafted);

        var gain = ComputeCraftingGain(recipe.Craftable, quality);
        skills?.Improve(recipe.Craftable.Skill, gain);
        if (recipe.Craftable.Skill != SkillType.Craftsmanship)
            skills?.Improve(SkillType.Craftsmanship, gain * 0.4f);

        PopupTextSystem.Show(
            _actor,
            $"{recipe.Prototype.Name} ({GetQualityLabel(quality)})",
            quality >= 0.9f ? Color.LightGreen : Color.Khaki,
            lifetime: 1.6f);
        RebuildRows();
    }

    private void SmeltItem(SmeltableItemView smeltable)
    {
        if (_actor == null)
            return;

        var skills = _actor.GetComponent<SkillComponent>();
        var currentSmithing = skills?.GetSkill(SkillType.Smithing) ?? 0f;
        var itemName = smeltable.Item.ItemName;
        var refundIngredients = GetRefundIngredients(smeltable.Craftable);
        if (refundIngredients.Count == 0)
        {
            PopupTextSystem.Show(_actor, "В этом предмете нечего вернуть.", Color.IndianRed, lifetime: 1.3f);
            return;
        }

        DetachItem(smeltable.ItemEntity);
        smeltable.ItemEntity.World?.DestroyEntity(smeltable.ItemEntity);

        foreach (var ingredient in refundIngredients)
            SpawnIngredientRefund(_actor, ingredient);

        var gain = ComputeSmeltingGain(smeltable.Craftable, smeltable.QualityTier?.Tier ?? smeltable.Craftable.RecipeTier);
        skills?.Improve(SkillType.Smithing, gain);
        skills?.Improve(SkillType.Craftsmanship, gain * 0.3f);

        var note = TryCreateDiscoveredRecipeNote(_actor, smeltable, currentSmithing);
        var popupText = note == null
            ? $"Переплавлено: {itemName}"
            : $"Переплавлено: {itemName}. Найдена запись.";
        PopupTextSystem.Show(_actor, popupText, new Color(214, 184, 122), lifetime: 1.7f);
        MarkWorldDirty();
        RebuildRows();
    }

    private Entity? TryCreateDiscoveredRecipeNote(Entity actor, SmeltableItemView smeltable, float currentSmithing)
    {
        if (string.IsNullOrWhiteSpace(smeltable.Craftable.RecipeFamily) || smeltable.Craftable.RecipeTier <= 0)
            return null;

        if (smeltable.Craftable.RecipeTier == 1 && currentSmithing < TierOneDiscoverySkill)
            return null;

        var candidates = GetDiscoverableRecipeCandidates(actor, smeltable.Craftable.RecipeFamily, smeltable.Craftable.RecipeTier);
        if (candidates.Count == 0)
            return null;

        var selected = candidates[Random.Shared.Next(candidates.Count)];
        var note = CreateRecipeNote(actor, selected);
        if (note == null)
            return null;

        TryGiveOrDropItemToActor(actor, note);
        return note;
    }

    private List<CraftRecipeView> GetDiscoverableRecipeCandidates(Entity actor, string family, int tier)
    {
        var knownRecipes = actor.GetComponent<KnownRecipesComponent>();
        return GetRecipesForStation("smithing")
            .Where(recipe => recipe.Craftable.DiscoverableBySmelting)
            .Where(recipe => recipe.Craftable.RequiresRecipe)
            .Where(recipe => recipe.Craftable.RecipeTier == tier)
            .Where(recipe => string.Equals(recipe.Craftable.RecipeFamily, family, StringComparison.OrdinalIgnoreCase))
            .Where(recipe => !(knownRecipes?.Knows(recipe.RecipeId) ?? false))
            .ToList();
    }

    private Entity? CreateRecipeNote(Entity actor, CraftRecipeView recipe)
    {
        var prototypes = ServiceLocator.Get<PrototypeManager>();
        var noteProto = prototypes.GetEntity(RecipeNotePrototypeId);
        if (noteProto == null)
            return null;

        var note = ServiceLocator.Get<EntityFactory>().CreateFromPrototype(noteProto, GetSpawnPosition(actor));
        if (note == null)
            return null;

        var recipeNote = note.GetComponent<RecipeNoteComponent>();
        if (recipeNote == null)
            return note;

        recipeNote.RecipeId = recipe.RecipeId;
        recipeNote.RecipeTitle = recipe.Prototype.Name;
        recipeNote.ReadSkill = recipe.Craftable.Skill;
        recipeNote.ReadRequiredSkill = MathF.Round(recipe.Craftable.RequiredSkill * 0.4f);
        recipeNote.RefreshPresentation();
        return note;
    }

    private static void ApplyCraftQuality(Entity crafted, float quality)
    {
        crafted.AddComponent(new CraftQualityComponent
        {
            Value = quality,
            Label = GetQualityLabel(quality)
        });

        if (crafted.GetComponent<WeaponComponent>() is { } weapon)
        {
            weapon.Damage *= quality;
            weapon.MinDamage *= quality;
            weapon.MaxDamage *= quality;
            weapon.BlockBonus *= MathHelper.Clamp(0.85f + quality * 0.2f, 0.4f, 1.2f);
        }

        if (crafted.GetComponent<WearableComponent>() is { } wearable)
        {
            wearable.SlashArmor *= quality;
            wearable.BluntArmor *= quality;
            wearable.BurnArmor *= quality;
            wearable.MaxDurability *= MathHelper.Clamp(0.75f + quality * 0.35f, 0.4f, 1.4f);
            wearable.Durability = wearable.MaxDurability;
        }

        if (crafted.GetComponent<HealingComponent>() is { } healing)
        {
            healing.HealAmount *= quality;
            healing.StopBleedAmount *= quality;
        }
    }

    private static string GetQualityLabel(float quality) => quality switch
    {
        < 0.6f => "Плохая работа",
        < 0.85f => "Грубая работа",
        < 1.05f => "Нормальная работа",
        < 1.18f => "Хорошая работа",
        _ => "Отличная работа"
    };

    private static string GetTierText(CraftRecipeView recipe)
        => recipe.QualityTier?.GetShortLabel() ?? GetTierText(recipe.Craftable.RecipeTier);

    private static string GetTierText(int tier)
        => tier switch
        {
            1 => "T1",
            2 => "T2",
            3 => "T3",
            4 => "T4",
            _ => "--"
        };

    private static Color GetTierColor(CraftRecipeView recipe)
        => recipe.QualityTier?.Tint ?? GetTierColor(recipe.Craftable.RecipeTier);

    private static Color GetTierColor(int tier)
        => tier switch
        {
            1 => new Color(217, 182, 74),
            2 => new Color(143, 167, 197),
            3 => new Color(191, 198, 207),
            4 => new Color(143, 106, 69),
            _ => Color.Gray
        };

    private string BuildSmeltingDiscoveryHint(SmeltableItemView smeltable, float currentSkill)
    {
        if (_actor == null)
            return "Некому изучать переплавку.";

        var family = smeltable.Craftable.RecipeFamily;
        var tier = smeltable.Craftable.RecipeTier;
        if (string.IsNullOrWhiteSpace(family) || tier <= 0)
            return "У этого предмета нет изучаемых схем.";

        if (tier == 1 && currentSkill < TierOneDiscoverySkill)
            return $"Тир 1 требует {TierOneDiscoverySkill:0}+ кузнечного дела.";

        var candidates = GetDiscoverableRecipeCandidates(_actor, family, tier);
        if (candidates.Count == 0)
            return "Новых рецептов этого тира больше нет.";

        return $"Можно вытащить рецепт: {candidates.Count} вариант(ов) этого тира.";
    }

    private static string BuildRefundText(CraftableComponent craftable)
    {
        var refunds = GetRefundIngredients(craftable);
        if (refunds.Count == 0)
            return "ничего";

        return string.Join(", ", refunds.Select(ingredient => $"{ResolveEntityName(ingredient.PrototypeId)} x{ingredient.Count}"));
    }

    private static List<CraftIngredient> GetRefundIngredients(CraftableComponent craftable)
    {
        var refund = new List<CraftIngredient>();
        foreach (var ingredient in craftable.Ingredients)
        {
            var count = (int)MathF.Ceiling(ingredient.Count * 0.5f);
            if (count <= 0)
                continue;

            refund.Add(new CraftIngredient
            {
                PrototypeId = ingredient.PrototypeId,
                Count = count
            });
        }

        return refund;
    }

    private void SpawnIngredientRefund(Entity actor, CraftIngredient ingredient)
    {
        if (ingredient.Count <= 0)
            return;

        var prototypes = ServiceLocator.Get<PrototypeManager>();
        var proto = prototypes.GetEntity(ingredient.PrototypeId);
        if (proto == null)
            return;

        var remaining = ingredient.Count;
        while (remaining > 0)
        {
            var entity = ServiceLocator.Get<EntityFactory>().CreateFromPrototype(proto, GetSpawnPosition(actor));
            if (entity == null)
                return;

            var item = entity.GetComponent<ItemComponent>();
            if (item?.Stackable == true)
            {
                var stackCount = Math.Min(remaining, Math.Max(1, item.MaxStack));
                item.StackCount = stackCount;
                remaining -= stackCount;
            }
            else
            {
                remaining--;
            }

            TryGiveOrDropItemToActor(actor, entity);
        }
    }

    private static bool TryGiveOrDropItemToActor(Entity actor, Entity itemEntity)
    {
        var hands = actor.GetComponent<HandsComponent>();
        if (hands?.TryPickUp(itemEntity) == true)
            return true;

        foreach (var storage in CollectAccessibleStorages(actor))
        {
            if (storage.Owner == itemEntity || !storage.CanInsert(itemEntity))
                continue;

            if (storage.TryInsert(itemEntity))
                return true;
        }

        return false;
    }

    private static IEnumerable<StorageComponent> CollectAccessibleStorages(Entity actor)
    {
        foreach (var entity in CollectAccessibleItems(actor))
        {
            if (entity.GetComponent<StorageComponent>() is { } storage)
                yield return storage;
        }
    }

    private List<CraftRecipeView> GetRecipesForStation(string stationId)
    {
        var result = new List<CraftRecipeView>();
        var prototypes = ServiceLocator.Get<PrototypeManager>();

        foreach (var proto in prototypes.GetAllEntities())
        {
            if (proto.Components?["craftable"]?.AsObject() is not { } node)
                continue;

            var craftable = ComponentPrototypeSerializer.Deserialize(typeof(CraftableComponent), node) as CraftableComponent;
            if (craftable == null || !string.Equals(craftable.StationId, stationId, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new CraftRecipeView
            {
                Prototype = proto,
                Craftable = craftable,
                RecipeId = craftable.ResolveRecipeId(proto.Id),
                QualityTier = TryReadQualityTier(proto)
            });
        }

        return result;
    }

    private static QualityTierComponent? TryReadQualityTier(EntityPrototype proto)
    {
        if (proto.Components?["qualityTier"]?.AsObject() is not { } node)
            return null;

        return ComponentPrototypeSerializer.Deserialize(typeof(QualityTierComponent), node) as QualityTierComponent;
    }

    private static List<SmeltableItemView> GetSmeltableItems(Entity actor)
    {
        return CollectAccessibleItems(actor)
            .Where(entity => entity != actor)
            .Where(entity => entity.GetComponent<ItemComponent>() != null)
            .Where(entity => entity.GetComponent<WeaponComponent>() != null)
            .Select(entity => new
            {
                Entity = entity,
                Item = entity.GetComponent<ItemComponent>(),
                Craftable = entity.GetComponent<CraftableComponent>(),
                Quality = entity.GetComponent<QualityTierComponent>()
            })
            .Where(entry => entry.Item != null && entry.Craftable != null)
            .Where(entry => entry.Craftable!.Skill == SkillType.Smithing && entry.Craftable.Ingredients.Count > 0)
            .Select(entry => new SmeltableItemView
            {
                ItemEntity = entry.Entity,
                Item = entry.Item!,
                Craftable = entry.Craftable!,
                QualityTier = entry.Quality
            })
            .ToList();
    }

    private static string ResolveEntityName(string prototypeId)
    {
        var proto = ServiceLocator.Get<PrototypeManager>().GetEntity(prototypeId);
        return proto?.Name ?? prototypeId;
    }

    private bool HasIngredients(Entity actor, IReadOnlyList<CraftIngredient> ingredients)
    {
        var sources = CollectIngredientSources(actor, _stationEntity);
        return ingredients.All(ingredient => CountItems(sources, ingredient.PrototypeId) >= ingredient.Count);
    }

    private static int CountItems(IEnumerable<Entity> inventory, string prototypeId)
    {
        var total = 0;
        foreach (var entity in inventory)
        {
            if (!string.Equals(entity.PrototypeId, prototypeId, StringComparison.OrdinalIgnoreCase))
                continue;

            var item = entity.GetComponent<ItemComponent>();
            total += item?.Stackable == true ? item.StackCount : 1;
        }

        return total;
    }

    private bool TryConsumeIngredients(Entity actor, IReadOnlyList<CraftIngredient> ingredients)
    {
        if (!HasIngredients(actor, ingredients))
            return false;

        foreach (var ingredient in ingredients)
        {
            var remaining = ingredient.Count;
            foreach (var entity in CollectIngredientSources(actor, _stationEntity)
                .Where(item => string.Equals(item.PrototypeId, ingredient.PrototypeId, StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                if (remaining <= 0)
                    break;

                remaining -= ConsumeIngredientEntity(entity, remaining);
            }
        }

        return true;
    }

    private static int ConsumeIngredientEntity(Entity entity, int amountNeeded)
    {
        var item = entity.GetComponent<ItemComponent>();
        if (item?.Stackable == true && item.StackCount > amountNeeded)
        {
            item.StackCount -= amountNeeded;
            MarkWorldDirty();
            return amountNeeded;
        }

        var consumed = item?.Stackable == true ? item.StackCount : 1;
        DetachItem(entity);
        entity.World?.DestroyEntity(entity);
        MarkWorldDirty();
        return consumed;
    }

    private static void DetachItem(Entity entity)
    {
        var item = entity.GetComponent<ItemComponent>();
        var container = item?.ContainedIn;

        container?.GetComponent<StorageComponent>()?.Contents.Remove(entity);
        container?.GetComponent<HandsComponent>()?.RemoveFromHand(entity);
        container?.GetComponent<EquipmentComponent>()?.RemoveEquipped(entity);

        if (item != null)
            item.ContainedIn = null;
    }

    private static List<Entity> CollectIngredientSources(Entity actor, Entity? stationEntity)
    {
        var seen = new HashSet<int>();
        var result = new List<Entity>();

        foreach (var entity in CollectAccessibleItems(actor))
        {
            if (seen.Add(entity.Id))
                result.Add(entity);
        }

        if (stationEntity == null || !stationEntity.Active)
            return result;

        var stationTf = stationEntity.GetComponent<TransformComponent>();
        if (stationTf == null)
            return result;

        var world = stationEntity.World ?? actor.World;
        if (world == null)
            return result;

        var tileSize = ServiceLocator.Has<MapManager>()
            ? ServiceLocator.Get<MapManager>().CurrentMap?.TileSize ?? 32
            : 32;
        var radius = NearbyIngredientTileRadius * tileSize;
        var radiusSq = radius * radius;

        foreach (var entity in world.GetEntitiesWith<TransformComponent, ItemComponent>())
        {
            if (!entity.Active || entity == actor || entity == stationEntity)
                continue;

            var item = entity.GetComponent<ItemComponent>();
            if (item == null || item.ContainedIn != null)
                continue;

            var tf = entity.GetComponent<TransformComponent>();
            if (tf == null)
                continue;

            if (Vector2.DistanceSquared(tf.Position, stationTf.Position) > radiusSq)
                continue;

            if (seen.Add(entity.Id))
                result.Add(entity);
        }

        return result;
    }

    private static List<Entity> CollectAccessibleItems(Entity actor)
    {
        var result = new List<Entity>();
        var seen = new HashSet<int>();

        void addRecursive(Entity? entity)
        {
            if (entity == null || !seen.Add(entity.Id))
                return;

            result.Add(entity);
            if (entity.GetComponent<StorageComponent>() is not { } storage)
                return;

            foreach (var nested in storage.Contents)
                addRecursive(nested);
        }

        if (actor.GetComponent<StorageComponent>() is { } actorStorage)
            foreach (var nested in actorStorage.Contents)
                addRecursive(nested);

        if (actor.GetComponent<HandsComponent>() is { } hands)
            foreach (var hand in hands.Hands)
                addRecursive(hand.HeldItem);

        if (actor.GetComponent<EquipmentComponent>() is { } equipment)
            foreach (var slot in equipment.Slots)
                addRecursive(slot.Item);

        return result;
    }

    private static bool IsStationAccessible(Entity actor, Entity stationEntity)
    {
        var stationItem = stationEntity.GetComponent<ItemComponent>();
        if (stationItem?.ContainedIn == actor)
            return true;

        var actorTf = actor.GetComponent<TransformComponent>();
        var stationTf = stationEntity.GetComponent<TransformComponent>();
        if (actorTf == null || stationTf == null)
            return false;

        var maxRange = stationEntity.GetComponent<InteractableComponent>()?.InteractRange ?? 80f;
        return Vector2.Distance(actorTf.Position, stationTf.Position) <= maxRange;
    }

    private Point GetSafeOpenPosition()
    {
        var mouse = _input.MousePosition;
        var x = mouse.X + 28;
        var y = mouse.Y + 20;

        if (!ServiceLocator.Has<Microsoft.Xna.Framework.Graphics.GraphicsDevice>())
            return new Point(x, y);

        var viewport = ServiceLocator.Get<Microsoft.Xna.Framework.Graphics.GraphicsDevice>().Viewport;
        x = Math.Clamp(x, 8, Math.Max(8, viewport.Width - CraftingWindowWidth - 8));
        y = Math.Clamp(y, 8, Math.Max(8, viewport.Height - CraftingWindowHeight - 8));
        return new Point(x, y);
    }

    private static bool IsSmeltingStation(CraftingStationComponent station)
        => station.StationId.Equals("smelting", StringComparison.OrdinalIgnoreCase);

    private static string GetSkillTitle(SkillType skill) => skill switch
    {
        SkillType.Smithing => "Кузнечное дело",
        SkillType.Tailoring => "Шитьё",
        SkillType.Medicine => "Медицина",
        SkillType.Thievery => "Воровство",
        SkillType.Social => "Социалка",
        SkillType.Trade => "Торговля",
        _ => "Ремесло"
    };

    private void ClearContext()
    {
        _actor = null;
        _stationEntity = null;
        _station = null;
    }

    private static Vector2 GetSpawnPosition(Entity actor)
        => actor.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;

    private static float ComputeCraftingGain(CraftableComponent craftable, float quality)
    {
        var matWeight = ComputeMaterialWeight(craftable.Ingredients);
        var complexityFactor = Math.Clamp(craftable.RequiredSkill / 80f, 0.05f, 1.5f);
        var qualityFactor = MathHelper.Clamp(0.65f + 0.55f * quality, 0.6f, 1.6f);
        var gainBase = 0.60f + 0.50f * complexityFactor + 0.30f * matWeight;
        return gainBase * qualityFactor;
    }

    private static float ComputeSmeltingGain(CraftableComponent craftable, int itemTier)
    {
        var matWeight = ComputeMaterialWeight(craftable.Ingredients);
        var tierBoost = Math.Max(0, 5 - itemTier) * 0.10f;
        var complexityFactor = Math.Clamp(craftable.RequiredSkill / 100f, 0.0f, 1.0f);
        return 0.50f + 0.20f * matWeight + 0.20f * complexityFactor + tierBoost;
    }

    private static float ComputeMaterialWeight(IReadOnlyList<CraftIngredient> ingredients)
    {
        if (ingredients == null || ingredients.Count == 0)
            return 0f;

        var prototypes = ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        var totalCount = 0;
        var weightedSum = 0f;

        foreach (var ingredient in ingredients)
        {
            if (ingredient == null || ingredient.Count <= 0)
                continue;

            var tier = LookupIngredientTier(prototypes, ingredient.PrototypeId);
            var weight = Math.Max(0, 5 - tier);
            weightedSum += weight * ingredient.Count;
            totalCount += ingredient.Count;
        }

        return totalCount > 0 ? weightedSum / totalCount : 0f;
    }

    private static int LookupIngredientTier(PrototypeManager? prototypes, string prototypeId)
    {
        if (prototypes == null || string.IsNullOrWhiteSpace(prototypeId))
            return 5;

        var proto = prototypes.GetEntity(prototypeId);
        if (proto?.Components?["qualityTier"]?.AsObject() is not { } node)
            return 5;

        if (node["tier"] is { } tierNode && int.TryParse(tierNode.ToString(), out var tier))
            return tier;

        return 5;
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}

internal static class CraftingStationSkillExtensions
{
    public static SkillType SkillForStation(this CraftingStationComponent station)
        => station.StationId.Trim().ToLowerInvariant() switch
        {
            "medicine" => SkillType.Medicine,
            "smithing" => SkillType.Smithing,
            "smelting" => SkillType.Smithing,
            "tailoring" => SkillType.Tailoring,
            _ => SkillType.Craftsmanship
        };
}
