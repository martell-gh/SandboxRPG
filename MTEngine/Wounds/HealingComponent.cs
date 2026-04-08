using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;
using MTEngine.Systems;

namespace MTEngine.Wounds;

/// <summary>
/// Компонент лечебного предмета. Определяет какой тип урона лечит,
/// сколько HP восстанавливает, и может ли останавливать кровотечение.
///
/// Ставится на предмет (Entity с ItemComponent).
/// При использовании предмет расходуется (стак -1 или удаление).
/// </summary>
[RegisterComponent("healing")]
public class HealingComponent : Component, IInteractionSource
{
    /// <summary>Тип урона, который лечит этот предмет.</summary>
    [DataField("damageType")]
    public DamageType HealsDamageType { get; set; } = DamageType.Slash;

    /// <summary>Количество урона, которое снимает за одно использование.</summary>
    [DataField("healAmount")]
    public float HealAmount { get; set; } = 20f;

    /// <summary>Может ли останавливать кровотечение (и на сколько HP/сек).</summary>
    [DataField("stopBleedAmount")]
    public float StopBleedAmount { get; set; }

    /// <summary>Название действия в меню (например "Перевязать", "Наложить мазь").</summary>
    [DataField("useLabel")]
    public string UseLabel { get; set; } = "Лечить";

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        // Лечить можно только если предмет в руке
        var item = Owner?.GetComponent<ItemComponent>();
        if (item == null || item.IsFree) yield break;

        // Цель должна иметь WoundComponent
        var targetWounds = ctx.Target.GetComponent<WoundComponent>();
        if (targetWounds == null) yield break;

        // Проверяем что есть что лечить
        bool canHealDamage = targetWounds.GetDamage(HealsDamageType) > 0.5f;
        bool canStopBleed = StopBleedAmount > 0f && targetWounds.IsBleeding;

        if (!canHealDamage && !canStopBleed) yield break;

        var damageTypeName = GetDamageTypeName(HealsDamageType);
        var label = $"{UseLabel} ({damageTypeName})";

        yield return new InteractionEntry
        {
            Id = $"healing.use.{HealsDamageType}",
            Label = label,
            Priority = 20,
            Delay = InteractionDelay.Seconds(1.8f, UseLabel),
            Execute = c => UseHealing(c.Actor, c.Target)
        };
    }

    private void UseHealing(Entity actor, Entity target)
    {
        var targetWounds = target.GetComponent<WoundComponent>();
        if (targetWounds == null) return;

        float healed = 0f;
        float bleedStopped = 0f;

        // Лечим урон
        if (HealAmount > 0f)
            healed = WoundComponent.HealDamage(target, HealsDamageType, HealAmount);

        // Останавливаем кровотечение
        if (StopBleedAmount > 0f)
            bleedStopped = WoundComponent.StopBleeding(target, StopBleedAmount);

        // Попап с результатом
        var parts = new List<string>();
        if (healed > 0.5f)
            parts.Add($"{GetDamageTypeName(HealsDamageType)} -{healed:F0}");
        if (bleedStopped > 0.5f)
            parts.Add($"кровотечение -{bleedStopped:F1}/с");

        if (parts.Count > 0)
        {
            var text = string.Join(", ", parts);
            PopupTextSystem.Show(target, text, Color.LightGreen, lifetime: 1.5f);
        }
        else
        {
            PopupTextSystem.Show(target, "Не помогло.", Color.Gray, lifetime: 1f);
        }

        // Расходуем предмет
        ConsumeItem(actor);
    }

    private void ConsumeItem(Entity actor)
    {
        var item = Owner?.GetComponent<ItemComponent>();
        if (item == null) return;

        if (item.Stackable && item.StackCount > 1)
        {
            item.StackCount--;
        }
        else
        {
            // Выбросить из рук и уничтожить
            var hands = actor.GetComponent<HandsComponent>();
            if (hands != null)
            {
                foreach (var hand in hands.Hands)
                {
                    if (hand.HeldItem == Owner)
                    {
                        hand.HeldItem = null;
                        break;
                    }
                }
            }

            if (Owner != null && Core.ServiceLocator.Has<ECS.World>())
                Core.ServiceLocator.Get<ECS.World>().DestroyEntity(Owner);
        }
    }

    private static string GetDamageTypeName(DamageType type) => type switch
    {
        DamageType.Slash => "порезы",
        DamageType.Blunt => "ушибы",
        DamageType.Burn => "ожоги",
        DamageType.Exhaustion => "истощение",
        _ => "урон"
    };
}
