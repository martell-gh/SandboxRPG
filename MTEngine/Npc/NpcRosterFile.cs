using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace MTEngine.Npc;

/// <summary>
/// Структура файла Maps/&lt;settlement&gt;.npc с предсозданными NPC.
/// Каждая запись использует общий шаблон (PrototypeId), но переопределяет
/// поля компонентов и кладёт NPC в дом / на профессию.
/// </summary>
public class NpcRosterEntry
{
    [JsonPropertyName("id")]            public string Id { get; set; } = "";
    [JsonPropertyName("proto")]         public string PrototypeId { get; set; } = "npc_base";

    [JsonPropertyName("ageYears")]      public int AgeYears { get; set; } = 25;

    [JsonPropertyName("identity")]      public NpcRosterIdentity Identity { get; set; } = new();
    [JsonPropertyName("personality")]   public NpcRosterPersonality? Personality { get; set; }
    [JsonPropertyName("skills")]        public Dictionary<string, float> Skills { get; set; } = new();
    [JsonPropertyName("residence")]     public NpcRosterResidence? Residence { get; set; }
    [JsonPropertyName("profession")]    public NpcRosterProfession? Profession { get; set; }
    [JsonPropertyName("kin")]           public List<NpcRosterKin> Kin { get; set; } = new();
    [JsonPropertyName("description")]   public string? Description { get; set; }

    /// <summary>
    /// Персональные overrides компонентов. Формат совпадает с proto.json/components.
    /// Используется для внешности, расписания и любых будущих компонентных правок.
    /// </summary>
    [JsonPropertyName("components")]    public Dictionary<string, JsonObject> Components { get; set; } = new();

    /// <summary>Одежда: slotId -> item prototype id, например torso -> tinted_shirt.</summary>
    [JsonPropertyName("outfit")]        public Dictionary<string, string> Outfit { get; set; } = new();

    /// <summary>Предметы в руках, по порядку.</summary>
    [JsonPropertyName("hands")]         public List<string> Hands { get; set; } = new();

    /// <summary>Предметы в личном инвентаре NPC.</summary>
    [JsonPropertyName("inventory")]     public List<string> Inventory { get; set; } = new();
}

public class NpcRosterIdentity
{
    [JsonPropertyName("firstName")]     public string FirstName { get; set; } = "";
    [JsonPropertyName("lastName")]      public string LastName { get; set; } = "";
    [JsonPropertyName("gender")]        public string Gender { get; set; } = "Male";
    [JsonPropertyName("factionId")]     public string FactionId { get; set; } = "";
    [JsonPropertyName("settlementId")]  public string SettlementId { get; set; } = "";
    [JsonPropertyName("districtId")]    public string DistrictId { get; set; } = "";
}

public class NpcRosterPersonality
{
    [JsonPropertyName("infidelity")]    public int Infidelity { get; set; } = -1;
    [JsonPropertyName("vengefulness")]  public int Vengefulness { get; set; } = -1;
    [JsonPropertyName("childWish")]     public int ChildWish { get; set; } = -1;
    [JsonPropertyName("marriageWish")]  public int MarriageWish { get; set; } = -1;
    [JsonPropertyName("sociability")]   public int Sociability { get; set; } = -1;
    [JsonPropertyName("pacifist")]      public bool Pacifist { get; set; }
}

public class NpcRosterResidence
{
    [JsonPropertyName("houseId")]       public string HouseId { get; set; } = "";
    [JsonPropertyName("bedSlotId")]     public string BedSlotId { get; set; } = "";
}

public class NpcRosterProfession
{
    [JsonPropertyName("slotId")]        public string SlotId { get; set; } = "";
}

public class NpcRosterKin
{
    [JsonPropertyName("npcId")]         public string NpcId { get; set; } = "";
    [JsonPropertyName("kind")]          public string Kind { get; set; } = "Father";
}
