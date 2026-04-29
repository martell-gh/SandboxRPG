using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.UI;

namespace MTEngine.Combat;

[RegisterComponent("skills")]
public class SkillComponent : Component, IInteractionSource
{
    private sealed class SkillDefinition
    {
        public required string Prefix { get; init; }
        public required SkillType Type { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string TrainingHint { get; init; }
        public required Color FillColor { get; init; }
    }

    private sealed class SkillViewState
    {
        public required UIElement HoverTarget { get; init; }
        public required SkillType Type { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string TrainingHint { get; init; }
    }

    private sealed class TooltipParagraph
    {
        public required string Text { get; init; }
        public required Color Color { get; init; }
    }

    [DataField("fortitude")]
    [SaveField("fortitude")]
    public float Fortitude { get; set; } = 0f;

    [DataField("dodge")]
    [SaveField("dodge")]
    public float Dodge { get; set; } = 0f;

    [DataField("blocking")]
    [SaveField("blocking")]
    public float Blocking { get; set; } = 0f;

    [DataField("handToHand")]
    [SaveField("handToHand")]
    public float HandToHand { get; set; } = 0f;

    [DataField("oneHandedWeapons")]
    [SaveField("oneHandedWeapons")]
    public float OneHandedWeapons { get; set; } = 0f;

    [DataField("twoHandedWeapons")]
    [SaveField("twoHandedWeapons")]
    public float TwoHandedWeapons { get; set; } = 0f;

    [DataField("rangedWeapons")]
    [SaveField("rangedWeapons")]
    public float RangedWeapons { get; set; } = 0f;

    [DataField("medicine")]
    [SaveField("medicine")]
    public float Medicine { get; set; } = 0f;

    [DataField("thievery")]
    [SaveField("thievery")]
    public float Thievery { get; set; } = 0f;

    [DataField("social")]
    [SaveField("social")]
    public float Social { get; set; } = 0f;

    [DataField("trade")]
    [SaveField("trade")]
    public float Trade { get; set; } = 0f;

    [DataField("craftsmanship")]
    [SaveField("craftsmanship")]
    public float Craftsmanship { get; set; } = 0f;

    [DataField("smithing")]
    [SaveField("smithing")]
    public float Smithing { get; set; } = 0f;

    [DataField("tailoring")]
    [SaveField("tailoring")]
    public float Tailoring { get; set; } = 0f;

    public float GetSkill(SkillType type) => type switch
    {
        SkillType.Fortitude => Fortitude,
        SkillType.Dodge => Dodge,
        SkillType.Blocking => Blocking,
        SkillType.HandToHand => HandToHand,
        SkillType.OneHandedWeapons => OneHandedWeapons,
        SkillType.TwoHandedWeapons => TwoHandedWeapons,
        SkillType.RangedWeapons => RangedWeapons,
        SkillType.Medicine => Medicine,
        SkillType.Thievery => Thievery,
        SkillType.Social => Social,
        SkillType.Trade => Trade,
        SkillType.Craftsmanship => Craftsmanship,
        SkillType.Smithing => Smithing,
        SkillType.Tailoring => Tailoring,
        _ => 0f
    };

    public void Improve(SkillType type, float amount)
    {
        if (amount <= 0f)
            return;

        var current = GetSkill(type);
        var scaledAmount = amount * GetProgressionFactor(current);
        var value = Math.Clamp(current + scaledAmount, 0f, 100f);
        switch (type)
        {
            case SkillType.Fortitude:
                Fortitude = value;
                break;
            case SkillType.Dodge:
                Dodge = value;
                break;
            case SkillType.Blocking:
                Blocking = value;
                break;
            case SkillType.HandToHand:
                HandToHand = value;
                break;
            case SkillType.OneHandedWeapons:
                OneHandedWeapons = value;
                break;
            case SkillType.TwoHandedWeapons:
                TwoHandedWeapons = value;
                break;
            case SkillType.RangedWeapons:
                RangedWeapons = value;
                break;
            case SkillType.Medicine:
                Medicine = value;
                break;
            case SkillType.Thievery:
                Thievery = value;
                break;
            case SkillType.Social:
                Social = value;
                break;
            case SkillType.Trade:
                Trade = value;
                break;
            case SkillType.Craftsmanship:
                Craftsmanship = value;
                break;
            case SkillType.Smithing:
                Smithing = value;
                break;
            case SkillType.Tailoring:
                Tailoring = value;
                break;
        }
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (ctx.Target != Owner)
            yield break;

        yield return new InteractionEntry
        {
            Id = "skills.view",
            Label = "Навыки",
            Priority = 6,
            InterruptsCurrentAction = false,
            Execute = c => ToggleSkillsWindow(c.Target)
        };
    }

    public static void ToggleSkillsWindow(Entity target)
    {
        var ui = ServiceLocator.Get<UIManager>();
        const string windowId = "skillsView";

        var existing = ui.GetWindow(windowId);
        if (existing != null)
        {
            if (existing.IsOpen)
            {
                existing.Close();
                return;
            }

            ui.RemoveWindow(existing);
        }

        var path = Path.Combine("SandboxGame", "Content", "UI", "SkillsWindow.xml");
        if (!File.Exists(path))
            return;

        var skills = target.GetComponent<SkillComponent>();
        if (skills == null)
            return;

        var window = ui.LoadWindow(path);
        window.Title = $"Навыки: {target.Name}";

        var definitions = CreateSkillDefinitions();
        var bindings = new List<SkillViewState>();
        foreach (var definition in definitions)
            BindSkillRow(window, definition, bindings);
        RefreshSkillRows(window, skills, definitions);

        SkillViewState? hoveredSkill = null;
        Rectangle tooltipRect = Rectangle.Empty;

        window.OnUpdate += _ =>
        {
            RefreshSkillRows(window, skills, definitions);

            var mouse = GetUiMousePoint();
            hoveredSkill = bindings.FirstOrDefault(binding => binding.HoverTarget.Visible && binding.HoverTarget.Bounds.Contains(mouse));
            if (hoveredSkill == null)
            {
                tooltipRect = Rectangle.Empty;
                return;
            }

            const int offset = 14;
            const int panelWidth = 260;
            const int panelHeight = 88;
            var x = mouse.X + offset;
            var y = mouse.Y + offset;
            if (ServiceLocator.Has<GraphicsDevice>())
            {
                var uiBounds = GameEngine.Instance.GetUiLogicalBounds(GetUiScale());
                var uiViewportWidth = uiBounds.Width;
                var uiViewportHeight = uiBounds.Height;
                x = Math.Min(x, uiViewportWidth - panelWidth - 8);
                y = Math.Min(y, uiViewportHeight - panelHeight - 8);
            }

            tooltipRect = new Rectangle(x, y, panelWidth, panelHeight);
        };

        window.OnDrawOverlay += (sb, pixel, font) =>
        {
            if (hoveredSkill == null || tooltipRect == Rectangle.Empty)
                return;

            const float bodyScale = 0.7f;
            const int paragraphGap = 6;
            const int lineHeight = 12;
            var paragraphs = BuildTooltipParagraphs(skills, hoveredSkill);
            var bodyHeight = MeasureTooltipBodyHeight(font, paragraphs, tooltipRect.Width - 16, bodyScale, lineHeight, paragraphGap);
            var drawRect = new Rectangle(tooltipRect.X, tooltipRect.Y, tooltipRect.Width, 28 + bodyHeight + 8);

            if (ServiceLocator.Has<GraphicsDevice>())
            {
                var uiViewportHeight = GameEngine.Instance.GetUiLogicalBounds(GetUiScale()).Height;
                if (drawRect.Bottom > uiViewportHeight - 8)
                    drawRect.Y = Math.Max(8, uiViewportHeight - drawRect.Height - 8);
            }

            sb.Draw(pixel, drawRect, new Color(15, 18, 22, 235));
            sb.Draw(pixel, new Rectangle(drawRect.X, drawRect.Y, drawRect.Width, 1), new Color(92, 122, 92));
            sb.Draw(pixel, new Rectangle(drawRect.X, drawRect.Bottom - 1, drawRect.Width, 1), new Color(92, 122, 92));
            sb.Draw(pixel, new Rectangle(drawRect.X, drawRect.Y, 1, drawRect.Height), new Color(92, 122, 92));
            sb.Draw(pixel, new Rectangle(drawRect.Right - 1, drawRect.Y, 1, drawRect.Height), new Color(92, 122, 92));

            sb.DrawString(font, hoveredSkill.Title, new Vector2(drawRect.X + 8, drawRect.Y + 6), new Color(205, 232, 205));
            DrawTooltipParagraphs(sb, font, paragraphs, new Vector2(drawRect.X + 8, drawRect.Y + 24), drawRect.Width - 16, bodyScale, lineHeight, paragraphGap);
        };

        window.Open(GetCenteredWindowPosition(window));
    }

    public float GetCombatLevel()
        => Fortitude + Dodge + Blocking + HandToHand + OneHandedWeapons + TwoHandedWeapons + RangedWeapons;

    public float GetTotalLevel()
        => CreateSkillDefinitions().Sum(definition => GetSkill(definition.Type));

    public static float GetDamageMultiplier(SkillType type, float currentValue)
    {
        var t = MathHelper.Clamp(currentValue / 100f, 0f, 1f);
        return type == SkillType.HandToHand
            ? MathHelper.Lerp(0.60f, 1.35f, t)
            : MathHelper.Lerp(0.72f, 1.22f, t);
    }

    public static float GetFortitudeDamageTakenMultiplier(float currentValue)
    {
        var t = MathHelper.Clamp(currentValue / 100f, 0f, 1f);
        return MathHelper.Lerp(1.12f, 0.78f, t);
    }

    public static float GetAccuracyOffset(SkillType type, float currentValue)
    {
        var t = MathHelper.Clamp(currentValue / 100f, 0f, 1f);
        return type switch
        {
            SkillType.HandToHand => MathHelper.Lerp(-0.16f, 0.10f, t),
            SkillType.OneHandedWeapons or SkillType.TwoHandedWeapons => MathHelper.Lerp(-0.24f, 0.09f, t),
            SkillType.RangedWeapons => MathHelper.Lerp(-0.20f, 0.12f, t),
            _ => 0f
        };
    }

    private static float GetProgressionFactor(float currentValue)
        => 1f / (1f + currentValue / 25f);

    private static IReadOnlyList<SkillDefinition> CreateSkillDefinitions()
        => new[]
        {
            new SkillDefinition
            {
                Prefix = "fortitude",
                Type = SkillType.Fortitude,
                Title = "Крепость",
                Description = "Снижает часть входящего урона даже без брони.",
                TrainingHint = "Прокачивается, когда ты получаешь урон и переживаешь его.",
                FillColor = new Color(196, 148, 108)
            },
            new SkillDefinition
            {
                Prefix = "dodge",
                Type = SkillType.Dodge,
                Title = "Уворот",
                Description = "Даёт шанс уклониться от следующей атаки, включая стрелы.",
                TrainingHint = "Прокачивается успешными уклонениями в бою.",
                FillColor = new Color(111, 180, 224)
            },
            new SkillDefinition
            {
                Prefix = "blocking",
                Type = SkillType.Blocking,
                Title = "Блокирование",
                Description = "Даёт шанс заблокировать удар оружием ближнего боя.",
                TrainingHint = "Прокачивается успешными блоками оружием ближнего боя.",
                FillColor = new Color(156, 156, 220)
            },
            new SkillDefinition
            {
                Prefix = "handToHand",
                Type = SkillType.HandToHand,
                Title = "Рукопашный бой",
                Description = "Повышает шанс попасть кулаком и заметно усиливает урон без оружия.",
                TrainingHint = "Прокачивается ударами кулаками по противнику.",
                FillColor = new Color(214, 174, 128)
            },
            new SkillDefinition
            {
                Prefix = "oneHanded",
                Type = SkillType.OneHandedWeapons,
                Title = "Одноручное оружие",
                Description = "Повышает шанс попасть одноручным оружием и слегка увеличивает его урон.",
                TrainingHint = "Прокачивается ударами одноручным оружием в бою.",
                FillColor = new Color(180, 210, 132)
            },
            new SkillDefinition
            {
                Prefix = "twoHanded",
                Type = SkillType.TwoHandedWeapons,
                Title = "Двуручное оружие",
                Description = "Повышает шанс попасть двуручным оружием и слегка увеличивает его урон.",
                TrainingHint = "Прокачивается ударами двуручным оружием в бою.",
                FillColor = new Color(210, 150, 118)
            },
            new SkillDefinition
            {
                Prefix = "ranged",
                Type = SkillType.RangedWeapons,
                Title = "Дальний бой",
                Description = "Снижает разброс луков и арбалетов и слегка увеличивает урон попаданий.",
                TrainingHint = "Прокачивается успешными попаданиями по живым целям; дальние попадания дают больше опыта.",
                FillColor = new Color(132, 202, 156)
            },
            new SkillDefinition
            {
                Prefix = "medicine",
                Type = SkillType.Medicine,
                Title = "Медицина",
                Description = "Влияет на шанс, эффективность и аккуратность лечения ран.",
                TrainingHint = "Прокачивается лечением себя и других медицинскими средствами.",
                FillColor = new Color(118, 212, 166)
            },
            new SkillDefinition
            {
                Prefix = "thievery",
                Type = SkillType.Thievery,
                Title = "Воровство",
                Description = "Повышает шанс удачно вытянуть вещь из чужих карманов.",
                TrainingHint = "Прокачивается попытками карманной кражи и успешным воровством.",
                FillColor = new Color(212, 132, 132)
            },
            new SkillDefinition
            {
                Prefix = "social",
                Type = SkillType.Social,
                Title = "Социалка",
                Description = "Помогает уговаривать людей и выбивать из них уступки словом.",
                TrainingHint = "Прокачивается успешными социальными проверками и разговорами.",
                FillColor = new Color(138, 206, 218)
            },
            new SkillDefinition
            {
                Prefix = "trade",
                Type = SkillType.Trade,
                Title = "Торговля",
                Description = "Определяет, насколько выгодно ты умеешь договариваться об обмене.",
                TrainingHint = "Прокачивается удачными торговыми и денежными сделками.",
                FillColor = new Color(212, 194, 110)
            },
            new SkillDefinition
            {
                Prefix = "craftsmanship",
                Type = SkillType.Craftsmanship,
                Title = "Ремесло",
                Description = "Общий ручной навык, влияющий на аккуратность базового изготовления вещей.",
                TrainingHint = "Прокачивается созданием простых предметов и работой руками.",
                FillColor = new Color(182, 176, 156)
            },
            new SkillDefinition
            {
                Prefix = "smithing",
                Type = SkillType.Smithing,
                Title = "Кузнечное дело",
                Description = "Определяет качество кованого оружия, брони и металлических изделий.",
                TrainingHint = "Прокачивается ковкой на наковальне и работой с металлом.",
                FillColor = new Color(214, 144, 104)
            },
            new SkillDefinition
            {
                Prefix = "tailoring",
                Type = SkillType.Tailoring,
                Title = "Шитьё",
                Description = "Влияет на качество одежды, сумок, перевязок и тканевых вещей.",
                TrainingHint = "Прокачивается шитьём на портняжном столе и изготовлением повязок.",
                FillColor = new Color(184, 154, 220)
            }
        };

    private static void RefreshSkillRows(XmlWindow window, SkillComponent skills, IReadOnlyList<SkillDefinition> definitions)
    {
        foreach (var definition in definitions)
            UpdateSkillRow(window, definition, skills.GetSkill(definition.Type));

        var totalLabel = window.Get<UILabel>("summaryLabel");
        if (totalLabel != null)
            totalLabel.Text = $"Всего: {skills.GetTotalLevel():F0}";
    }

    private static void BindSkillRow(XmlWindow window, SkillDefinition definition, List<SkillViewState> bindings)
    {
        var nameLabel = window.Get<UILabel>($"{definition.Prefix}Name");
        var valueLabel = window.Get<UILabel>($"{definition.Prefix}Value");
        var progress = window.Get<UIProgressBar>($"{definition.Prefix}Progress");

        if (nameLabel == null || valueLabel == null || progress == null)
            return;

        nameLabel.Text = definition.Title;
        nameLabel.Color = definition.FillColor;
        progress.FillColor = definition.FillColor;
        progress.MaxValue = 100f;

        bindings.Add(new SkillViewState
        {
            HoverTarget = progress,
            Type = definition.Type,
            Title = definition.Title,
            Description = definition.Description,
            TrainingHint = definition.TrainingHint
        });
        bindings.Add(new SkillViewState
        {
            HoverTarget = nameLabel,
            Type = definition.Type,
            Title = definition.Title,
            Description = definition.Description,
            TrainingHint = definition.TrainingHint
        });
        bindings.Add(new SkillViewState
        {
            HoverTarget = valueLabel,
            Type = definition.Type,
            Title = definition.Title,
            Description = definition.Description,
            TrainingHint = definition.TrainingHint
        });
    }

    private static void UpdateSkillRow(XmlWindow window, SkillDefinition definition, float value)
    {
        var valueLabel = window.Get<UILabel>($"{definition.Prefix}Value");
        var progress = window.Get<UIProgressBar>($"{definition.Prefix}Progress");
        if (valueLabel == null || progress == null)
            return;

        var currentLevel = (int)MathF.Floor(Math.Clamp(value, 0f, 100f));
        var progressValue = (value - currentLevel) * 100f;

        valueLabel.Text = currentLevel.ToString();
        valueLabel.Color = value switch
        {
            >= 60f => AssetManager.ParseHexColor("#88FF88", Color.White),
            >= 25f => AssetManager.ParseHexColor("#E8D97A", Color.White),
            _ => AssetManager.ParseHexColor("#CCCCCC", Color.White)
        };
        progress.Value = progressValue;
        progress.MaxValue = 100f;
    }

    private static List<TooltipParagraph> BuildTooltipParagraphs(SkillComponent skills, SkillViewState hoveredSkill)
    {
        var paragraphs = new List<TooltipParagraph>
        {
            new()
            {
                Text = hoveredSkill.Description,
                Color = new Color(208, 208, 208)
            }
        };

        var currentValue = skills.GetSkill(hoveredSkill.Type);
        switch (hoveredSkill.Type)
        {
            case SkillType.Fortitude:
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Текущий множитель получаемого урона: x{GetFortitudeDamageTakenMultiplier(currentValue):0.00}",
                    Color = new Color(150, 230, 150)
                });
                break;
            case SkillType.HandToHand:
            case SkillType.OneHandedWeapons:
            case SkillType.TwoHandedWeapons:
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Текущий множитель урона: x{GetDamageMultiplier(hoveredSkill.Type, currentValue):0.00}",
                    Color = new Color(150, 230, 150)
                });
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Текущий модификатор попадания: {GetAccuracyOffset(hoveredSkill.Type, currentValue):+0%;-0%;0%}",
                    Color = new Color(150, 230, 150)
                });
                break;
            case SkillType.Medicine:
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Эффективность лечения: x{SkillChecks.GetMedicineEfficiency(currentValue):0.00}",
                    Color = new Color(150, 230, 150)
                });
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Базовый шанс осечки: {SkillChecks.GetMedicineFumbleChance(currentValue, 25f):0%}",
                    Color = new Color(150, 230, 150)
                });
                break;
            case SkillType.Thievery:
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Шанс кражи предмета средней сложности: {SkillChecks.GetStealChance(currentValue, 22f, 0f):0%}",
                    Color = new Color(150, 230, 150)
                });
                break;
            case SkillType.Trade:
                paragraphs.Add(new TooltipParagraph
                {
                    Text = $"Текущий бонус к выгоде: +{SkillChecks.GetTradeBonus(currentValue):0%}",
                    Color = new Color(150, 230, 150)
                });
                break;
        }

        paragraphs.Add(new TooltipParagraph
        {
            Text = $"Как развить:\n{hoveredSkill.TrainingHint}",
            Color = new Color(208, 208, 208)
        });
        return paragraphs;
    }

    private static Point GetCenteredWindowPosition(XmlWindow window)
    {
        if (!ServiceLocator.Has<GraphicsDevice>())
            return new Point(40, 40);

        var size = GameEngine.Instance.GetUiClientSize();
        return new Point(
            Math.Max(0, (size.X - window.Width) / 2),
            Math.Max(0, (size.Y - window.Height) / 2));
    }

    private static Point GetUiMousePoint()
    {
        if (!ServiceLocator.Has<InputManager>())
            return Point.Zero;

        var input = ServiceLocator.Get<InputManager>();
        var scale = GetUiScale();
        var mouse = input.MousePosition;
        return new Point(
            (int)MathF.Round(mouse.X / scale),
            (int)MathF.Round(mouse.Y / scale));
    }

    private static float GetUiScale()
        => ServiceLocator.Has<IUiScaleSource>()
            ? Math.Clamp(ServiceLocator.Get<IUiScaleSource>().UiScale, 0.75f, 2f)
            : 1f;

    private static string WrapTooltipText(SpriteFont font, string text, int maxWidth, float scale)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
            return text;

        var paragraphs = text.Replace("\r\n", "\n").Split('\n');
        var wrapped = new StringBuilder(text.Length + 16);

        for (var p = 0; p < paragraphs.Length; p++)
        {
            var words = paragraphs[p].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;

            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                var width = font.MeasureString(candidate).X * scale;
                if (width > maxWidth && !string.IsNullOrEmpty(line))
                {
                    if (wrapped.Length > 0 && wrapped[^1] != '\n')
                        wrapped.Append('\n');
                    wrapped.Append(line);
                    wrapped.Append('\n');
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            if (!string.IsNullOrEmpty(line))
            {
                if (wrapped.Length > 0 && wrapped[^1] != '\n')
                    wrapped.Append('\n');
                wrapped.Append(line);
            }

            if (p < paragraphs.Length - 1)
                wrapped.Append('\n');
        }

        return wrapped.ToString();
    }

    private static int MeasureTooltipBodyHeight(SpriteFont font, IReadOnlyList<TooltipParagraph> paragraphs, int maxWidth, float scale, int lineHeight, int paragraphGap)
    {
        var total = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var wrapped = WrapTooltipText(font, paragraphs[i].Text, maxWidth, scale);
            var lineCount = Math.Max(1, wrapped.Split('\n').Length);
            total += lineCount * lineHeight;
            if (i < paragraphs.Count - 1)
                total += paragraphGap;
        }

        return total;
    }

    private static void DrawTooltipParagraphs(SpriteBatch sb, SpriteFont font, IReadOnlyList<TooltipParagraph> paragraphs, Vector2 start, int maxWidth, float scale, int lineHeight, int paragraphGap)
    {
        var y = start.Y;
        foreach (var paragraph in paragraphs)
        {
            var wrapped = WrapTooltipText(font, paragraph.Text, maxWidth, scale);
            var lines = wrapped.Split('\n');
            foreach (var line in lines)
            {
                sb.DrawString(
                    font,
                    line,
                    new Vector2(start.X, y),
                    paragraph.Color,
                    0f,
                    Vector2.Zero,
                    scale,
                    SpriteEffects.None,
                    0f);
                y += lineHeight;
            }

            y += paragraphGap;
        }
    }
}
