using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;
using MTEngine.UI;

namespace MTEngine.Wounds;

/// <summary>
/// Типы урона в стиле SS14.
/// Каждый тип накапливается отдельно и лечится разными средствами.
/// </summary>
public enum DamageType
{
    /// <summary>Порезы — от лезвий, когтей. Могут вызвать кровотечение.</summary>
    Slash,

    /// <summary>Ушибы — от ударов тупыми предметами, падений.</summary>
    Blunt,

    /// <summary>Ожоги — от огня, кипятка, кислот.</summary>
    Burn,

    /// <summary>Истощение — урон от голода, жажды и общего упадка сил.</summary>
    Exhaustion
}

/// <summary>
/// Активное кровотечение. Создаётся при сильном режущем ударе.
/// Каждое кровотечение тикает отдельно и постепенно замедляется.
/// </summary>
public class BleedingEntry
{
    /// <summary>Текущая скорость кровотечения (HP/сек).</summary>
    public float Rate { get; set; }

    /// <summary>Скорость естественной остановки кровотечения (Rate снижается на это в сек).</summary>
    public float NaturalClotRate { get; set; }
}

/// <summary>
/// Компонент ранений. Хранит накопленный урон по типам и активные кровотечения.
/// Как в SS14: урон суммируется по типам, общая сумма вычитается из здоровья.
///
/// Урон наносится через статические методы:
///   WoundComponent.ApplyDamage(entity, DamageType.Slash, 25f)
///   WoundComponent.ApplyDamage(entity, DamageType.Burn, 10f)
///
/// Суммарный урон = Slash + Blunt + Burn. Здоровье = MaxHealth - TotalDamage.
/// </summary>
[RegisterComponent("wounds")]
public class WoundComponent : Component, IInteractionSource
{
    // ── Накопленный урон по типам ──────────────────────────────────

    [SaveField("slashDamage")]
    [DataField("slashDamage")]
    public float SlashDamage { get; set; }

    [SaveField("bluntDamage")]
    [DataField("bluntDamage")]
    public float BluntDamage { get; set; }

    [SaveField("burnDamage")]
    [DataField("burnDamage")]
    public float BurnDamage { get; set; }

    [SaveField("exhaustionDamage")]
    [DataField("exhaustionDamage")]
    public float ExhaustionDamage { get; set; }

    // ── Кровотечения ───────────────────────────────────────────────

    /// <summary>Список активных кровотечений.</summary>
    [SaveField]
    public List<BleedingEntry> Bleedings { get; } = new();

    // ── Пороги кровотечения ────────────────────────────────────────

    /// <summary>Минимальная сила единичного пореза для начала кровотечения.</summary>
    [DataField("bleedThreshold")]
    [SaveField("bleedThreshold")]
    public float BleedThreshold { get; set; } = 15f;

    /// <summary>Множитель: сила_пореза * BleedRateMultiplier = начальная скорость кровотечения.</summary>
    [DataField("bleedRateMultiplier")]
    [SaveField("bleedRateMultiplier")]
    public float BleedRateMultiplier { get; set; } = 0.3f;

    /// <summary>Базовая скорость свёртывания (снижение Rate/сек). Чем выше — тем быстрее останавливается.</summary>
    [DataField("baseClotRate")]
    [SaveField("baseClotRate")]
    public float BaseClotRate { get; set; } = 0.4f;

    /// <summary>Порог Rate, выше которого кровотечение НЕ останавливается само (слишком сильное).</summary>
    [DataField("unstoppableBleedRate")]
    [SaveField("unstoppableBleedRate")]
    public float UnstoppableBleedRate { get; set; } = 5f;

    // ── Вычисляемые свойства ───────────────────────────────────────

    /// <summary>Суммарный урон по всем типам.</summary>
    public float TotalDamage => SlashDamage + BluntDamage + BurnDamage + ExhaustionDamage;

    /// <summary>Суммарная скорость кровотечения (HP/сек).</summary>
    public float TotalBleedRate => Bleedings.Sum(b => b.Rate);

    /// <summary>Есть ли активные кровотечения.</summary>
    public bool IsBleeding => Bleedings.Count > 0;

    // ── Получение/установка урона по типу ──────────────────────────

    public float GetDamage(DamageType type) => type switch
    {
        DamageType.Slash => SlashDamage,
        DamageType.Blunt => BluntDamage,
        DamageType.Burn => BurnDamage,
        DamageType.Exhaustion => ExhaustionDamage,
        _ => 0f
    };

    public void SetDamage(DamageType type, float value)
    {
        value = Math.Max(0f, value);
        switch (type)
        {
            case DamageType.Slash: SlashDamage = value; break;
            case DamageType.Blunt: BluntDamage = value; break;
            case DamageType.Burn: BurnDamage = value; break;
            case DamageType.Exhaustion: ExhaustionDamage = value; break;
        }
    }

    // ── Статический API для нанесения урона ────────────────────────

    /// <summary>
    /// Нанести урон указанного типа сущности.
    /// Если у сущности нет WoundComponent — урон не наносится.
    /// Возвращает true если урон был нанесён.
    /// </summary>
    public static bool ApplyDamage(Entity target, DamageType type, float amount)
    {
        if (amount <= 0f) return false;

        var wounds = target.GetComponent<WoundComponent>();
        if (wounds == null) return false;

        wounds.SetDamage(type, wounds.GetDamage(type) + amount);

        if (type != DamageType.Exhaustion)
            DamageFlashComponent.Trigger(target);

        // Кровотечение от сильных порезов
        if (type == DamageType.Slash && amount >= wounds.BleedThreshold)
        {
            var bleedRate = amount * wounds.BleedRateMultiplier;
            var clotRate = bleedRate < wounds.UnstoppableBleedRate
                ? wounds.BaseClotRate
                : 0f; // Слишком сильное — само не остановится

            wounds.Bleedings.Add(new BleedingEntry
            {
                Rate = bleedRate,
                NaturalClotRate = clotRate
            });
        }

        return true;
    }

    /// <summary>
    /// Вылечить указанный тип урона на amount единиц.
    /// Возвращает реально вылеченное количество.
    /// </summary>
    public static float HealDamage(Entity target, DamageType type, float amount)
    {
        if (amount <= 0f) return 0f;

        var wounds = target.GetComponent<WoundComponent>();
        if (wounds == null) return 0f;

        var current = wounds.GetDamage(type);
        var healed = Math.Min(current, amount);
        wounds.SetDamage(type, current - healed);
        return healed;
    }

    /// <summary>
    /// Остановить кровотечение (убрать все или часть записей).
    /// stopAmount — на сколько HP/сек суммарно снизить кровотечение.
    /// </summary>
    public static float StopBleeding(Entity target, float stopAmount)
    {
        var wounds = target.GetComponent<WoundComponent>();
        if (wounds == null) return 0f;

        float totalStopped = 0f;
        for (int i = wounds.Bleedings.Count - 1; i >= 0 && stopAmount > 0f; i--)
        {
            var entry = wounds.Bleedings[i];
            if (entry.Rate <= stopAmount)
            {
                stopAmount -= entry.Rate;
                totalStopped += entry.Rate;
                wounds.Bleedings.RemoveAt(i);
            }
            else
            {
                entry.Rate -= stopAmount;
                totalStopped += stopAmount;
                stopAmount = 0f;
            }
        }

        return totalStopped;
    }

    // ── Взаимодействие: Осмотреть ─────────────────────────────────

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (ctx.Target != Owner)
            yield break;

        // Осмотреть можно любую сущность с этим компонентом
        yield return new InteractionEntry
        {
            Id = "wounds.examine",
            Label = "Осмотреть ранения",
            Priority = 5,
            Delay = InteractionDelay.Seconds(1f, "Осмотр"),
            Execute = c => OpenExamineWindow(c.Target)
        };
    }

    // ── UI окно осмотра ────────────────────────────────────────────

    private static void OpenExamineWindow(Entity target)
    {
        var ui = Core.ServiceLocator.Get<UIManager>();
        const string windowId = "woundsExamine";

        // Закрыть старое, если было
        var existing = ui.GetWindow(windowId);
        if (existing != null)
        {
            ui.RemoveWindow(existing);
        }

        var wounds = target.GetComponent<WoundComponent>();
        var health = target.GetComponent<HealthComponent>();
        var path = Path.Combine("SandboxGame", "Content", "UI", "WoundExamineWindow.xml");
        if (!File.Exists(path))
            return;

        var window = ui.LoadWindow(path);
        window.Title = $"Осмотр: {target.Name ?? "Неизвестный"}";

        var noneLabel = window.Get<UILabel>("noneLabel");
        var slashLabel = window.Get<UILabel>("slashLabel");
        var bluntLabel = window.Get<UILabel>("bluntLabel");
        var burnLabel = window.Get<UILabel>("burnLabel");
        var exhaustionLabel = window.Get<UILabel>("exhaustionLabel");
        var totalLabel = window.Get<UILabel>("totalLabel");
        var bleedLabel = window.Get<UILabel>("bleedLabel");
        var healthLabel = window.Get<UILabel>("healthLabel");

        SetDamageLabel(noneLabel, wounds == null || wounds.TotalDamage <= 0.5f, "Ранений нет.", "#88FF88");
        SetDamageLabel(slashLabel, wounds is { SlashDamage: > 0.5f }, $"Порезы: {wounds?.SlashDamage:F0}", wounds != null ? GetSeverityColor(wounds.SlashDamage) : "#CCCC44");
        SetDamageLabel(bluntLabel, wounds is { BluntDamage: > 0.5f }, $"Ушибы: {wounds?.BluntDamage:F0}", wounds != null ? GetSeverityColor(wounds.BluntDamage) : "#CCCC44");
        SetDamageLabel(burnLabel, wounds is { BurnDamage: > 0.5f }, $"Ожоги: {wounds?.BurnDamage:F0}", wounds != null ? GetSeverityColor(wounds.BurnDamage) : "#CCCC44");
        SetDamageLabel(exhaustionLabel, wounds is { ExhaustionDamage: > 0.5f }, $"Истощение: {wounds?.ExhaustionDamage:F0}", wounds != null ? GetSeverityColor(wounds.ExhaustionDamage) : "#CCCC44");
        SetDamageLabel(totalLabel, wounds != null && wounds.TotalDamage > 0.5f, $"Общий урон: {wounds?.TotalDamage:F0}", "#FFAAAA");

        if (bleedLabel != null)
        {
            bleedLabel.Visible = wounds?.IsBleeding == true;
            if (wounds?.IsBleeding == true)
            {
                bleedLabel.Text = wounds.TotalBleedRate >= wounds.UnstoppableBleedRate
                    ? $"Кровотечение: {wounds.TotalBleedRate:F1}/с [СИЛЬНОЕ!]"
                    : $"Кровотечение: {wounds.TotalBleedRate:F1}/с";
                bleedLabel.Color = ParseColor(wounds.TotalBleedRate >= wounds.UnstoppableBleedRate ? "#FF3333" : "#FF8888");
            }
        }

        if (healthLabel != null)
        {
            healthLabel.Visible = health != null;
            if (health != null)
            {
                healthLabel.Text = $"Здоровье: {health.Health:F0}/{health.MaxHealth:F0}";
                var hpColor = health.Health > health.MaxHealth * 0.5f ? "#88FF88" :
                              health.Health > health.MaxHealth * 0.25f ? "#FFFF44" : "#FF4444";
                healthLabel.Color = ParseColor(hpColor);
            }
        }

        var position = GetCenteredWindowPosition(window);
        window.Open(position);
    }

    private static string GetSeverityColor(float damage)
    {
        if (damage < 20f) return "#CCCC44";   // Лёгкие
        if (damage < 50f) return "#FF9944";   // Средние
        return "#FF4444";                      // Тяжёлые
    }

    private static void SetDamageLabel(UILabel? label, bool visible, string text, string colorHex)
    {
        if (label == null)
            return;

        label.Visible = visible;
        if (!visible)
            return;

        label.Text = text;
        label.Color = ParseColor(colorHex);
    }

    private static Color ParseColor(string hex)
        => AssetManager.ParseHexColor(hex, Color.White);

    private static Point GetCenteredWindowPosition(XmlWindow window)
    {
        if (!Core.ServiceLocator.Has<GraphicsDevice>())
            return new Point(40, 40);

        var viewport = Core.ServiceLocator.Get<GraphicsDevice>().Viewport;
        var x = Math.Max(0, (viewport.Width - window.Width) / 2);
        var y = Math.Max(0, (viewport.Height - window.Height) / 2);
        return new Point(x, y);
    }
}
