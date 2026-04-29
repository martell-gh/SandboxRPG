using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Npc;

[RegisterComponent("npcLocationTravel")]
public class NpcLocationTravelComponent : Component
{
    [SaveField("finalMapId")]
    public string FinalMapId { get; set; } = "";

    [SaveField("finalX")]
    public float FinalX { get; set; }

    [SaveField("finalY")]
    public float FinalY { get; set; }

    [SaveField("finalAreaId")]
    public string FinalAreaId { get; set; } = "";

    [SaveField("finalPointId")]
    public string FinalPointId { get; set; } = "";

    [SaveField("nextMapId")]
    public string NextMapId { get; set; } = "";

    [SaveField("spawnPointId")]
    public string SpawnPointId { get; set; } = "default";

    [SaveField("triggerId")]
    public string TriggerId { get; set; } = "";

    [SaveField("transitionX")]
    public float TransitionX { get; set; }

    [SaveField("transitionY")]
    public float TransitionY { get; set; }

    public bool FadingOut { get; set; }
    public float FadeSeconds { get; set; }
    public Color OriginalColor { get; set; } = Color.White;

    public Vector2 FinalPosition => new(FinalX, FinalY);
    public Vector2 TransitionPosition => new(TransitionX, TransitionY);
}
