# NPC и A-Life: спека, архитектура, роудмап

Этот документ — единая большая спека по системе НПС, отношений, профессий, родственников, мести и фоновой симуляции жизни города. Он же — план встраивания всего этого в существующий ECS/прототип/save-движок.

Документ намеренно длинный. Цель — чтобы по нему можно было садиться кодить любой кусок, не возвращаясь каждый раз к ChatGPT-формату «расскажи как».

---

## Оглавление

0. [Что уже есть в коде (мост к существующей архитектуре)](#0-что-уже-есть-в-коде)
1. [Время, календарь, старение](#1-время-календарь-старение)
2. [Иерархия мира: фракции / поселения / районы / зоны](#2-иерархия-мира)
3. [Анатомия NPC: какие компоненты и почему](#3-анатомия-npc)
4. [Расписание и фритаймы](#4-расписание-и-фритаймы)
5. [Отношения, брак, ребёнок, измена, переезд](#5-отношения-брак-ребёнок-измена-переезд)
6. [Дети: рост, навыки, наследование](#6-дети-рост-навыки-наследование)
7. [Профессии, безработные, торговля](#7-профессии-безработные-торговля)
8. [Родственники и месть](#8-родственники-и-месть)
9. [Информация о NPC и развитие отношений с игроком](#9-информация-о-npc-и-отношения-с-игроком)
10. [Зональная симуляция (active / background / distant)](#10-зональная-симуляция)
11. [Смерть, пенсия, наследование, «вырезать весь город»](#11-смерть-пенсия-наследование)
12. [Сохранение / загрузка](#12-сохранениезагрузка)
13. [UI: окно «Инфо», заметки, журналы](#13-ui-инфо-заметки-журналы)
14. [Псевдокод ключевых систем](#14-псевдокод-ключевых-систем)
15. [Расширения MTEditor](#15-расширения-mteditor)
16. [Контент: новые папки и прототипы](#16-контент-новые-папки-и-прототипы)
17. [Открытые вопросы и пропуски](#17-открытые-вопросы-и-пропуски)
18. [Роудмап по приоритетам (P0…P9)](#18-роудмап-по-приоритетам)

---

## 0. Что уже есть в коде

Перечисляю только то, на что мы будем опираться или что нужно расширять. Имена файлов реальные.

**ECS-ядро:**
- [Entity.cs](MTEngine/ECS/Entity.cs) — id + словарь компонентов + `PrototypeId`.
- [World.cs](MTEngine/ECS/World.cs) — список entities, список систем, `Update(dt)` + два слоя отрисовки.
- [GameSystem.cs](MTEngine/ECS/GameSystem.cs) — базовый класс системы.
- [Component.cs](MTEngine/ECS/Component.cs) — базовый компонент.

**Регистрация и сериализация:**
- `[RegisterComponent("xxx")]` / `[PrototypeComponent]` — регистрация типа компонента в `ComponentRegistry`.
- `[DataField("xxx")]` — поле читается из `proto.json`.
- `[SaveField("xxx")]` / `[SaveObject("xxx")]` — поле/объект попадает в save.
- [PrototypeManager.cs](MTEngine/Core/PrototypeManager.cs) — рекурсивно грузит все `proto.json` из контента.
- [EntityFactory.cs](MTEngine/Core/EntityFactory.cs) — `CreateFromPrototype(proto, position)` собирает Entity.

**Время:**
- [GameClock.cs](MTEngine/Core/GameClock.cs) — пока хранит только `_totalSeconds` в пределах **одних суток** (clamp к 86400). Для жизни-сима этого мало: нужна непрерывная шкала «всего сыграно секунд». Это первый блокер.
- [DayNightSystem.cs](MTEngine/Systems/DayNightSystem.cs) — управляет цветом окружения по `Hour`.
- [SleepSystem.cs](MTEngine/Systems/SleepSystem.cs) — ускоряет `TimeScale` пока актёр спит, лечит и пр.

**Карты и зоны:**
- [MapData.cs](MTEngine/World/MapData.cs) — карта = тайлы + entities + триггеры + spawn-points.
- [TriggerZone.cs](MTEngine/World/TriggerZone.cs) — набор тайлов + действие. Сейчас единственное действие — `location_transition`. Расширяемо.
- [TriggerSystem.cs](MTEngine/World/TriggerSystem.cs) — рантайм проверки.
- [MapManager.cs](MTEngine/World/MapManager.cs) — грузит/сохраняет карты, эмитит `MapLoadedEvent`.
- [MapEntitySpawner.cs](SandboxGame/Game/MapEntitySpawner.cs) — на `MapLoadedEvent` либо берёт сохранённое состояние, либо спаунит дефолтные entities.

**Сохранения:**
- [SaveGameManager.cs](SandboxGame/Save/SaveGameManager.cs) — делает per-map снапшот entities + per-system state. Игрок сохраняется отдельно. Для NPC всё уже почти готово: их состояния улетят в `MapRuntimeStateData`.

**Прочее:**
- [InfoComponent.cs](MTEngine/Components/InfoComponent.cs) — окно «Инфо» с описанием. Это и есть UI «инфо о NPC», в который мы будем подмешивать факты.
- [HealthComponent.cs](MTEngine/Components/HealthComponent.cs), [WoundComponent.cs](MTEngine/Wounds/WoundComponent.cs) — здоровье/раны/смерть.
- [BedComponent.cs](MTEngine/Metabolism/BedComponent.cs) — кровать. Сейчас односпальная. Будем расширять до двух «слотов сна».
- [HandsComponent.cs](MTEngine/Items/HandsComponent.cs), [EquipmentComponent.cs](MTEngine/Items/EquipmentComponent.cs), [StorageComponent.cs](MTEngine/Items/StorageComponent.cs), [ItemComponent.cs](MTEngine/Items/ItemComponent.cs) — у NPC всё это уже доступно «бесплатно».
- [PlayerTagComponent.cs](MTEngine/Components/PlayerTagComponent.cs) — маркер игрока.
- [EventBus.cs](MTEngine/Core/EventBus.cs) — глобальный паблиш/сабскрайб.
- [ServiceLocator.cs](MTEngine/Core/ServiceLocator.cs) — реестр глобальных сервисов.

**Чего нет вообще:**
- Календаря (`день/неделя/месяц/год`), возраста, дня рождения.
- Понятий «дом», «город», «район», «фракция» как структур данных.
- Профессий и торговли.
- AI/расписания/path-finding для NPC.
- Социальной модели: семья, отношения, измена, родня, месть.
- Зонной симуляции (active/background/distant).

Всё, что ниже — добавление, не правки существующего, кроме узких мест: `GameClock`, `BedComponent`, `TriggerActionTypes`, `MapData` (новый тип «area»).

---

## 1. Время, календарь, старение

### 1.1 Что сломано в `GameClock`

`GameClock._totalSeconds` ограничен 86400 — клампится в `SavedTotalSeconds.set`. Это значит, что после полуночи мы теряем «какой сегодня день».

### 1.2 Что нужно

Новое: `WorldClock` (или расширенный `GameClock`). Хранит:

- `TotalSeconds` (double, монотонная) — сколько игровых секунд прошло с момента «начала мира».
- `TimeOfDaySeconds` — это то, что сейчас зовётся `_totalSeconds`. Считается как `TotalSeconds % SecondsPerDay`.
- `DayIndex = floor(TotalSeconds / SecondsPerDay)`.

Календарь задаётся через JSON-файл, чтобы можно было крутить без перекомпиляции:

```json
// SandboxGame/Content/Data/calendar.json
{
  "secondsPerDay": 86400,
  "daysPerWeek": 7,
  "daysPerMonth": 30,
  "monthsPerYear": 12,
  "monthNames": ["Морозень","Капельник","...","Стуженень"],
  "weekdayNames": ["Пн","Вт","Ср","Чт","Пт","Сб","Вс"],
  "epochYear": 1000
}
```

Класс `Calendar` (в `MTEngine.Core`) загружает это и предоставляет:

```csharp
public readonly struct GameDate {
    public int Year, Month, Day, Weekday;
    public int Hour, Minute;
    public long DayIndex;       // абсолютный индекс дня
    public double TotalSeconds; // монотонная шкала
}

class Calendar {
    int DaysPerWeek, DaysPerMonth, MonthsPerYear;
    int SecondsPerDay = 86400;
    GameDate FromTotalSeconds(double total);
    long DaysBetween(GameDate a, GameDate b);
    int YearsBetween(GameDate a, GameDate b);
}
```

### 1.3 Что меняется в существующем коде

- `GameClock` сохраняем как фасад, но добавляем `TotalSeconds` (double) и `DayIndex`. `TimeOfDaySeconds` остаётся для совместимости, но **не клампим**, а считаем модулем.
- `[SaveField("totalSeconds")]` на новой `TotalSeconds`.
- `Calendar` регистрируем в `ServiceLocator`.
- `DayNightSystem` уже использует `Hour` — не ломается.
- `SleepSystem` сейчас крутит `TimeScale` и в конце ставит `_clock.SetTime(WakeHour)`. Это место поправить, чтобы оно прокручивало `TotalSeconds` к ближайшему утру, а не сбрасывало день.

### 1.4 Старение (player + NPC)

Новый компонент:

```csharp
[RegisterComponent("age")]
public class AgeComponent : Component {
    [DataField("birthDayIndex")] [SaveField("birthDayIndex")]
    public long BirthDayIndex { get; set; } = -1; // -1 = нужно проставить при рождении/спауне

    [SaveField("isPensioner")]
    public bool IsPensioner { get; set; }
}
```

Свойство `Years` считается на лету через `Calendar`:

```csharp
int years = Calendar.YearsBetween(
    Calendar.FromTotalSeconds(Clock.TotalSeconds),
    Calendar.FromDayIndex(Age.BirthDayIndex)
);
```

`AgingSystem` (тикает раз в игровой день):
- если `years >= 65` → `IsPensioner = true` → освобождаем профессию.
- если `years > deathExpectancy` (например 85 ± случайный разброс) → запускаем `Die(NaturalCauses)`.

Отдельная мелочь: при создании NPC из шаблона нужно прописывать стартовый `BirthDayIndex` так, чтобы получился нужный возраст. См. §3.5.

---

## 2. Иерархия мира

Сейчас «локация» = одна `MapData`. Нужно поверх этого ввести логические сущности:

```
Faction
  └─ Settlement (город / деревня)
       └─ District (район; деревня = 1 район)
            ├─ House (помечена area-zoną на карте)
            ├─ Profession area (кузница, таверна и т.д.)
            ├─ School area
            ├─ Inn area (отель)
            ├─ Tavern area
            ├─ Orphanage area (опционально)
            └─ Wander points (фоновые «бродячие» точки)
```

### 2.1 Decoupling: «area-zone» вместо «trigger-zone»

`TriggerZoneData` сейчас — набор тайлов + одно действие. Мы вводим **area-zones**: те же тайлы, но с **семантическими тегами**, без обязательного `Action`. Они нужны не для перехода, а для разметки «это дом такой-то», «это кузница», «это участок для прогулок».

Два варианта реализации:

- **Вариант A (минимальный):** добавить в `TriggerZoneData` опциональные поля `Tags: List<string>` и `Kind: string` (`"trigger"` / `"area"`). Один тип данных, один редактор.
- **Вариант B:** отдельная коллекция `MapData.Areas` со своим типом `AreaZoneData`, где обязательно есть `Kind` и набор тегов/ссылок.

Рекомендую **B** — чище, не путает реальный триггер (он что-то делает) с разметкой. Дальше в документе используется `AreaZoneData`.

```csharp
public class AreaZoneData {
    public string Id { get; set; } = "";          // напр. "house_smith_01"
    public string Kind { get; set; } = "";        // "house" | "profession" | "school" | "inn" | "tavern" | "orphanage" | "wander"
    public Dictionary<string,string> Properties { get; set; } = new();
        // for "house": "settlement"="rivertown", "district"="central"
        // for "profession": "professionId"="blacksmith"
    public List<TriggerTile> Tiles { get; set; } = new();
    public List<AreaPointData> Points { get; set; } = new();   // именованные точки внутри
}

public class AreaPointData {
    public string Id { get; set; } = "";   // "bed_slot_a", "bed_slot_b", "child_bed", "wander_1"
    public int X { get; set; }
    public int Y { get; set; }
}
```

`MapData` получает: `public List<AreaZoneData> Areas { get; set; } = new();`

### 2.2 Глобальный реестр

`WorldRegistry` (новый ServiceLocator-сервис) держит таблицы:

```csharp
class WorldRegistry {
    Dictionary<string, FactionDef> Factions;
    Dictionary<string, SettlementDef> Settlements;       // "rivertown"
    Dictionary<string, DistrictDef> Districts;           // "rivertown.central"
    Dictionary<string, HouseDef> Houses;                 // "house_smith_01"
    Dictionary<string, ProfessionSlotDef> Professions;   // "rivertown.blacksmith_01"

    // обратные индексы
    HouseDef? FindHouseByMapAndTile(string mapId, int x, int y);
    IEnumerable<HouseDef> HousesInDistrict(string districtId);
    IEnumerable<HouseDef> HousesInSettlement(string settlementId);
    IEnumerable<HouseDef> EmptyHousesInSettlement(string settlementId);
}
```

Эти структуры строятся при старте игры путём прохода по всем `MapData.Areas` каждой карты в `Maps/`. Заодно дома/проф-слоты проверяются на наличие нужных point-ов:

- **House:** хотя бы 1 двуспальная кровать = две точки `bed_slot_a` + `bed_slot_b`. Сколько `child_bed`-точек, столько и максимум детей. Опционально точка `door`.
- **Profession area:** point `work_anchor` (где NPC «работает»). Тег профессии в `Properties.professionId`.
- **School:** точки `wander_*` для перемещений ребёнка.
- **Inn:** список `inn_bed_*` точек — кровати для безработных.
- **Tavern, Orphanage:** просто `wander_*` точки.

### 2.3 Дом как сущность

Дом ≠ entity на сцене. Дом = `HouseDef` в реестре. У дома:

```csharp
class HouseDef {
    string Id;
    string MapId;
    string DistrictId;
    string SettlementId;
    string FactionId;
    List<(int x,int y)> Tiles;
    List<AreaPointData> BedSlots;        // парные слоты
    List<AreaPointData> ChildBedSlots;   // ёмкость для детей
    bool ForSale;                        // вычисляется по «есть ли владелец»
    HashSet<int> ResidentNpcIds;         // кто живёт
}
```

`ResidentNpcIds` — динамическое поле, его держит `HouseRegistrySystem`.

---

## 3. Анатомия NPC

NPC = entity с тем же ECS, что и игрок, плюс добавочные компоненты. **Никаких отдельных классов «Npc»** — только новые компоненты на универсальной сущности.

### 3.1 Базовый набор

`Actor` (NPC любого пола/возраста, кроме игрока) собирается из прототипа `Actors/Npc/proto.json` или конкретного шаблона (`Actors/NpcSmith/proto.json`):

- `transform`, `sprite`, `collider`, `velocity` — есть.
- `health`, `wounds` — есть.
- `metabolism` — есть, но для NPC можно урезать decay в 0 чтобы не упарывались (или кормятся фоном).
- `hands`, `equipment`, `currency` — есть.
- `interactable`, `info` — есть. Под «инфо» подмешиваются факты (см. §9).

Новые компоненты, которые мы вводим (все — отдельные файлы в `MTEngine.Npc`):

| Компонент             | Назначение                                                  |
|-----------------------|-------------------------------------------------------------|
| `NpcTagComponent`     | Маркер NPC (для фильтрации запросов).                       |
| `IdentityComponent`   | Имя, фамилия, пол, фракция, settlement/district «прописки». |
| `AgeComponent`        | См. §1.4.                                                   |
| `PersonalityComponent`| Готовность к измене, мстительность, желание иметь детей, желание жениться, пацифист, и пр. (см. §3.2). |
| `SkillsComponent`     | Словарь `string skillId → float level (0..10)`.             |
| `ScheduleComponent`   | Тайм-таблица 24×7 + список «фритайм-предпочтений».          |
| `RelationshipsComponent` | Семейный статус, партнёр, потенциальная дата свадьбы, ссылки на родню. |
| `KinComponent`        | Список родственников: `[(npcId, kind)]`.                    |
| `ResidenceComponent`  | `houseId` (где живёт), `bedSlotId` (какая половина кровати), `isHomeless`. |
| `ProfessionComponent` | `professionSlotId`, `professionId`, `joinedDayIndex`.       |
| `RevengeComponent`    | Состояние «ушёл мстить» (см. §8).                           |
| `ScheduledEventsComponent` | Очередь будущих событий: «начать встречаться», «свадьба», «родить», «зайти к корчмарю» и т.д. |
| `BackgroundFactsComponent` | Сколько фактов уже узнал игрок (для §9).               |

`NpcTagComponent` и `PlayerTagComponent` существуют параллельно — игрок может тоже жениться, иметь возраст и навыки, но он **не управляется AI**.

### 3.2 `PersonalityComponent` — слот случайных черт

```csharp
[RegisterComponent("personality")]
public class PersonalityComponent : Component {
    [DataField("infidelity")] [SaveField] public int Infidelity;       // 0..10
    [DataField("vengefulness")] [SaveField] public int Vengefulness;   // 0..10
    [DataField("childWish")] [SaveField] public int ChildWish;         // 0..10
    [DataField("marriageWish")] [SaveField] public int MarriageWish;   // 0..10
    [DataField("pacifist")] [SaveField] public bool Pacifist;          // true → vengefulness = 0
    [DataField("sociability")] [SaveField] public int Sociability;     // 0..10 (для дружбы с игроком)
}
```

Если значения в прототипе не заданы, спаунер при создании катает их случайно (uniform 0..10), кроме пацифистов — у них `Vengefulness = 0`.

### 3.3 `IdentityComponent`

```csharp
[RegisterComponent("identity")]
public class IdentityComponent : Component {
    [DataField("firstName")] [SaveField] public string FirstName = "";
    [DataField("lastName")]  [SaveField] public string LastName  = "";
    [DataField("gender")]    [SaveField] public Gender Gender    = Gender.Male;

    [DataField("factionId")]    [SaveField] public string FactionId    = "";
    [DataField("settlementId")] [SaveField] public string SettlementId = "";
    [DataField("districtId")]   [SaveField] public string DistrictId   = "";
}
public enum Gender { Male, Female }
```

`Gender` нужен только чтобы запретить «однополая романтика» (по твоему ТЗ).

### 3.4 `SkillsComponent`

```csharp
[RegisterComponent("skills")]
public class SkillsComponent : Component {
    [DataField("values")] [SaveField]
    public Dictionary<string, float> Values { get; set; } = new();
        // "melee_unarmed", "melee_one_handed", "melee_two_handed",
        // "smithing", "tailoring", "cooking", "alchemy" ...
    public float Get(string id) => Values.TryGetValue(id, out var v) ? v : 0f;
    public void Add(string id, float delta) => Values[id] = Math.Clamp(Get(id) + delta, 0f, 10f);
}
```

Список доступных навыков задаётся отдельным JSON-файлом `Data/skills.json`, чтобы не хардкодить:

```json
{
  "skills": [
    { "id": "melee_unarmed", "name": "Рукопашка", "category": "combat" },
    { "id": "melee_one_handed", "name": "Одноручное", "category": "combat" },
    { "id": "melee_two_handed", "name": "Двуручное", "category": "combat" },
    { "id": "smithing", "name": "Кузнечное", "category": "craft" },
    ...
  ]
}
```

### 3.5 Шаблоны NPC и стартовые предсозданные

ТЗ: «нпс созданы руками, но по шаблону».

Решение:

1. Базовый прототип `Actors/NpcBase/proto.json` — содержит **дефолты компонентов**, без имени и без личности.
2. Конкретные предсозданные NPC лежат **на карте** как `MapEntityData` с `componentOverrides` — там прописываются `firstName`, `lastName`, `factionId`, и т.д. (или, для NPC родившихся в игре, шаблон используется напрямую).
3. Есть отдельный JSON-каталог стартовых NPC — `Maps/<settlement>.npcs.json` — оттуда `MapEntitySpawner` после загрузки карты создаёт ещё entity-ков и регистрирует их в реестрах. Это удобнее, чем катать каждый NPC через map-editor вручную.

```json
// Maps/rivertown.npcs.json
[
  {
    "id": "smith_oleg",
    "proto": "npc_base",
    "spawnArea": "house_smith_01",  // или конкретные x/y
    "identity": { "firstName":"Олег", "lastName":"Кузнецов", "gender":"Male",
                  "factionId":"northkingdom", "settlementId":"rivertown", "districtId":"central" },
    "ageYears": 35,
    "personality": { "infidelity": 2, "vengefulness": 6, "childWish": 7, "marriageWish": 8 },
    "skills": { "smithing": 7.5, "melee_one_handed": 4 },
    "residence": { "houseId":"house_smith_01" },
    "profession": { "slotId":"rivertown.blacksmith_01" },
    "kin": [ { "npcId":"smith_oleg_father", "kind":"father" } ]
  },
  ...
]
```

Где `ageYears` — спаунер пересчитает в `BirthDayIndex = currentDayIndex - ageYears * daysPerYear`.

---

## 4. Расписание и фритаймы

### 4.1 Базовая структура

```csharp
[RegisterComponent("schedule")]
public class ScheduleComponent : Component {
    [SaveField] public List<ScheduleSlot> Slots = new();
    [SaveField] public List<FreetimeOption> Freetime = new();
}

public class ScheduleSlot {
    public int StartHour;       // 0..23
    public int EndHour;         // exclusive
    public ScheduleAction Action;       // enum
    public string TargetAreaId;         // куда идти: house / profession / school / wander zone
    public int Priority;                // больше — важнее
}

public enum ScheduleAction {
    Work,           // на проф-area
    Sleep,          // в свою кровать
    EatAtHome,      // дом
    Wander,         // wander-points района/населёнки
    Visit,          // конкретная entity (партнёр на работе и т.п.)
    StayInTavern,   // безработные
    SchoolDay,      // ребёнок
    Free            // → роллит из Freetime
}

public class FreetimeOption {
    public ScheduleAction Action;
    public string? TargetAreaId;
    public int Priority;       // одинаковый = ролл
    public bool DayOnly;
    public bool NightOnly;
    public List<string> Conditions = new(); // напр. "has_partner", "in_relationship"
}
```

### 4.2 Шаблоны расписаний

Чтобы не писать руками для каждого NPC, шаблоны лежат в `Data/schedule_templates.json`:

```json
{
  "default_worker": {
    "slots": [
      {"start":0, "end":6, "action":"Sleep"},
      {"start":6, "end":7, "action":"EatAtHome"},
      {"start":7, "end":12, "action":"Work"},
      {"start":12, "end":13, "action":"EatAtHome"},
      {"start":13, "end":18, "action":"Work"},
      {"start":18, "end":19, "action":"Free"},
      {"start":19, "end":20, "action":"EatAtHome"},
      {"start":20, "end":24, "action":"Sleep"}
    ],
    "freetime": [
      {"action":"Wander", "priority":1},
      {"action":"Visit", "targetAreaId":"$partner_location", "priority":3, "conditions":["has_partner"]},
      {"action":"StayInTavern", "priority":2}
    ]
  },
  "default_unemployed": { ... },
  "default_child": { ... },
  "default_pensioner": { ... }
}
```

Профессия из `ProfessionPrototype` ссылается на нужный шаблон. Дальше при назначении профессии `ScheduleSystem` копирует шаблон в `ScheduleComponent` и подставляет `TargetAreaId`.

### 4.3 Принципы

- **Минимум 1 час в день** — окно `Free`. Гарантируется проверкой при сборке расписания.
- **Фритайм день/ночь различает.** В `Update` фритайма берётся `Clock.IsDay`, фильтруются опции.
- **Резолв при равном приоритете.** Все опции с одинаковым максимальным приоритетом → `random.PickWeighted`.
- **Совпадение фритаймов с партнёром.** Если опция = `Visit` и партнёр сейчас тоже в `Free`, она получает буст приоритета (`+5`). Это автоматом даёт «гуляют вместе».

### 4.4 `ScheduleSystem`

```csharp
class ScheduleSystem : GameSystem {
    void Update(float dt) {
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, ScheduleComponent>()) {
            if (!IsActiveZone(npc)) continue;       // в фоне дёргается реже, см. §10
            var slot = ResolveCurrentSlot(npc, Clock.HourInt);
            ExecuteSlot(npc, slot);
        }
    }
}
```

`ExecuteSlot` уже не двигает руками — он публикует «намерение» (target area + action). А **движение** = задача `NpcMovementSystem` + `Pathfinder` (см. §14.5).

---

## 5. Отношения, брак, ребёнок, измена, переезд

Это самый «социальный» блок. Чтобы не утонуть, разделим на сущность и события.

### 5.1 `RelationshipsComponent`

```csharp
[RegisterComponent("relationships")]
public class RelationshipsComponent : Component {
    [SaveField] public RelationshipStatus Status = RelationshipStatus.Single;
    [SaveField] public int? PartnerNpcId;            // NPC-партнёр
    [SaveField] public bool PartnerIsPlayer;         // если игрок

    [SaveField] public long? ScheduledDateDayIndex;     // когда начнётся «встречаются»
    [SaveField] public long? ScheduledWeddingDayIndex;  // когда свадьба
    [SaveField] public long? ScheduledBirthDayIndex;    // когда следующий ребёнок

    [SaveField] public long LastMatchSearchDayIndex;    // чтобы дёргать поиск раз в 7 дней

    [SaveField] public int OvernightStreak;          // сколько ночей подряд оставались
}

public enum RelationshipStatus { Single, Dating, Engaged, Married, Widowed, Separated }
```

### 5.2 Поиск пары

`MatchmakingSystem` тикает раз в игровой день (или по требованию):

```
foreach npc in EligibleSingles:
    if npc.Relationships.LastMatchSearchDayIndex + 7 > today: continue
    npc.Relationships.LastMatchSearchDayIndex = today
    candidates = WorldRegistry
        .NpcsInResidenceLocation(npc.IdentityComponent)   // свой район/деревня
        .Where(other => Eligible(npc, other))
    if candidates.Empty: continue

    pair = candidates.PickRandom()
    # вычислить даты:
    daysToDate    = avg(npc.RandomDays(2..14),    pair.RandomDays(2..14))
    daysToWedding = avg(npc.RandomDays(30..120),  pair.RandomDays(30..120))

    Both.Relationships.PartnerNpcId = each-other.Id
    Both.Relationships.Status       = Single  # они ещё не встречаются
    Both.Relationships.ScheduledDateDayIndex    = today + daysToDate
    Both.Relationships.ScheduledWeddingDayIndex = today + daysToDate + daysToWedding
```

`Eligible(a, b)` проверяет:
- разные пол (по ТЗ только разнополые),
- оба `Single`,
- возраст обоих ≥ 18 (точнее ≥ совершеннолетия из настроек),
- если у `a.Personality.Infidelity == 0` и у `b.Personality.Infidelity == 0`, то и они не в отношениях с игроком,
- не близкие родственники (через `KinComponent`).

Разбросы 2..14 / 30..120 настраиваются в `Data/lifesim_tuning.json`.

### 5.3 Начало отношений

`RelationshipTickSystem` тикает раз в день. Срабатывает в момент `today >= ScheduledDateDayIndex`:
- если оба сейчас в одном районе и их пути пересекутся (см. фритайм совпадает) — на ближайшем общем фритайме они «встречаются»;
- статус `Status = Dating`;
- публикуется `RelationshipStarted` в `EventBus`.

«Встречаются» означает в коде:
- ставим `Visit` цели друг друга в Freetime с высоким приоритетом,
- по ночам с шансом `p(overnight)` оба идут в один дом (см. ниже).

### 5.4 Ночёвка вместе

```
p_overnight(today) = clamp((today - dateDay) / (weddingDay - dateDay), 0.1, 0.95)
```

Если ролл удался — оба ночуют в доме того, у кого статус «активный жилец» (по умолчанию мужчины, можно случайно выбрать). Партнёр временно занимает второй `bed_slot` дома.

### 5.5 Свадьба

Срабатывает в `today == ScheduledWeddingDayIndex`:

- `Status = Married`;
- жена переезжает: `ResidenceComponent.HouseId` = дом мужа;
- старый дом жены: `HouseDef.ResidentNpcIds.Remove(wife)`. Если у дома больше нет жильцов → `HouseDef.ForSale = true` (на UI ничего не делаем; на «продажу» подвязываем переезд других NPC из других городов и игрока, см. §11.4).
- катаем `ScheduledBirthDayIndex` (см. §6).

### 5.6 Готовность к измене и игрок

Кейсы:

1. **NPC в отношениях, игрок хочет с ним романтику.**
   - `Infidelity == 0`: невозможно. UI скрывает соответствующие реплики/действия.
   - `Infidelity == 10`: романтика идёт как с одиноким (партнёр игнорируется).
   - `1..9`: требуется дополнительный «уровень доверия» от игрока, прогресс копится медленнее в `RelationshipWithPlayerComponent` (см. §9).

2. **Игрок развил отношения «достаточно».** В EventBus летит `PlayerProposedSeparation(npc)`. Логика:
   - текущий `PartnerNpcId` получает `Status = Separated`, `LastMatchSearchDayIndex = today` (он будет искать новую пару через 7 дней).
   - наш `npc` получает `PartnerNpcId = player.Id`, `PartnerIsPlayer = true`, `Status = Engaged → Married` через короткий разброс.

3. **Игрок женат, у партнёра-NPC высокая измена, игрока долго нет.**
   `PlayerCohabitationSystem` смотрит на (`today - lastSeenWithPartner`) против `Personality.Infidelity * cheatSensitivity`. Если превышено:
   - **с шансом** партнёр уходит к рандомному eligible NPC в его текущей жилой локации:
     - оставляет в доме игрока entity «записка» (новый прототип `Note`, см. §13.2),
     - `Relationships.Status = Separated`, `PartnerNpcId = null`, `PartnerIsPlayer = false`.
   - **с другим шансом** телепортируется (только когда игрок входит в локацию):
     - случай 1: тот же settlement, другой district (если есть);
     - случай 2: settlement той же фракции;
     - случай 3 (низкий вес): settlement другой фракции;
   - в новом settlement: ищется свободный дом, NPC заселяется, входит в Matchmaking (как §5.2). Записка в покинутом доме появляется.

«Только когда игрок входит в локацию» — флаг `PendingRelocation` в `RelationshipsComponent`. Срабатывает на событии `MapLoadedEvent` для исходной локации.

### 5.7 Сводка событий по EventBus

- `RelationshipDateScheduled(npcA, npcB, dateDay, weddingDay)`
- `RelationshipStarted(npcA, npcB)`
- `RelationshipMarried(npcA, npcB)`
- `RelationshipSeparated(npcA, npcB, cause)`
- `NpcMovedHouse(npc, oldHouse, newHouse)`

Эти эвенты — **точки расширения** (квесты, диалоги, журнал игрока).

---

## 6. Дети: рост, навыки, наследование

### 6.1 Начало

После свадьбы (или после «достижения отношений» с игроком, если у игрока тоже есть `AgeComponent` и он в фертильном возрасте):

```
each game-month:
  if both have AgeComponent in [18..40]:
      avgWish = (wifeChildWish + husbandChildWish) / 2
      p = avgWish * 0.10                      # 10% за единицу
      if random < p:
          if HouseHasFreeChildBedSlot(house):
              schedule birth: today + random(2..5) * daysPerWeek
              ScheduledBirthDayIndex = ...
              # после рождения каждый wish делится пополам
```

Если все `child_bed`-слоты заняты или их нет — `ScheduledBirthDayIndex` не назначается.

### 6.2 Рождение

`BirthSystem` слушает `today >= ScheduledBirthDayIndex`:
- создаёт нового NPC (`npc_base`),
- пишет в `IdentityComponent.LastName` фамилию отца, `FirstName` — рандом из `Data/names_<faction>.json` по полу,
- `AgeComponent.BirthDayIndex = today`,
- `ResidenceComponent.HouseId = parents.house`, `BedSlotId = первый свободный child_bed`,
- `KinComponent` на ребёнке: father/mother, на родителях добавляем child,
- `SkillsComponent` — см. §6.3,
- `ScheduleComponent` копируется из `default_child`,
- желания обоих родителей: `ChildWish /= 2`, `ScheduledBirthDayIndex = null`.

### 6.3 Наследование навыков

Целевые значения навыков ребёнка фиксируются на момент рождения:

```
foreach skill in skills:
    parentMax = max(father.skills[skill], mother.skills[skill])
    parentAvg = (father.skills[skill] + mother.skills[skill]) / 2
    if skill == bestSkill(father) or skill == bestSkill(mother):
        target = 0.8 * parentMax
    else:
        target = parentAvg
    child.skills[skill] = 0
    child.targetSkills[skill] = target
```

Сохраняем `targetSkills` в `ChildGrowthComponent`:

```csharp
[RegisterComponent("childGrowth")]
public class ChildGrowthComponent : Component {
    [SaveField] public Dictionary<string,float> TargetSkills = new();
}
```

### 6.4 Рост во времени

`ChildGrowthSystem` (тикает раз в игровой день):

```
ageYears = years(child)
if ageYears < 18:
    progress = ageYears / 18.0
    foreach skill in TargetSkills:
        child.skills[skill] = TargetSkills[skill] * progress
else:
    # Совершеннолетие
    child.RemoveComponent<ChildGrowthComponent>()
    JobMarketSystem.AddSeeker(child)
    HouseRegistry.AssignFreeHouseInSettlement(child)
```

Никакого «кача от тренировок» до 18. Это сильно упрощает.

### 6.5 Расписание ребёнка

Шаблон `default_child`:

```
00..08 Sleep
08..14 [SchoolDay if school exists else Wander]
14..18 [Visit parent at work] OR [Wander]
18..20 EatAtHome
20..00 Sleep
```

«SchoolDay» = заходит в area `kind=school` и тикает по `wander_*` точкам школы.

---

## 7. Профессии, безработные, торговля

### 7.1 Прототип профессии

Новая категория прототипов: `category: "profession"`. Лежит в `Content/Prototypes/Professions/Blacksmith/proto.json`:

```json
{
  "id": "blacksmith",
  "category": "profession",
  "name": "Кузнец",
  "primarySkill": "smithing",
  "scheduleTemplate": "default_worker",
  "trades": true,
  "tradeKinds": ["weapon_melee", "weapon_armor"],
  "stockSize": [4, 12],
  "restockEveryDays": 7,
  "skillGainPerDay": 0.005
}
```

`PrototypeManager` регистрирует это в новом словаре `_professions: Dictionary<string, ProfessionPrototype>`.

### 7.2 Площадка профессии

Площадка профессии — это `AreaZoneData(Kind="profession", Properties.professionId="blacksmith")`. Также там обязан быть `work_anchor` point.

Регистрируется в `WorldRegistry.Professions` как `ProfessionSlotDef`:

```csharp
class ProfessionSlotDef {
    string Id;                  // "rivertown.blacksmith_01"
    string ProfessionId;        // "blacksmith"
    string SettlementId;
    string DistrictId;
    string MapId;
    AreaPointData WorkAnchor;
    int? OccupiedNpcId;
    long? OccupiedSinceDayIndex;
}
```

### 7.3 Подбор работы

`JobMarketSystem` тикает при:
- появлении нового совершеннолетнего,
- освобождении вакансии (смерть/переезд/пенсия NPC).

```
foreach settlement:
    seekers = NpcsInSettlement.Where(n => n.Profession == null && n.Age >= 18 && !pensioner)
    vacancies = ProfessionSlots[settlement].Where(p => p.OccupiedNpcId == null)
    foreach v in vacancies:
        primary = professionProto[v].PrimarySkill
        best = seekers.OrderByDescending(n => n.Skills[primary]).FirstOrDefault()
        if best != null:
            AssignProfession(best, v)
            seekers.Remove(best)
```

`AssignProfession`:
- `ProfessionComponent { ProfessionId, ProfessionSlotId }`,
- `ScheduleComponent` ← из шаблона профессии,
- если у NPC ещё нет дома — `HouseRegistry.AssignFreeHouseInSettlement`.

### 7.4 Безработные

NPC без профессии и без `ProfessionComponent`:
- ночуют в `Inn` area (на свободной `inn_bed_*` точке, ставит `BedSlotId` в `ResidenceComponent`); если все заняты — расписание `Free` всю ночь, идёт в `Tavern` или `Wander`.
- Днём — `Free` всегда.
- Если игрок женится на безработном NPC и оставляет его дома → `ResidenceComponent.HouseId = playerHouse`, `JobMarketSystem.AddSeeker(npc)` (он начинает участвовать).
- Если он компаньон (за игроком таскается) — `JobMarketSystem.RemoveSeeker(npc)`.

### 7.5 Прокачка профессии

`ProfessionTickSystem` раз в игровой день:

```
foreach npc in NpcsWithProfession in active|background zones:
    skill = professionProto[npc].PrimarySkill
    npc.skills.Add(skill, professionProto[npc].SkillGainPerDay)
```

В distant зоне можно вообще не считать — за неделю накапливается крошечная разница, но если хочется честно, делаем «catch-up» при загрузке зоны (см. §10.3).

### 7.6 Торговля

`ShopComponent` — добавляется на entity на `work_anchor` (по сути «прилавок»):

```csharp
[RegisterComponent("shop")]
public class ShopComponent : Component {
    [SaveField] public string ProfessionSlotId;
    [SaveField] public List<int> StockEntityIds = new();   // entity-id товаров
    [SaveField] public long NextRestockDayIndex;
}
```

Restock-алгоритм:

```
skill = ownerNpc.skills[primarySkill]   # 0..10
tier  = ResolveTier(skill)              # 4 -> 1 (4 = низший, 1 = лучший)
# в proto предметов добавляем поле "tier"
candidates = AllItemPrototypes
    .Where(p => p.Tags ∩ professionProto.tradeKinds)
    .Where(p => p.tier <= tier)
amount = lerp(stockSizeMin, stockSizeMax, skill / 10)
shop.StockEntityIds = pick `amount` items, c повторами разрешено
```

Тиры лежат в `proto.json` каждого предмета:

```json
{
  "id": "iron_sword",
  "category": "entity",
  "components": { "item": { "tags": ["weapon_melee"] } },
  "tier": 3
}
```

ShopComponent не привязывает физически предметы в storage в фоне — он просто хранит «список proto + qty» и материализует entity предмета только когда игрок открывает торговое окно. Иначе будут тысячи висящих entity по всем городам.

### 7.7 «У NPC нет профессии»

Безработный NPC — обычная штатная ситуация (см. §7.4). Если в settlement свободна профессия и подходящих кандидатов нет (мало народа), — слот висит, `JobMarketSystem` повторяет попытку при каждом изменении `seekers` или раз в день.

---

## 8. Родственники и месть

### 8.1 `KinComponent`

```csharp
[RegisterComponent("kin")]
public class KinComponent : Component {
    [SaveField] public List<KinLink> Links = new();
}
public struct KinLink {
    public int NpcId;          // или string SaveId, см. §12.1
    public KinKind Kind;
}
public enum KinKind { Father, Mother, Spouse, Child, Sibling }
```

Два NPC всегда хранят зеркальные ссылки. `RelationshipsSystem` отвечает за их синхронизацию при свадьбах/рождениях/смертях.

### 8.2 Триггер мести

Слушаем `EntityDied(victim, cause, killer)`. (Это новый эвент; добавим в `HealthSystem`.)

```
if !(victim has KinComponent): return
if killer is null or killer != player and not player-allied: return

foreach link in victim.Kin.Links:
    grieving = WorldRegistry.GetNpc(link.NpcId)
    if grieving.Personality.Pacifist: continue
    if grieving.Personality.Vengefulness == 0: continue

    behavior = ResolveRevengeBehavior(grieving.Personality.Vengefulness)
    grieving.Add(new RevengeTrigger {
        Cause = link.Kind,
        TriggerAfterDayIndex = today + random(5..20)   # либо до совершеннолетия для ребёнка
    })
```

`ResolveRevengeBehavior`:

| Vengefulness | Поведение                                                 |
|--------------|-----------------------------------------------------------|
| 0            | Ничего.                                                   |
| 1..2         | `MerchantPriceMultiplier` += 1.5 (если торгует). Никакой агрессии. |
| 3..5         | + переход в Hostile при встрече с игроком, без преследования. |
| 6..7         | + «по фриатйму» с малым шансом начинает искать игрока в текущей зоне. |
| 8..10        | Полная процедура «Avenger». См. §8.3.                     |

### 8.3 Avenger-процедура (Vengefulness ≥ 8)

`RevengeSystem` тикает раз в игровой день:

```
when today >= grieving.RevengeTrigger.TriggerAfterDayIndex
       AND not (grieving is child)  # дети ждут совершеннолетия:
    1. Снять профессию: ProfessionSlot освобождается.
    2. Добавить компонент AvengerComponent.
    3. Прокачать на 5 уровней случайный из ["melee_unarmed","melee_one_handed","melee_two_handed"].
    4. Если выбран не unarmed — выдать оружие (любое подходящее из общего пула).
    5. Расписание = AvengerSchedule (24/7 преследование).
    6. Скорость движения слегка повышена.

each tick:
    move toward player.lastKnownPosition (или текущей, если в той же карте)
    on enter player's location: chase_state = true
    on adjacent tile to player AND player.NotSleeping:
        attack
        say once "Ты убил моего близкого!"
    on player.IsSleeping:
        stand still on tile (по ТЗ — не атакует спящего)
```

Важно: «следует слепо» = не сворачивает на свои дела. Просто всегда идёт к игроку (см. §14.5 — pathfinding между картами через цепь locations).

### 8.4 «Если убить ребенка-сироту, у которого нет родителей в живых»

Если у убитой жертвы `KinComponent.Links.Empty` — никого не запускаем. Это ОК — ТЗ так и подразумевает.

### 8.5 Если убить **всех** в городе

Просто следствие правил выше:
- кто остаётся (вне settlement) — тот мстит, по правилам §8.2.
- `JobMarketSystem` не находит кандидатов → вакансии висят.
- `MatchmakingSystem` не находит eligibles → ничего.
- `HouseRegistry` фиксирует все дома `ForSale`.
- В будущем: «дрейф населения» — `MigrationSystem` раз в N дней может перевозить NPC из соседних settlements в пустые (сейчас не делаем, см. §17).

---

## 9. Информация о NPC и отношения с игроком

### 9.1 `RelationshipWithPlayerComponent`

```csharp
[RegisterComponent("playerRel")]
public class RelationshipWithPlayerComponent : Component {
    [SaveField] public int Friendship;     // 0..100
    [SaveField] public int Romance;        // 0..100, заблокирован для одного пола
    [SaveField] public int FactsRevealed;  // см. §9.2
}
```

При первом разговоре игрока с NPC компонент дописывается, если его нет.

### 9.2 Узнавание фактов

Список «открываемых фактов» о NPC:

1. Лучший навык (`bestSkill(npc)` по `SkillsComponent.Values`).
2. Случайный родственник (`KinLink` + кем приходится).
3. Профессия.
4. Город/район жительства.
5. Семейный статус.
6. ... (расширяется).

`InfoComponent` уже умеет показывать описание. Расширяем:
- Если у entity есть `IdentityComponent` (то есть это NPC), окно информации добавляет блок «О персонаже» из фактов, которые `FactsRevealed` уже разрешает.
- Каждое успешное «приятное» взаимодействие имеет шанс открыть один новый факт. Шанс растёт от `Friendship`.
- Жена/муж игрока — все факты автоматом.

### 9.3 Развитие отношений

UI и механика бесед — отдельная тема (квесты, диалоги). Сейчас минимум:
- интеракция «Поговорить» (в `IdentityComponent.GetInteractions` или отдельном `DialogueComponent`),
- нажатие на «Поговорить» прибавляет `Friendship += 1` (с кулдауном раз в N часов),
- предусмотреть точку расширения: `IDialogTopic` (для будущих квестовых диалогов).

---

## 10. Зональная симуляция

Принцип LOD как в `Dwarf Fortress` / `Rimworld` / `Kenshi`:

| Зона       | Что симулируется                                  | Тик                          |
|------------|---------------------------------------------------|------------------------------|
| **Active** | Игрок на этой карте. Полная AI-петля.             | Каждый кадр.                 |
| **Background** | NPC в settlements рядом (1–2 локационных перехода). | Раз в N игровых часов (например 1 час) — большим батчем. |
| **Distant** | Все остальные.                                    | Раз в N игровых дней (например 1 день) — только статистика. |

### 10.1 Определение зоны

`LocationGraph` — направленный граф между картами, рёбра соответствуют `location_transition`-триггерам. Билдится при старте.

```csharp
class LocationGraph {
    int Distance(string fromMapId, string toMapId);
}
```

`SimulationLodSystem.GetZone(npc)`:
```
playerMap = current map
npcMap    = npc.IdentityComponent.SettlementId.MapId
d         = LocationGraph.Distance(playerMap, npcMap)
if d == 0: Active
else if d <= 2: Background
else: Distant
```

### 10.2 Что отрабатывается в каждой зоне

**Active:**
- `ScheduleSystem` (каждый кадр),
- `NpcMovementSystem` (каждый кадр),
- `RelationshipsTickSystem` (раз в день),
- `MatchmakingSystem` (раз в день),
- `BirthSystem` / `AgingSystem` (раз в день),
- `ProfessionTickSystem` (раз в день),
- `RevengeSystem` (раз в день; преследование = каждый кадр когда NPC в Active).

**Background:**
- `ScheduleSystem` отключён (NPC не двигаются физически),
- логические события (`MatchmakingSystem`, `BirthSystem`, `RelationshipsTickSystem`, `RevengeSystem`) — РАБОТАЮТ как в Active. Это критично: месть, рождение, переезд должны происходить даже когда игрока нет.
- `NpcMovementSystem` не работает; позиции NPC замораживаются.

**Distant:**
- Только агрегированная статистика: рождение/свадьба/смерть/назначение профессии. Реализуется как `BatchedSimulationSystem`:
  - тик = 1 раз в игровой день,
  - проходит по settlements в Distant,
  - в каждом settlement набирает «лимит событий» (например 1 свадьба, 1 рождение, 1 смерть) и катает их случайно.
- Не используются ScheduleComponent / Movement.

### 10.3 Catch-up при пересечении зон

Когда игрок входит в settlement, который был Distant → теперь Active:
- проиграть в ускоренном темпе все pending-события: рождения, свадьбы, переезды, разрывы;
- прокачать `ProfessionTickSystem.SkillGainPerDay * daysSinceLastVisit` на каждом NPC с профессией;
- прокатить `RevengeTrigger` для тех, у кого `TriggerAfterDayIndex` уже прошёл (если ещё не запустились в Background).

Это даёт «жизнь шла без меня» без лагов в момент входа.

### 10.4 Приоритезация важных событий

ТЗ: «важные моменты обрабатывать фоново и далеко (месть к примеру), а неважные убирать по мере удаления».

Решение через теги-важности на эвентах:

```csharp
class ScheduledLifeEvent {
    long DayIndex;
    LifeEventKind Kind;       // Wedding, Birth, Revenge, ...
    LifePriority Priority;    // Low, Medium, High
}

enum LifePriority { Low, Medium, High }
```

В Distant-симуляции пропускаются события `Low`. `High`-события (месть, смерть протагониста квеста) обрабатываются всегда. Это решает кейс «месть NPC, который сейчас на другом краю карты».

---

## 11. Смерть, пенсия, наследование

### 11.1 Естественная смерть

`AgingSystem` раз в день:
- `years > deathExpectancy` → `DieFromAge(npc)`.
- `years > 65` → `IsPensioner = true`. `JobMarketSystem` освобождает слот.

### 11.2 Что происходит при смерти NPC

`DeathSystem` (новая система, слушает `EntityDied`):

```
// 1. Имущество в доме остаётся (можно потом грабить)
// 2. Освободить проф-слот
// 3. Снять с дома владельца:
//    if ResidentNpcIds.empty after: HouseDef.ForSale = true
// 4. KinComponent родственников: помечаем кого-то Widowed
//    spouse.RelationshipsComponent.Status = Widowed
//    spouse.PartnerNpcId = null
//    spouse.LastMatchSearchDayIndex = today (через 7 дней пойдёт искать снова)
// 5. Если у умершего были несовершеннолетние дети без второго родителя:
//    forEach child: if no living parent: 
//        if Settlement has Orphanage area:
//            child.ResidenceComponent.HouseId = orphanage.Id
//            child.BedSlotId = orphanage.AssignFreeBed()
//        else:
//            child остаётся в родительском доме (по ТЗ),
//            HouseDef.ForSale = false (есть жилец = ребёнок), всё остальное по умолчанию.
// 6. EventBus: NpcDied(npc, cause, killerOpt)
// 7. Если killer == player → запустить Revenge для всех KinComponent.Links (см. §8.2)
```

### 11.3 Пенсия

`Personality` — никаких изменений. Просто:
- `IsPensioner = true`,
- `Schedule` = `default_pensioner` (много `Wander`, `EatAtHome`, ранний сон),
- профессия снята.

### 11.4 «Дом продан» = что это значит

`HouseDef.ForSale = true` — это просто атрибут. Используется:
- при `Matchmaking` — eligibles, у которых нет дома (например мигрант), могут «купить»;
- игрок может купить через NPC-нотариуса (этот функционал — уже квестовая надстройка, не базовый AI).

---

## 12. Сохранение / загрузка

### 12.1 Идентификация NPC между перезагрузками

Сейчас `Entity.Id` = инкремент в рантайме, не подходит для save. Уже есть `SaveEntityIdComponent` с `SaveId` (Guid) — будем использовать его.

Все ссылки между NPC (`PartnerNpcId`, `KinLink.NpcId`, `OccupiedNpcId` в `ProfessionSlotDef` и т.д.) храним как **`string SaveId`**, а не как `int`.

В рантайме строится индекс `Dictionary<string, Entity>`, по которому делаются lookup-ы. После загрузки карты этот индекс пересобирается.

### 12.2 Что сохраняется

**В `MapRuntimeStateData` (как сейчас):**
- entities этой карты, со всеми компонентами через `[SaveField]`.

**Глобально, в `SystemStates` `SaveSessionData`:**
- `WorldClock` (TotalSeconds + DayIndex).
- `Calendar` (актуальная конфигурация — на случай если игрок поменял JSON между сессиями).
- `WorldRegistry`:
  - `Settlements`, `Districts`, `Houses` (id-ишники, ссылки на map+area-id, ResidentNpcIds).
  - `Professions` (id, OccupiedNpcId, OccupiedSinceDayIndex).
- `MigrationLog` (история переездов — для дебага, опционально).
- Кэш distant-NPC (см. §12.3).

### 12.3 Distant-NPC: «лёгкие» сейвы

NPC из distant settlements физически не существуют как `Entity` в `World` (иначе тысячи entity), но их состояния нужно где-то хранить. Решение:

- `WorldPopulationStore` (новый сервис) хранит `Dictionary<string, NpcSnapshot>`, где `NpcSnapshot` — POCO со всеми важными полями (Identity, Age, Skills, Personality, Relationships, Profession, Residence, Kin, ScheduledEvents).
- При попадании NPC в Active или Background зону — `World.CreateEntity` собирается из снапшота через тот же механизм `EntityFactory + RestoreEntities`, что и SaveGameManager.
- При выходе из Background обратно в Distant — компоненты снова сериализуются в `NpcSnapshot`, entity уничтожается.
- `WorldPopulationStore` целиком ложится в save.

Это требует расширить `SaveComponentSerializer` — он уже умеет сериализовать компоненты по `[SaveField]`, нужно дать API «преобразовать к JsonObject и обратно без живой Entity».

### 12.4 Что меняется в `SaveGameManager`

- `RegisterSaveObject(WorldClock)` (вместо `Clock`).
- `RegisterSaveObject(Calendar)`.
- `RegisterSaveObject(WorldRegistry)`.
- `RegisterSaveObject(WorldPopulationStore)`.
- В `RestorePlayerEntities` вместо отдельного Player — общий путь восстановления, потому что NPC-партнёр игрока (компаньон) технически не на карте, а «таскается» — его легче считать частью PlayerEntities (как `ItemComponent.ContainedIn`).

---

## 13. UI: инфо, заметки, журналы

### 13.1 Окно «Инфо» NPC

[InfoComponent.cs](MTEngine/Components/InfoComponent.cs) расширяется так:

```csharp
private static void OpenInfoWindow(Entity target) {
    ...
    var npc = target.GetComponent<IdentityComponent>();
    if (npc != null) {
        // под description добавляем блок:
        // - Имя, фамилия, пол, возраст
        // - Семейный статус
        // - Открытые факты (FactsRevealed)
    }
}
```

### 13.2 Прототип «Записка»

```json
// Content/Prototypes/Furniture/Note/proto.json
{
  "id": "note",
  "category": "entity",
  "components": {
    "sprite": { "source": "sprite.png", ... },
    "transform": { "x": 0, "y": 0 },
    "info": { "description": "Сложенный лист бумаги." },
    "interactable": { "name": "Записка", "range": 48 },
    "note": { "text": "" }
  }
}
```

Новый компонент `NoteComponent`:

```csharp
[RegisterComponent("note")]
public class NoteComponent : Component, IInteractionSource {
    [DataField("text")] [SaveField("text")] public string Text = "";
    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx) {
        yield return new InteractionEntry {
            Id = "note.read", Label = "Прочитать", Priority = 12,
            Execute = c => OpenTextWindow(Owner!, Text)
        };
    }
}
```

Когда NPC уходит из дома игрока — `NoteSpawner` создаёт `Note` entity на тайле двери дома игрока с `Text = "Я ушёл к другому. Не ищи меня. — <Имя>"`.

### 13.3 Журнал событий (опционально, низкий приоритет)

Аккумулирует жизненные события, видимые игроку (свадьба знакомого NPC, смерть, ребёнок партнёра). Базовый `JournalSystem` слушает EventBus и пишет в `JournalComponent` на игроке. UI — позже.

---

## 14. Псевдокод ключевых систем

Здесь — самые важные циклы. Смотри как «канон» при имплементации.

### 14.1 `WorldClock.Update` (расширенный `GameClock`)

```csharp
void Update(float dt) {
    var prevDay = DayIndex;
    TotalSeconds += dt * TimeScale;
    DayIndex = (long)(TotalSeconds / SecondsPerDay);
    if (DayIndex != prevDay) {
        EventBus.Publish(new DayChanged(DayIndex));
    }
}
```

Все системы, которые раньше «тикали раз в день», подписываются на `DayChanged`.

### 14.2 `MatchmakingSystem.OnDayChanged`

```csharp
void OnDayChanged(DayChanged e) {
    foreach (var settlement in WorldRegistry.Settlements.Values) {
        var residents = settlement.GetActiveOrBackgroundResidents()
            .Concat(settlement.GetDistantSnapshots().AsLiveProxies());
        var males   = residents.Where(IsEligibleSingle).Where(n => n.Gender == Male).ToList();
        var females = residents.Where(IsEligibleSingle).Where(n => n.Gender == Female).ToList();

        // случайно перебираем
        Shuffle(males); Shuffle(females);
        for (int i = 0; i < Math.Min(males.Count, females.Count); i++) {
            var a = males[i]; var b = females[i];
            if (e.DayIndex - a.LastMatchSearchDay < 7) continue;
            if (e.DayIndex - b.LastMatchSearchDay < 7) continue;
            a.LastMatchSearchDay = e.DayIndex;
            b.LastMatchSearchDay = e.DayIndex;
            // ролл: сходятся?
            if (Random.NextDouble() < MatchProbability(a, b)) {
                ScheduleDates(a, b, e.DayIndex);
            }
        }
    }
}
```

`MatchProbability` зависит от `MarriageWish` обоих и того, в одном ли районе.

### 14.3 `RelationshipsTickSystem.OnDayChanged`

```csharp
void OnDayChanged(DayChanged e) {
    var today = e.DayIndex;
    foreach (var npc in AllRelevantNpcs()) {
        var r = npc.Get<RelationshipsComponent>();
        if (r == null) continue;

        if (r.ScheduledDateDayIndex == today && r.Status == Single)
            r.Status = Dating;

        if (r.ScheduledWeddingDayIndex == today && r.Status == Dating) {
            DoWedding(npc, ResolvePartner(npc));
        }

        if (r.Status == Married && r.PartnerNpcId != null) {
            TryScheduleNextChild(npc, today);
        }

        if (r.ScheduledBirthDayIndex == today) {
            DoBirth(npc, ResolvePartner(npc));
        }
    }
}
```

`DoWedding`, `DoBirth` — описаны в §5.5, §6.2.

### 14.4 `RevengeSystem.OnDayChanged`

```csharp
void OnDayChanged(DayChanged e) {
    foreach (var trigger in PendingRevengeTriggers) {
        var npc = WorldRegistry.GetNpc(trigger.NpcSaveId);
        if (npc.Age.Years < 18 && trigger.Cause == KinKind.Father /*sample*/) continue;
        if (e.DayIndex < trigger.TriggerAfterDayIndex) continue;

        var v = npc.Personality.Vengefulness;
        if (v >= 8) StartAvenger(npc);
        else if (v >= 6) AddOpportunisticHunter(npc);
        else if (v >= 3) MarkHostileToPlayer(npc);
        else if (v >= 1) ApplyMerchantPenalty(npc);

        Pending.Remove(trigger);
    }
}
```

### 14.5 `NpcMovementSystem` + `Pathfinder`

ECS-движок не имеет path-finding. Минимум — A* по `TileMap.IsSolid`:

```csharp
class GridPathfinder {
    List<Point> FindPath(TileMap map, Point from, Point to);  // A*
}

class NpcMovementSystem : GameSystem {
    void Update(float dt) {
        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, ScheduleComponent, TransformComponent>()) {
            var intent = ScheduleSystem.CurrentIntent(npc);
            var target = ResolveWorldPoint(intent);
            if (target == null) continue;
            var path = npc.GetComponent<NpcPathComponent>() ?? Pathfinder.FindPath(...);
            FollowPath(npc, path, dt);
        }
    }
}
```

Между картами — отдельная задача:
- если `intent.TargetMapId != currentMapId`:
  - в Active: вести NPC к ближайшему `location_transition`-триггеру, ведущему в нужную сторону (BFS по `LocationGraph`);
  - в Background: телепорт на следующую карту по графу со временем = `transitionDistance × walkSpeedFactor`;
  - в Distant: телепорт мгновенно (или через 1 «тиковый» день).

`Avenger` использует тот же стек, цель — текущая позиция игрока.

### 14.6 `ScheduleSystem.ResolveCurrentSlot`

```csharp
ScheduleSlot ResolveCurrentSlot(Entity npc, int hour) {
    var sched = npc.Get<ScheduleComponent>();
    var slot  = sched.Slots.FirstOrDefault(s => s.StartHour <= hour && hour < s.EndHour)
             ?? new ScheduleSlot { Action = ScheduleAction.Wander };
    if (slot.Action == ScheduleAction.Free) {
        var ctx = new FreetimeContext(npc, Clock);
        var options = sched.Freetime
            .Where(o => MatchesContext(o, ctx))
            .OrderByDescending(o => EffectivePriority(o, ctx))
            .ToList();
        var top = options.TakeWhile(o => EffectivePriority(o, ctx) == EffectivePriority(options[0], ctx))
                          .ToList();
        var pick = top[Random.Next(top.Count)];
        slot = ToSlotFromFreetime(pick);
    }
    return slot;
}

int EffectivePriority(FreetimeOption o, FreetimeContext ctx) {
    int p = o.Priority;
    if (o.Action == Visit && PartnerInFreetime(npc)) p += 5;
    return p;
}
```

### 14.7 `BirthSystem`

```csharp
void OnDayChanged(DayChanged e) {
    foreach (var npc in NpcsWithRelComp) {
        if (npc.Rel.ScheduledBirthDayIndex != e.DayIndex) continue;
        var partner = ResolvePartner(npc);
        var house   = WorldRegistry.GetHouse(npc.Residence.HouseId);
        var slot    = house.GetFreeChildBedSlot();
        if (slot == null) continue;          // двойная страховка

        var child = SpawnNpcFromSnapshot(BuildChildSnapshot(npc, partner, e.DayIndex));
        child.Residence.HouseId  = house.Id;
        child.Residence.BedSlotId = slot.Id;
        AssignKin(child, motherOf:npc, fatherOf:partner);

        npc.Personality.ChildWish      /= 2;
        partner.Personality.ChildWish  /= 2;
        npc.Rel.ScheduledBirthDayIndex     = null;
        partner.Rel.ScheduledBirthDayIndex = null;

        EventBus.Publish(new ChildBorn(npc, partner, child));
    }
}
```

### 14.8 `ChildGrowthSystem`

```csharp
void OnDayChanged(DayChanged e) {
    foreach (var child in World.GetEntitiesWith<ChildGrowthComponent, AgeComponent, SkillsComponent>()) {
        var years = AgeYears(child);
        if (years >= 18) {
            child.RemoveComponent<ChildGrowthComponent>();
            JobMarketSystem.AddSeeker(child);
            HouseRegistry.AssignFreeHouseInSettlement(child);
            child.GetComponent<ScheduleComponent>().LoadFromTemplate("default_unemployed");
            continue;
        }
        var progress = years / 18f;
        var growth = child.Get<ChildGrowthComponent>();
        var skills = child.Get<SkillsComponent>();
        foreach (var (id, target) in growth.TargetSkills)
            skills.Values[id] = target * progress;
    }
}
```

### 14.9 `ShopRestockSystem`

```csharp
void OnDayChanged(DayChanged e) {
    foreach (var shop in World.GetEntitiesWith<ShopComponent>()) {
        var s = shop.Get<ShopComponent>();
        if (e.DayIndex < s.NextRestockDayIndex) continue;
        var slot = WorldRegistry.GetProfession(s.ProfessionSlotId);
        var ownerNpc = WorldRegistry.GetNpc(slot.OccupiedNpcId);
        if (ownerNpc == null) { s.StockProtoIds.Clear(); continue; }
        var prof = PrototypeManager.GetProfession(slot.ProfessionId);
        var skill = ownerNpc.Skills[prof.PrimarySkill];
        var tier  = ResolveTier(skill);
        var pool  = PrototypeManager.GetAllEntities()
            .Where(p => p.Tags.Intersect(prof.TradeKinds).Any())
            .Where(p => p.Tier <= tier)
            .ToList();
        var amount = Lerp(prof.StockSizeMin, prof.StockSizeMax, skill / 10f);
        s.StockProtoIds = Enumerable.Range(0, amount).Select(_ => pool.PickRandom().Id).ToList();
        s.NextRestockDayIndex = e.DayIndex + prof.RestockEveryDays;
    }
}
```

---

## 15. Расширения MTEditor

Карты в редакторе должны уметь:

- рисовать **area-zones** с типом и тегами (как сейчас рисуются triggers, но в отдельном слое и с пикером `Kind`),
- расставлять **именованные точки** внутри area (для `bed_slot_a`, `child_bed_1`, `work_anchor`),
- валидировать `house`-area: должна содержать парные `bed_slot_a/b`, минимум 1 пара,
- валидировать `profession`-area: должна иметь `work_anchor` и `Properties.professionId`,
- редактор каталога стартовых NPC рядом с картой (`<map>.npcs.json`).

В `MTEditor` уже есть `EditorGame.cs` (огромный) — следуем его паттерну с `EditorModes.cs`. Добавить новый режим `AreaPaintMode`, рядом с существующим режимом для триггеров.

---

## 16. Контент: новые папки и прототипы

```
SandboxGame/Content/
  Data/
    calendar.json                 # см. §1.2
    skills.json                   # см. §3.4
    schedule_templates.json       # см. §4.2
    lifesim_tuning.json           # все магические числа (разбросы, шансы, тиры)
  Prototypes/
    Actors/
      NpcBase/proto.json          # базовый NPC
    Furniture/
      Note/proto.json             # записка
    Professions/
      Blacksmith/proto.json
      Tailor/proto.json
      ...
  Names/
    names_male_<faction>.json
    names_female_<faction>.json
    last_names_<faction>.json
  Maps/
    rivertown.npcs.json           # стартовые NPC поселения
    rivertown.map.json            # сама карта (уже существует)
```

---

## 17. Открытые вопросы и пропуски

Это то, что осознанно не решаем сейчас и оставляем «на потом». Каждый пункт — потенциальный отдельный мини-документ.

1. **Однополые отношения.** ТЗ запрещает; легко снимается в `Eligible(a,b)`.
2. **Развод по инициативе NPC без вмешательства игрока.** Сейчас не моделируется, можно ввести «совместимость» и шанс расторжения.
3. **Болезни.** Пока только смерть от старости/боя. Эпидемии — отдельная система, опционально.
4. **Миграция между поселениями без триггера от игрока.** Сейчас только при «уходе от игрока» (§5.6) и при поиске свободного дома совершеннолетним. «Дрейф населения» когда город опустел — отдельная задача.
5. **Экономика.** Цены, баланс золота, дефицит. Пока shop = просто витрина.
6. **Диалоги и квесты.** Не часть базы — только хуки в EventBus.
7. **Path-finding между картами в realtime.** В Active это уже сложная задача — надо больше думать про производительность.
8. **Очередь регистрации/освобождения профессий, если несколько NPC одинаково подходят.** Tie-break: youngest first / random.
9. **Смерть игрока.** ТЗ не описано. Game over vs «новый персонаж в роду» — обсудить.
10. **Совместная работа `MetabolismSystem` и NPC.** Пока NPC не голодают. Если включать — нужна еда в инвентаре + взаимодействие с домашней едой.

---

## 18. Роудмап по приоритетам

Цель — каждая фаза самодостаточна и не блокирует игру. После каждой фазы можно играть.

### P0 — фундамент времени и реестра (1–2 фазы кода)

Это основа. Без неё ничего ниже не имеет смысла.

- [x] **P0.1** Расширить `GameClock` → монотонный `TotalSeconds` + `DayIndex`. Не клампить день. Эвент `DayChanged`. ✅ (`MTEngine/Core/GameClock.cs`: добавлены `TotalSecondsAbsolute` (double, монотонный), `DayIndex` (long), `AdvanceToHour`, эвент `DayChanged`. `SleepSystem.StopSleep` теперь использует `AdvanceToHour` вместо `SetTime`, чтобы день увеличивался при пробуждении.)
- [x] **P0.2** `Calendar` (новый класс) + `Data/calendar.json`. Регистрация в `ServiceLocator`. Save-объект. ✅ (`MTEngine/Core/Calendar.cs` + `SandboxGame/Content/Data/calendar.json`. `Calendar` — `[SaveObject("calendar")]`, грузится из json в `Game1.LoadContent`, регистрируется в `ServiceLocator` и в `SaveGameManager`. Добавлен `GamePaths.Data` и `ContentPaths.DataRoot`.)
- [x] **P0.3** `AgeComponent` + `AgingSystem` (пока только подсчёт лет, без смерти). ✅ (`MTEngine/Npc/AgeComponent.cs` с DataField-прокси `ageYears`; `MTEngine/Npc/AgingSystem.cs` подписан на `DayChanged` + ленивая инициализация `BirthDayIndex` в `Update`. Зарегистрирован в `GameEngine.Initialize` как `AgingSystem`.)
- [x] **P0.4** Прокинуть `AgeComponent` на player-прототип. ✅ (`SandboxGame/Content/Prototypes/Actors/Player/proto.json` — добавлено `"age": { "ageYears": 25 }`.)
- [x] **P0.5** Подкрутить `SleepSystem` чтобы перематывал `TotalSeconds`, а не клампил день. ✅ (Сделано в P0.1: `SleepSystem.StopSleep` использует новый `GameClock.AdvanceToHour`, который при необходимости увеличивает `DayIndex` и публикует `DayChanged`.)
- [x] **P0.6** `EntityDied` эвент в `HealthSystem`. ✅ (`MTEngine/Core/LifeEvents.cs` — `enum DeathCause` + `struct EntityDied`. `HealthSystem.PublishDeath` дёргает `EventBus` в момент перехода в `IsDead`.)

### P1 — иерархия мира и area-zones (2–3 фазы кода)

- [x] **P1.1** `AreaZoneData` + `MapData.Areas`. ✅ (`MTEngine/World/AreaZone.cs` — `AreaZoneKinds`, `AreaPointData`, `AreaZoneData` (Id/Kind/Properties/Tiles/Points + `GetPoint`, `GetPointsByPrefix`). `MapData.Areas` + расширенная `Validate`.)
- [x] **P1.2** Загрузка/сериализация в `MapManager` и `MapEntitySpawner`. ✅ (Уже работает через `JsonSerializer.Deserialize<MapData>` / `Serialize` — поле `areas` подхватывается автоматически. Старые карты без `areas` грузятся как пустой список. `SaveGameManager.CloneMap` тоже сохраняет `Areas`. `MapEntitySpawner` пока не использует `Areas` — это понадобится только в P1.4–P1.5.)
- [x] **P1.3** В MTEditor — режим рисования area-zones с пикером `Kind` и редактором `Properties` + именованных point-ов (см. §15). ✅ (`MTEditor/Tools/AreaZoneTool.cs` — новый инструмент `Tool.AreaZone`, горячая клавиша `5`, кнопка "Areas" в HUD. Brush/Mouse режимы как в TriggerZoneTool. `[ / ]` циклит Kind, текстовые поля для Area ID, Prop key/value, Point ID. `Shift+ЛКМ` ставит именованную точку, `Shift+ПКМ` удаляет точку под курсором. Каждый Kind подсвечен своим цветом, точки — жёлтые квадратики с подписями. Счётчик Areas попадает в нижнюю инфо-строку.)
- [x] **P1.4** Структуры `FactionDef`, `SettlementDef`, `DistrictDef`, `HouseDef`, `ProfessionSlotDef`. ✅ (`MTEngine/Npc/WorldDefinitions.cs` — POCO с минимальными полями. `HouseDef` сразу с `BedSlots`, `ChildBedSlots`, `ResidentNpcSaveIds`, `ForSale` (вычислимое). `ProfessionSlotDef` с `WorkAnchor`, `OccupiedNpcSaveId`, `IsVacant`.)
- [x] **P1.5** `WorldRegistry` — строится при старте после загрузки всех карт. ✅ (`MTEngine/Npc/WorldRegistry.cs` — `RebuildFromMaps(MapManager)` пробегает по `GetAvailableMaps`, читает `area.Kind` (`settlement` / `district` / `house` / `profession`) и наполняет таблицы. Прокидывает родительские связи (house → district → settlement → faction). Query-методы: `HousesInDistrict`, `HousesInSettlement`, `EmptyHousesInSettlement`, `VacantProfessionsInSettlement`, `FindHouseByMapAndTile`. Регистрируется в `GameEngine` и `ServiceLocator`, `Game1.LoadContent` дёргает `RebuildFromMaps` сразу после создания `MapManager`.)
- [x] **P1.6** Save для `WorldRegistry` (резидентность домов, занятость профессий). ✅ (`WorldRegistry` помечен `[SaveObject("worldRegistry")]`, динамика лежит в `HouseResidency` / `ProfessionOccupation` (оба `[SaveField]`). Цикл: `CaptureDynamicState` перед save, `RehydrateDynamicState` после load. Подключено в `Game1.HandleSaveSlotConfirmed` и `Game1.RestoreLoadedSession`. Статика (Houses/Districts/...) каждый раз восстанавливается из карт — это правильно, потому что карты могут меняться между сейвами.)
- [x] **P1.7** Документация по конвенциям (где должны быть `bed_slot_a/b`, `child_bed_*`, `work_anchor`). ✅ (Покрыто в §2.1–2.2 этого документа: house = пара `bed_slot_*` + опц. `child_bed_*`; profession = `work_anchor` + `Properties.professionId`; school/inn/tavern/orphanage/wander = `wander_*` точки. Соответствие реализовано в `WorldRegistry.EnsureHouse/EnsureProfession`, `HouseDef.BedSlots/ChildBedSlots` через `area.GetPointsByPrefix("bed_slot")` / `"child_bed"`.)

### P2 — NPC-каркас (один город, без социалки)

- [ ] **P2.1** `NpcTagComponent`, `IdentityComponent`, `PersonalityComponent`, `SkillsComponent`, `ResidenceComponent`, `KinComponent`.
- [ ] **P2.2** Прототип `NpcBase`. Базовый sprite, можно временно тот же что у player.
- [ ] **P2.3** `WorldPopulationStore` (даже для одного города пока не активен — но будем класть туда снапшоты).
- [ ] **P2.4** Загрузка стартовых NPC из `Maps/<settlement>.npcs.json` (см. §3.5) и распределение по домам.
- [ ] **P2.5** UI: расширить `InfoComponent` чтобы показывал имя/возраст/семейный статус.

### P3 — расписание + базовый AI движения

- [ ] **P3.1** `ScheduleComponent` + `Data/schedule_templates.json`.
- [ ] **P3.2** `ScheduleSystem` с `ResolveCurrentSlot` и Freetime-роллом.
- [ ] **P3.3** A*-пасфайндер по `TileMap`.
- [ ] **P3.4** `NpcMovementSystem` (двигает NPC в Active-зоне к target из intent).
- [ ] **P3.5** Расширить `BedComponent` до двух слотов сна (`Slots: List<BedSlot>` + точки в area).
- [ ] **P3.6** Базовый «Wander» — обход `wander_*` точек в area.
- [ ] **P3.7** Эвенты подобные `NpcArrivedAtArea` (для других систем).

### P4 — социальный слой (одиночное поселение, активная зона)

- [ ] **P4.1** `RelationshipsComponent` + `MatchmakingSystem`.
- [ ] **P4.2** Расчёт даты свадьбы и переход `Single → Dating → Married`.
- [ ] **P4.3** Ночёвка вместе (правка приоритетов sleep-slot в schedule).
- [ ] **P4.4** Переезд жены в дом мужа. Обновление `HouseDef.ResidentNpcIds`.
- [ ] **P4.5** `BirthSystem`, `ChildGrowthSystem` + прототип ребёнка/наследование (§6).

### P5 — профессии и торговля

- [ ] **P5.1** `ProfessionPrototype` + расширение `PrototypeManager`.
- [ ] **P5.2** `ProfessionComponent` + `JobMarketSystem`.
- [ ] **P5.3** `ProfessionTickSystem` (рост навыка).
- [ ] **P5.4** `ShopComponent` + `ShopRestockSystem`. Тиры в прототипах товаров.
- [ ] **P5.5** UI окна торговли (минимально — список товаров, цена = `ResolvePrice(item, sellerSkill)`, кнопка купить).
- [ ] **P5.6** Безработные → ночёвка в `Inn`.

### P6 — зональная симуляция и LOD

- [ ] **P6.1** `LocationGraph` (BFS по `location_transition`-триггерам).
- [ ] **P6.2** `SimulationLodSystem.GetZone(npc)`.
- [ ] **P6.3** Фильтрация в каждой системе: «работаю только если zone <= X».
- [ ] **P6.4** `WorldPopulationStore.Live()` / `Snapshot()` (преобразование туда-сюда).
- [ ] **P6.5** Catch-up при пересечении границы зоны.

### P7 — родственники и месть

- [ ] **P7.1** Зеркальная синхронизация `KinComponent` при свадьбе/рождении/смерти.
- [ ] **P7.2** `RevengeTrigger` + `RevengeSystem` (§8.3).
- [ ] **P7.3** `AvengerComponent` + специальное расписание преследования.
- [ ] **P7.4** Поведение при нахождении игрока: подойти, фраза, атака. Не атакует спящего.
- [ ] **P7.5** Меньшие шкалы Vengefulness (наценка торговца, hostile при встрече).

### P8 — измена, переезды между поселениями

- [ ] **P8.1** `RelationshipWithPlayerComponent`. UI игнорирования партнёра NPC при `Infidelity == 10`.
- [ ] **P8.2** Уход партнёра-NPC из дома игрока (§5.6).
- [ ] **P8.3** Прототип `Note` + `NoteComponent`.
- [ ] **P8.4** Телепорт партнёра в другую settlement по правилам §5.6, с весами.
- [ ] **P8.5** `Matchmaking` после переезда.

### P9 — отполировка, журналы, опционально

- [ ] **P9.1** `JournalSystem` + UI журнала.
- [ ] **P9.2** Достижения / отслеживание «убил всех в городе».
- [ ] **P9.3** Migration drift (если город пустеет).
- [ ] **P9.4** Полировка smell-теста: 50 NPC × 3 settlement × 100 игровых дней должны симулироваться без лагов.
- [ ] **P9.5** Профайлинг: основные горячие точки `MatchmakingSystem`, `ScheduleSystem`, `NpcMovementSystem`.

---

## 19. Чеклист первого реального коммита

Если хочется завтра уже что-то закоммитить — минимальный полезный мердж:

1. `WorldClock` + `Calendar` + `DayChanged` + `AgeComponent` (P0.1–P0.4).
2. `EntityDied` эвент (P0.6).
3. `AreaZoneData` + загрузка/сохранение (P1.1–P1.2).

Это уже даёт календарь, возраст и площадки в карте, без NPC-логики. Дальше всё надстраивается над этим без переделок.
