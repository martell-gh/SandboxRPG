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

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
    }

    public override void Update(float deltaTime)
    {
        if (DevConsole.IsOpen)
            return;

        var player = World.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();
        if (player == null)
            return;

        if (_input.IsPressed(Keys.P))
            PrintHeldContainer(player);

        if (_input.IsPressed(Keys.O))
            PrintKnowledgeMemory(player);
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

    private static void PrintKnowledgeMemory(Entity player)
    {
        var memory = player.GetComponent<KnowledgeMemoryComponent>();
        if (memory == null)
        {
            Console.WriteLine("[Memory] Справочник памяти пуст.");
            Systems.PopupTextSystem.Show(player, "Память пуста", Color.LightGray, lifetime: 1.5f);
            return;
        }

        var text = memory.DescribeSubstanceCatalog();
        Console.WriteLine($"[Memory]\n{text}");
        Systems.PopupTextSystem.Show(player, "Справочник выведен в консоль", Color.LightGreen, lifetime: 1.5f);
    }
}
