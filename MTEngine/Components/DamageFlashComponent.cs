using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("damageFlash")]
public class DamageFlashComponent : Component
{
    public float Remaining { get; set; }
    public float Duration { get; set; } = 0.12f;

    public float Intensity => Duration <= 0.001f
        ? 0f
        : Math.Clamp(Remaining / Duration, 0f, 1f);

    public static void Trigger(Entity entity, float duration = 0.12f)
    {
        var flash = entity.GetComponent<DamageFlashComponent>();
        if (flash == null)
        {
            flash = new DamageFlashComponent();
            entity.AddComponent(flash);
        }

        flash.Duration = Math.Max(0.05f, duration);
        flash.Remaining = flash.Duration;
    }
}
