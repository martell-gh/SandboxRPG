using Microsoft.Xna.Framework;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Systems;

public class DayNightSystem : GameSystem
{
    private GameClock _clock = null!;
    private LightingSystem? _lighting;

    public GameClock Clock => _clock;

    public override void OnInitialize()
    {
        _clock = ServiceLocator.Get<GameClock>();
    }

    public override void Update(float deltaTime)
    {
        _clock.Update(deltaTime);

        _lighting ??= World.GetSystem<LightingSystem>();
        if (_lighting != null)
            _lighting.AmbientColor = GetAmbientColor(_clock.Hour);
    }

    public static Color GetAmbientColor(float hour)
    {
        if (hour < 4f)
            return new Color(15, 15, 40);
        if (hour < 6f)
        {
            float t = (hour - 4f) / 2f;
            return Color.Lerp(new Color(15, 15, 40), new Color(60, 40, 80), t);
        }
        if (hour < 8f)
        {
            float t = (hour - 6f) / 2f;
            return Color.Lerp(new Color(60, 40, 80), new Color(255, 200, 150), t);
        }
        if (hour < 10f)
        {
            float t = (hour - 8f) / 2f;
            return Color.Lerp(new Color(255, 200, 150), Color.White, t);
        }
        if (hour < 17f)
            return Color.White;
        if (hour < 19f)
        {
            float t = (hour - 17f) / 2f;
            return Color.Lerp(Color.White, new Color(255, 180, 100), t);
        }
        if (hour < 21f)
        {
            float t = (hour - 19f) / 2f;
            return Color.Lerp(new Color(255, 180, 100), new Color(30, 20, 60), t);
        }
        return new Color(15, 15, 40);
    }
}