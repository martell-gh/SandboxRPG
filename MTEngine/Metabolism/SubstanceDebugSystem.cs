using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;

namespace MTEngine.Metabolism;

public class SubstanceDebugSystem : GameSystem
{
    private InputManager _input = null!;
    private IKeyBindingSource? _keys;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _keys = ServiceLocator.Has<IKeyBindingSource>() ? ServiceLocator.Get<IKeyBindingSource>() : null;
    }

    public override void Update(float deltaTime)
    {
        if (DevConsole.IsOpen)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();
        if (player == null)
            return;

        if (_input.IsPressed(GetKey("InspectContainer", Keys.P)))
            PrintHeldContainer(player);
    }

    private static void PrintHeldContainer(Entity player)
    {
        var hands = player.GetComponent<HandsComponent>();
        var held = hands?.ActiveItem;
        var container = held?.GetComponent<LiquidContainerComponent>();
        if (container != null)
        {
            var text = container.DescribeContents();
            Console.WriteLine($"[Substances] {text}");
            Systems.PopupTextSystem.Show(player, "Состав выведен в консоль", Color.LightCyan, lifetime: 1.5f);
            return;
        }

        var mortar = held?.GetComponent<MortarComponent>();
        if (mortar != null)
        {
            Console.WriteLine($"[Mortar] {mortar.DescribeContents()}");
            Systems.PopupTextSystem.Show(player, "Толкушка выведена в консоль", Color.LightCyan, lifetime: 1.5f);
        }
    }
    private Keys GetKey(string action, Keys fallback)
        => _keys?.GetKey(action) ?? fallback;
}
