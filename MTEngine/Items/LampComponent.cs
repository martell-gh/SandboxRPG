using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Items;

[RegisterComponent("lamp")]
public class LampComponent : Component, IInteractionSource, IPrototypeInitializable
{
    [DataField("on")]
    public bool IsOn { get; set; }

    [DataField("name")]
    public string LampName { get; set; } = "Lamp";

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        SyncLightState();
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (ctx.Target != Owner)
            yield break;

        var item = Owner?.GetComponent<ItemComponent>();
        var name = item?.ItemName ?? LampName;

        yield return new InteractionEntry
        {
            Id = IsOn ? "lamp.turnOff" : "lamp.turnOn",
            Label = IsOn ? $"Выключить ({name})" : $"Включить ({name})",
            Priority = 24,
            Execute = c => Toggle(c.Actor)
        };
    }

    public void Toggle(Entity actor)
    {
        IsOn = !IsOn;
        SyncLightState();

        var item = Owner?.GetComponent<ItemComponent>();
        var name = item?.ItemName ?? LampName;
        var text = IsOn ? $"{name}: включена" : $"{name}: выключена";
        PopupTextSystem.Show(actor, text, IsOn ? Color.Khaki : Color.SlateGray, lifetime: 1.3f);
    }

    private void SyncLightState()
    {
        var light = Owner?.GetComponent<LightComponent>();
        if (light != null)
            light.Enabled = IsOn;
    }
}
