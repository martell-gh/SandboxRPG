using MTEngine.Components;
using MTEngine.ECS;

namespace MTEngine.Systems;

public class DamageFlashSystem : GameSystem
{
    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<DamageFlashComponent>())
        {
            var flash = entity.GetComponent<DamageFlashComponent>()!;
            flash.Remaining = Math.Max(0f, flash.Remaining - deltaTime);
        }
    }
}
