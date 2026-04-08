using System;
using MTEngine.Components;
using MTEngine.ECS;

namespace MTEngine.Wounds;

/// <summary>
/// Система обработки ранений. Каждый кадр:
///   1. Тикает кровотечения (урон от кровопотери → Slash damage растёт).
///   2. Естественное свёртывание (Rate снижается со временем).
///   3. Синхронизация здоровья: Health = MaxHealth - TotalDamage.
/// </summary>
public class WoundSystem : GameSystem
{
    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<WoundComponent>())
        {
            var wounds = entity.GetComponent<WoundComponent>()!;
            var health = entity.GetComponent<HealthComponent>();

            // Не обрабатываем мёртвых
            if (health?.IsDead == true) continue;

            // ── 1. Кровотечения: добавляют урон типа Slash ─────────
            if (wounds.IsBleeding)
            {
                float bleedDamage = wounds.TotalBleedRate * deltaTime;
                if (bleedDamage > 0f)
                    wounds.SlashDamage += bleedDamage;
            }

            // ── 2. Естественное свёртывание ─────────────────────────
            for (int i = wounds.Bleedings.Count - 1; i >= 0; i--)
            {
                var entry = wounds.Bleedings[i];

                if (entry.NaturalClotRate > 0f)
                {
                    entry.Rate -= entry.NaturalClotRate * deltaTime;
                    if (entry.Rate <= 0f)
                    {
                        wounds.Bleedings.RemoveAt(i);
                        continue;
                    }
                }
            }

            // ── 3. Синхронизация здоровья ──────────────────────────
            // Health = MaxHealth - TotalDamage (как в SS14)
            if (health != null)
            {
                health.Health = Math.Max(0f, health.MaxHealth - wounds.TotalDamage);
            }
        }
    }
}
