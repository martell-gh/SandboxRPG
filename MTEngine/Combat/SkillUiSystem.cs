using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Combat;

public class SkillUiSystem : GameSystem
{
    private InputManager? _input;
    private IKeyBindingSource? _keys;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _keys = ServiceLocator.Has<IKeyBindingSource>() ? ServiceLocator.Get<IKeyBindingSource>() : null;
    }

    public override void Update(float deltaTime)
    {
        if (_input == null || DevConsole.IsOpen)
            return;

        if (!_input.IsPressed(GetKey("Skills", Keys.K)))
            return;

        var actor = World.GetEntitiesWith<SkillComponent>().FirstOrDefault(entity => entity.HasComponent<Components.PlayerTagComponent>());
        if (actor == null)
            return;

        SkillComponent.ToggleSkillsWindow(actor);
    }

    private Keys GetKey(string action, Keys fallback)
        => _keys?.GetKey(action) ?? fallback;
}
