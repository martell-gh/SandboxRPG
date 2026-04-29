ДОПОЛНЕНИЕ ОТ МЕНЯ:
у каждого региона в редакторе (у каждой карты) можно установить "значимость".
Типа незначимый, важный, очень важный.
Это влияет вот на что: события, которые происходят в регионе, будут распространяться по миру. Условно: игрок всех повырезал в незначимом регионе:
кто-то изредка упоминает, что где-то там какой-то чел всех перехуярил и забывают через пару дней.
В важном уже чаще, все напрягаются там, поговаривают дальше по миру и упоминают неделю. 
В очень важном в курсе становятся все и часто говорят об этом событии и довольно долго, пару недель.

# NPC и A-Life: спека, архитектура, роудмап

> **Кодовое имя системы — MTLiving.** Используем для namespace’ов и имён файлов в новых модулях; миграция существующего `MTEngine.Npc` — отдельный пункт полировки, не блокер.

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
  └─ Settlement (город / деревня; задаётся шапкой MapData: City + LocationKind)
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
        // for "house": optional "settlementId"/"districtId"; by default taken from MapData.CityId
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

Эти структуры строятся при старте игры из шапки карты (`MapData.CityId`, `MapData.FactionId`, `LocationKind`) и проходом по `MapData.Areas` каждой карты в `Maps/`. Отдельная `area` типа `settlement` больше не нужна: если системе нужен поиск по поселению, она использует всю карту этого `CityId`. Заодно дома/проф-слоты проверяются на наличие нужных point-ов:

- **House:** хотя бы 1 точка `bed_slot_*`. Сколько `child_bed`-точек, столько и максимум детей. Опционально точка `door`.
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
// NB: тип регистрируется как "npcSkills" (а не "skills"), потому что в коде уже
// существует MTEngine.Combat.SkillComponent с typeId "skills" — это игроцкий
// набор боевых/крафтовых скиллов. NPC-словарь живёт параллельно.
[RegisterComponent("npcSkills")]
public class SkillsComponent : Component {
    [DataField("values")] [SaveField]
    public Dictionary<string, float> Values { get; set; } = new();
        // Ключи совпадают с именами SkillType (см. MTEngine.Combat.SkillType):
        // "Smithing", "Tailoring", "OneHandedWeapons", "Trade", "Medicine" ...
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
2. Конкретные предсозданные NPC лежат не как отдельные прототипы на каждого человека, а как **персональные записи** в ростере карты. Прототип остаётся шаблоном-архетипом (`npc_base`, `npc_worker`, `npc_guard`), а человек получает имя, пол, дом, расписание, одежду, имущество и component-overrides в своём ростере.
3. Официальный формат ростера — `Maps/<settlement>.npc`. Это JSON-compatible файл с нашим расширением `.npc`: синтаксис тот же, что у JSON, но по смыслу это именно список людей. Старый `Maps/<settlement>.npcs.json` поддерживается как fallback для миграции.

```json
// Maps/rivertown.npc
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
    "kin": [ { "npcId":"smith_oleg_father", "kind":"father" } ],
    "components": {
      "schedule": { "templateId": "default_worker" },
      "sprite": { "source": "../Player/sprite.png", "srcX": 0, "srcY": 0, "width": 32, "height": 32 }
    },
    "outfit": { "torso": "tinted_shirt", "pants": "work_pants" },
    "hands": ["iron_hammer"],
    "inventory": ["bread", "water_bottle"]
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
    Socialize,      // фритайм: подойти к другому NPC, постоять рядом и поговорить
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
      {"action":"Socialize", "priority":1, "dayOnly":true},
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
- **Социальный wander-event.** Если выпал `Socialize`, NPC ищет другого свободного NPC на той же активной карте, оба получают соседние точки встречи, подходят, поворачиваются друг к другу, несколько секунд стоят и переговариваются popup-репликами, затем расходятся и получают короткий cooldown.

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

    // Ссылка на партнёра — стабильный SaveId, а не runtime int (см. §12.1).
    [SaveField] public string PartnerNpcSaveId = "";
    [SaveField] public bool PartnerIsPlayer;

    // Все дни — long с sentinel-значением -1L "не запланировано" (вместо long?).
    [SaveField] public long ScheduledDateDayIndex    = -1L;
    [SaveField] public long DatingStartedDayIndex    = -1L;
    [SaveField] public long ScheduledWeddingDayIndex = -1L;
    [SaveField] public long MarriageDayIndex         = -1L;
    [SaveField] public long ScheduledBirthDayIndex   = -1L;
    [SaveField] public long LastMatchSearchDayIndex  = -1L;

    [SaveField] public int OvernightStreak;
    [SaveField] public int PlayerOpinion;            // -100..100, см. §9.1 / NpcCombatReactionSystem
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
- проверяет, что пара зеркально ссылается друг на друга и оба ещё `Single`;
- ставит обоим `Status = Dating`;
- очищает отработанный `ScheduledDateDayIndex`;
- публикует `RelationshipStarted` в `EventBus`.

«Встречаются» означает в коде:
- текущий минимум P4.2: статус и событие уже работают;
- позже ставим `Visit` цели друг друга в Freetime с высоким приоритетом,
- по ночам с шансом `p(overnight)` оба идут в один дом (см. ниже).

### 5.4 Ночёвка вместе

```
p_overnight(today) = clamp((today - dateDay) / (weddingDay - dateDay), 0.1, 0.95)
```

Если ролл удался — оба ночуют в доме того, у кого статус «активный жилец» (по умолчанию мужчины, можно случайно выбрать). Партнёр временно занимает второй `bed_slot` дома.

Текущий минимум P4.3: `ScheduleSystem` при `Sleep` стабильно на одну игровую ночь катает шанс совместной ночёвки для пары `Dating/Engaged/Married`. Если ролл прошёл, один партнёр идёт в дом другого, хозяин берёт свой `Residence.BedSlotId`, гость берёт другую `bed_slot_*` точку того же дома. Между картами NPC пока не ходят, поэтому ночёвка срабатывает только если дом-хост находится на текущей карте.

### 5.5 Свадьба

Срабатывает в `today >= ScheduledWeddingDayIndex`:

- `Status = Married`, очищается `ScheduledWeddingDayIndex`, публикуется `RelationshipMarried`;
- P4.4: жена переезжает: `ResidenceComponent.HouseId` = дом мужа; если дом мужа невалиден, работает fallback в дом второго партнёра;
- старый дом переезжающего: `HouseDef.ResidentNpcSaveIds.Remove(npc)`. Если у дома больше нет жильцов → `HouseDef.ForSale = true` (на UI ничего не делаем; на «продажу» подвязываем переезд других NPC из других городов и игрока, см. §11.4);
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
- `RelationshipStarted(npcA, npcB, dayIndex)`
- `RelationshipMarried(npcA, npcB, dayIndex)`
- `RelationshipSeparated(npcA, npcB, cause)` *(планируется к P8)*
- `NpcMovedHouse(npc, oldHouse, newHouse, dayIndex)`
- `PregnancyScheduled(motherSaveId, fatherSaveId, birthDayIndex)` *(P4.5)*
- `NpcBorn(childSaveId, fatherSaveId, motherSaveId, houseId, dayIndex)` *(P4.5)*
- `MapCatchUpRan(mapId, daysElapsed, todayDayIndex)` *(P6.5)* — после возврата игрока в карту, бывшую вне Active.

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

### 7.1 Глобальная профессия

Профессии не создаются отдельно для каждого города. Они лежат в глобальном справочнике `Content/Data/professions.json`, который редактируется вкладкой `MTEditor → Professions`:

```json
{
  "professions": [
    {
      "id": "blacksmith",
      "name": "Blacksmith",
      "description": "Makes and sells metal tools, weapons and armor.",
      "primarySkill": "Smithing",
      "isTrader": true,
      "tradeTags": ["metal", "ingot", "weapon", "armor", "tool"],
      "stockSizeMin": 4,
      "stockSizeMax": 12,
      "restockEveryDays": 7,
      "skillGainPerDay": 0.005
    }
  ]
}
```

`ProfessionCatalog` грузит этот файл и даёт редактору/системам доступ к `id/name/primarySkill/isTrader/tradeTags/stockSizeMin/stockSizeMax/restockEveryDays/skillGainPerDay/description`.

### 7.2 Площадка профессии

Площадка профессии обычно задаётся как `AreaZoneData(Kind="profession", Properties.professionId="blacksmith")`. В редакторе это выбирается dropdown-ом `Profession` из глобального справочника. Для `profession`-зоны обязателен `work_anchor` point.

Исключение для общих социальных мест: `kind=tavern` автоматически получает `Properties.professionId=innkeeper`. Тогда одна и та же tavern-зона одновременно остаётся местом еды/общения NPC и становится рабочим слотом трактирщика. Если `work_anchor` не задан явно, работа берёт ближайшую подходящую точку `work_*` / `seat_*` / `table_*` / `eat_*` / `wander_*`, а затем центр зоны.

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

### 7.4.1 Inn + Tavern и трактирщик

`Inn` и `Tavern` остаются разными area-зонами, потому что у них разные роли:
- `inn` — кровати/комнаты, сон и аренда.
- `tavern` — еда, выпивка, социализация, точки `eat_*`, `table_*`, `seat_*`, `wander_*`.

В типичном здании это две зоны внутри одного трактира. Держит их один NPC с профессией `innkeeper`: tavern-зона сама автоматически становится его рабочим слотом, а отдельная `profession`-зона нужна только для нетипичной планировки. `InnRentalSystem` автоматически ищет `inn`/`tavern` на текущей карте. У такого NPC покупается еда через обычную торговлю по тегам `food/drink`, а также появляется действие аренды.

Аренда:
- цена комнаты читается из properties `roomPrice`, цена кровати — из `bedPrice`; общий fallback — `rentPrice`; если ничего не задано, используются дефолты;
- аренда длится ровно одни игровые сутки (`GameClock.SecondsPerDay`) с момента покупки;
- если внутри `inn` найдена маленькая комната, окружённая стенами/коллизией, с одной дверью и 1–2 кроватями, сдаётся комната;
- если комнат нет, сдаётся отдельная кровать;
- дверь снятой комнаты подсвечивается жёлтым, пока аренда активна;
- спать в кровати гостиницы без активной аренды нельзя.

Трактирщик не уходит спать по обычному расписанию: если его текущий слот `Sleep`, `ScheduleSystem` переводит его в `StayInTavern`. Он всё ещё может иметь свободные часы/прогулки, и тогда торговля/аренда недоступны.

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
bestAllowedTier = ResolveBestAllowedTier(skill)  # 4 -> 1 (4 = низший, 1 = лучший)
candidates = AllItemPrototypes
    .Where(p => p.Tags ∩ profession.tradeTags)
    .Where(p => p.QualityTier >= bestAllowedTier)
amount = lerp(stockSizeMin, stockSizeMax, skill / 10)
shop.StockEntityIds = pick `amount` items, c повторами разрешено
```

Выбор товаров делается **по тегам**, а качество — через компонент `qualityTier`. Чем выше навык основной профессии, тем более качественные товары могут попасть в restock: новичок получает в основном tier 4, мастер уже может продавать tier 1–4. Если у предмета нет `qualityTier`, он считается обычным низким тиром для торговли.

Цена считается в `ShopPricing`: базовая стоимость идёт от размера, тегов (`weapon/armor/tool/metal/ingot/food/medical`), редкости (`rare/unique/legendary`), сложности крафта (`requiredSkill`, `craftTime`, `recipeTier`, `requiresRecipe`) и затем умножается на `qualityTier`. Продажа игроком даёт половину текущей buy-price, умноженную на размер стака.

У каждой карты есть региональная рыночная поправка:

```json
{
  "wantedTags": ["food", "medicine"],
  "unwantedTags": ["weapon"]
}
```

Если у предмета есть тег из `wantedTags`, цена на этой карте выше; если есть тег из `unwantedTags`, ниже. В редакторе карты это редактируется в панели `LOCATION METADATA` через строки `Wanted` и `Unwanted`. Например, пацифистский регион может пометить `weapon` как невостребованный, и оружие там будет покупаться/продаваться заметно дешевле.

Тиры лежат в `proto.json` каждого предмета:

```json
{
  "id": "iron_sword",
  "category": "entity",
  "components": {
    "item": { "tags": ["weapon", "melee", "metal"] },
    "qualityTier": { "tier": 3 }
  }
}
```

ShopComponent не привязывает физически предметы в storage в фоне — он просто хранит «список proto + qty» и материализует entity предмета только когда игрок открывает торговое окно. Иначе будут тысячи висящих entity по всем городам.

Торговать с NPC можно только в его рабочее время. Для обычных торговцев это слот `Work`; для `innkeeper` также считается рабочим `StayInTavern`. Если смена закончилась, action «Торговать» не показывается, а уже открытое торговое окно закрывается.

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
public class KinLink {
    public string NpcSaveId = "";   // стабильный SaveEntityIdComponent.SaveId
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

### 8.6 Боевой рассудок NPC: оценка сил, страх и бегство

Нормальный житель не должен драться до смерти по умолчанию. Базовое правило MTLiving: **агрессия не равна самоубийству**. NPC оценивает не только “обидел ли меня игрок”, но и “есть ли шанс выжить”, “опасность всё ещё рядом”, “меня преследуют или я уже ушёл”.

Нужен отдельный `CombatThreatSystem` / `NpcSelfPreservationSystem`, работающий поверх `NpcCombatReactionSystem`.

#### Оценка силы

Вводим runtime-оценку `CombatPowerScore`:

```
power =
    weaponPower                 // оружие в руках, reach, base damage, armor penetration
  + armorPower                  // защита экипировки
  + relevantSkill * skillWeight // рукопашка / одноручное / двуручное
  + healthFactor                // текущий HP и кровотечения
  + staminaFactor               // позже, когда появится выносливость
  + alliesNearbyBonus           // свои рядом, если реализовано
  - woundsPenalty               // раны, кровотечение, переломы, exhaustion
```

Для игрока отдельно учитываем “видимую угрозу”: если NPC видит меч/топор/двуручник в руках игрока, `playerVisibleWeaponPower` сильно повышает риск. Если оружие спрятано в инвентаре и NPC его не видел — оно не участвует.

#### Боевые роли

Разные NPC принимают разные решения:

| Роль | Поведение |
|------|-----------|
| `civilian` / обычный житель | Предупреждает, может ударить/толкнуть за дом, но при явном риске отступает. |
| `guard` | Держится дольше, зовёт помощь, отступает только при тяжёлых ранах или огромном перевесе игрока. |
| `avenger` | Может игнорировать часть страха, но всё равно не обязан умереть глупо; при критическом HP отступает, чтобы перегруппироваться. |
| `pacifist` | Не атакует, старается уйти/позвать помощь. |

Роль можно брать из прототипа/профессии/фракции, а пока fallback: NPC без боевой профессии = `civilian`.

#### Решение: драться или бежать

Каждый combat tick NPC строит `ThreatAssessment`:

```
relativePower = npcPower / max(1, playerPower)
hpRatio = npcHealth / npcMaxHealth
playerArmed = visibleWeaponPower > unarmedThreshold
outnumbered = hostileCountNearby > allyCountNearby
escapeKnown = FindEscapePoint() != null

if role == civilian:
    flee if hpRatio < 0.45
    flee if playerArmed && npcUnarmed && relativePower < 1.25
    flee if relativePower < 0.75
    flee if bleedingHard || limbDisabled

if role == guard:
    flee/regroup if hpRatio < 0.25
    flee/regroup if relativePower < 0.45 && noAlliesNearby

if role == avenger:
    flee/regroup if hpRatio < 0.18
    otherwise press attack unless target lost
```

Важно: “если NPC сильнее, но у него мало HP” — не всегда бегство. Для обычного жителя низкий HP почти всегда повод уйти. Для вооружённого/злого/мстящего NPC низкий HP может перейти в “осторожную атаку”: держать дистанцию, не входить в удар, ждать окна, но это отдельный уровень AI. На первом этапе делаем проще: `civilian` бежит, `guard/avenger` могут остаться только если `relativePower` сильно выше и нет кровотечения.

#### Состояние бегства

Добавить `NpcFleeComponent`:

```csharp
[RegisterComponent("npcFlee")]
public class NpcFleeComponent : Component {
    public int ThreatEntityId;
    public string ThreatSaveId = "";
    public string Reason = "";          // "low_hp", "outmatched", "unarmed_vs_weapon", ...
    public Vector2 LastThreatPosition;
    public Vector2 EscapeTarget;
    public double StartedAt;
    public double LastSawThreatAt;
    public float SafeForSeconds;
}
```

`NpcMovementSystem` получает приоритет: если есть `NpcFleeComponent`, обычное расписание и погоня временно игнорируются.

#### Куда бежать

`FindEscapePoint(npc, threat)`:

1. ближайшая точка вне line-of-sight игрока;
2. тайл за стеной/углом, если есть;
3. свой дом, если дом не является местом угрозы;
4. ближайшая зона `guard`, `tavern`, `market`, `profession` с людьми;
5. просто дальний проходимый тайл от игрока, не через `location_transition`, если это не осознанное бегство из локации.

Для обычного жителя нельзя выбирать клетку в чужом доме и нельзя бежать в тупик, если есть альтернатива. Если путь к escape target пересёкся с игроком или игрок снова оказался близко — пересчитать.

#### Когда NPC понимает, что опасность прошла

NPC успокаивается, если одновременно:

- игрок не виден через `NpcPerception` минимум `SafeForSecondsTarget` (например 8–12 секунд);
- дистанция до последней позиции угрозы > `safeDistance` (например 500px);
- игрок не наносил новый урон NPC после начала бегства;
- NPC не находится в своём доме с нарушителем внутри.

После этого:

- `NpcFleeComponent` снимается;
- `NpcAggressionComponent` снимается или понижается до `None`;
- NPC получает короткое состояние `Recovering`: постоять 2–5 секунд, затем `ScheduleSystem.RefreshNow()`;
- отношение к игроку уже испорчено, но NPC не продолжает драку бесконечно.

Если игрок преследует бегущего NPC и снова входит в FOV/близкую область, `LastSawThreatAt` обновляется, `SafeForSeconds` сбрасывается. Если игрок атакует во время бегства, `ProvokedByTarget = true`, страх растёт, а у некоторых ролей может включиться крик/зов помощи.

#### Интеграция с домашней агрессией

Для `HomeIntrusion`:

- хозяин может сначала выгнать, потом атаковать;
- если игрок вышел из дома и не атаковал — хозяин успокаивается, как сейчас;
- если игрок вооружён намного сильнее или хозяин ранен — обычный житель не должен стоять насмерть в дверях: он бежит из дома/к соседям/к страже;
- при этом дом всё равно остаётся “оскорблением”: `PlayerOpinion` падает, возможно появляется delayed-жалоба/штраф позже.

#### Не реализуем пока

Этот блок — дизайн. Текущая кодовая задача на потом: `CombatThreatSystem`, `NpcFleeComponent`, escape-point resolver и редакторские/прототипные поля роли (`combatRole`, `bravery`, `riskTolerance`).

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
- При попадании NPC в Active или Background зону — `World.CreateEntity` собирается из снапшота через `WorldPopulationStore.Live()` и `EntityFactory`.
- При выходе из Background обратно в Distant — компоненты снова сериализуются в `NpcSnapshot`, entity уничтожается.
- `WorldPopulationStore` целиком ложится в save.

Для этого есть движковый `NpcSnapshotComponentSerializer`: он читает/пишет `[SaveField]`-поля компонентов в `JsonObject` без зависимости на слой `SandboxGame.Save`.

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

### 14.5.1 God Mode / MTLiving observer

Для отладки живой симуляции в игре есть консольная команда:

```
god
god on
god off
god toggle
```

В активном режиме:

- `PlayerMovementSystem` не управляет игроком: персонаж остаётся болванчиком на месте;
- `InteractionSystem` не открывает обычное right-click меню;
- `TriggerCheckSystem` не запускает переходы от позиции игрока;
- камера становится свободной: `WASD`/стрелки двигают, `Shift` ускоряет, колесо меняет zoom;
- слева показывается список карт, клик по карте грузит её в realtime без телепорта игрока;
- при выключении режима игра возвращает активную карту и позицию игрока туда, где они были при включении;
- right-click по NPC открывает debug-карточку: identity/saveId, HP/позиция/возраст, residence/profession, текущий schedule-slot, `NpcIntent`, relationships/запланированные даты, kin, personality, aggression-state, лучший skill и список компонентов.

Это не “читерская неуязвимость”, а инструмент наблюдения за MTLiving. Для бессмертия игрока/лечения остаются отдельные dev-команды (`heal`, `hurt`, etc.).

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

- рисовать **area-zones** с типом и properties (сделано через `AreaZoneTool`, меню `Tools → Area Zones`, hotkey `5`),
- расставлять **именованные точки** внутри area (сделано: `Point ID` + shift-модификатор в `AreaZoneTool`),
- валидировать `house`-area: должна содержать минимум одну точку `bed_slot_*`,
- валидировать `profession`-area: должна иметь `work_anchor` и `Properties.professionId`; `tavern` автоматически получает `innkeeper` без отдельного `work_anchor`,
- редактировать глобальный справочник профессий (`Data/professions.json`): торговая ли профессия, какими тегами товаров торгует, основной навык, размер/restock лавки,
- редактор каталога стартовых NPC рядом с картой (`<map>.npc`).

В `MTEditor` `AreaZoneTool` подключён рядом с `TriggerZoneTool`, использует тот же HUD/dropdown-паттерн и сохраняет данные в `MapData.Areas`. `Kind` переключается через dropdown в панели или стрелками `←/→/↑/↓` при наведении на панель; старые `[`/`]` тоже работают. Для `kind=profession` панель показывает dropdown `Profession`, который берёт значения из глобального `ProfessionCatalog` и записывает в area только `professionId`. Для `kind=tavern` редактор сам ставит `professionId=innkeeper`, скрывает лишний выбор профессии и использует социальные точки или центр зоны как рабочую точку; ошибки подсвечиваются в панели и блокируют save карты, если редактор не может исправить их автоматически.

В `MTEditor` есть вкладка `Professions`: она редактирует [SandboxGame/Content/Data/professions.json](../SandboxGame/Content/Data/professions.json). Профессия задаётся один раз на весь мир: `id`, `name`, `primarySkill`, `isTrader`, `tradeTags`, `stockSizeMin`, `stockSizeMax`, `restockEveryDays`, `skillGainPerDay`, `description`. Торговые теги выбираются из тегов существующих item-прототипов. При переименовании/удалении профессии редактор обновляет `professionId` в area-zones карт.

В `MTEditor` есть вкладка `NPCs`: слева выбирается карта/ростер, рядом список людей, справа карточка человека. Карточка редактирует базовую личность, `proto`-шаблон, пол, возраст, дом/кровать, профессию, расписание, спрайт, цвет кожи (`components.sprite.color`), одежду, причёску, цвет волос, предметы в руках, инвентарь, личностные параметры и навыки. Поля со ссылками работают как picker/dropdown по реальным данным: фракции и поселения берутся из `WorldData`, дома/кровати/рабочие места — из `MapData.Areas`, одежда и предметы — из существующих прототипов, `Hair Style` — из `Prototypes/Hair`. Справа в карточке есть preview-панель: она рисует базовый спрайт с выбранным skin tint, одежду и волосы с тем же tint-подходом, что игра, и позволяет крутить NPC кнопками `<`/`>`. Там же кнопка `Create / Update Hair` создаёт/обновляет hair-прототип с `hairStyle` в отдельной папке `Prototypes/Hair/<Name>/`, если нужно быстро завести новую причёску. Сами зоны рисуются в `AreaZoneTool`, а `NPCs` выбирает, к каким зонам привязан конкретный человек. `districtId` не выбирается руками: он выводится из выбранного дома/карты и показывается в блоке `Derived Zones`. Команда `Save .npc` пишет `Maps/<mapId>.npc`. Команда `Save As Template` сохраняет текущие component-overrides как прототип-наследник от `npc_base` в `Prototypes/Actors/NpcTemplates/` и подставляет его в поле `proto`.

Вкладка `Global Settings` выбирает стартовую карту, spawn и стартовую одежду героя (`startingOutfit`: `torso/pants/shoes/back`) из полного списка `.map.json` и wearable-прототипов, затем пишет это в `Maps/world_data.json`. `Начать игру` больше не показывает список карт: поток теперь `Main Menu → Character Creator → StartingMapId`. Character creator задаёт пол, причёску, цвет волос и цвет кожи игрока; выбранные значения применяются к `IdentityComponent`, `HairAppearanceComponent` и `SpriteComponent.Color` player entity перед загрузкой стартовой карты. Стартовая одежда создаётся как item-entities и сразу кладётся в `EquipmentComponent` игрока.

---

## 16. Контент: новые папки и прототипы

```
SandboxGame/Content/
  Data/
    calendar.json                 # см. §1.2
    professions.json              # глобальные профессии: skill/trader/tradeTags/restock
    skills.json                   # см. §3.4
    schedule_templates.json       # см. §4.2
    lifesim_tuning.json           # все магические числа (разбросы, шансы, тиры)
  Prototypes/
    Base/
      Entity/proto.json           # base_entity
      Actor/proto.json            # base_actor
      Item/proto.json             # base_item
      Furniture/proto.json        # base_furniture
      Weapon/proto.json           # base_weapon
      Tile/proto.json             # base_tile
      Substance/proto.json        # base_substance
    Actors/
      NpcBase/proto.json          # базовый NPC
    Hair/
      BaseHair/proto.json         # abstract base_hair
      ShortMessy/proto.json       # hairStyle + белый sprite.png-заглушка
    Furniture/
      Note/proto.json             # записка
  Names/
    names_male_<faction>.json
    names_female_<faction>.json
    last_names_<faction>.json
  Maps/
    rivertown.npc                 # стартовые NPC поселения (.npc = JSON-compatible формат)
    rivertown.map.json            # сама карта (уже существует)
```

### 16.1 Наследование прототипов

Любой `proto.json` может указать:

```json
{
  "id": "steel_longsword",
  "base": "longsword"
}
```

`PrototypeManager` сначала резолвит родителя, потом накладывает дочерний JSON. Обычные top-level поля заменяются целиком. `components` мержатся по id компонента: дочерний `"weapon"` заменит только базовый `"weapon"`, а базовые `"sprite"` / `"transform"` останутся. `null` можно использовать как явное удаление унаследованного top-level поля или компонента.

`abstract: true` помечает служебный базовый прототип: он доступен как `base`, но не попадает в палитры через `GetAll*`.

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

> **Легенда статусов**
> - 🟢 **DONE** — задача завершена и собирается в build’е, описано «куда легло»
> - 🟡 **WIP** — в работе сейчас
> - ⚪ **TODO** — ещё не начато
>
> Прогресс по фазам:
> - **P0 — фундамент времени и реестра:** 🟢🟢🟢🟢🟢🟢 (6/6) ✅ ЗАВЕРШЕНА
> - **P1 — иерархия мира и area-zones:** 🟢🟢🟢🟢🟢🟢🟢 (7/7) ✅ ЗАВЕРШЕНА
> - **P2 — NPC-каркас:** 🟢🟢🟢🟢🟢 (5/5) ✅ ЗАВЕРШЕНА
> - **P3 — расписание + AI движения:** 🟢🟢🟢🟢🟢🟢🟢 (7/7) ✅ ЗАВЕРШЕНА, ДОБАВЛЕН SOCIALIZE WANDER-EVENT
> - **P4 — социальный слой:** 🟢🟢🟢🟢🟢 (5/5) ✅ ЗАВЕРШЕНА
> - **P5 — профессии и торговля:** 🟢🟢🟢🟢🟢🟢 (6/6) ✅ ЗАВЕРШЕНА
> - **P6 — LOD-симуляция:** 🟢🟢🟢🟢🟢 (5/5) ✅ ЗАВЕРШЕНА
> - **P7 — родня, месть и самосохранение:** 🟢🟢🟢🟢🟢🟢 (6/6) ✅ ЗАВЕРШЕНА
> - **P8 — измена и переезды:** 🟢🟢🟢⚪⚪⚪ (3/6) — добавлен P8.1b (Talk-интеракция)
> - **P9 — полировка:** ⚪⚪⚪⚪⚪ (0/5)

### P0 — фундамент времени и реестра (1–2 фазы кода)

Это основа. Без неё ничего ниже не имеет смысла.

#### 🟢 DONE — P0.1 Расширить `GameClock` → монотонный `TotalSeconds` + `DayIndex`, эвент `DayChanged`
> **Куда легло:** [MTEngine/Core/GameClock.cs](../MTEngine/Core/GameClock.cs) — `TotalSecondsAbsolute` (double, монотонный), `DayIndex` (long), `AdvanceToHour`, struct `DayChanged`. `SleepSystem.StopSleep` теперь использует `AdvanceToHour` вместо `SetTime`, чтобы день увеличивался при пробуждении.

#### 🟢 DONE — P0.2 `Calendar` + `Data/calendar.json`, регистрация в `ServiceLocator`, save-объект
> **Куда легло:** [MTEngine/Core/Calendar.cs](../MTEngine/Core/Calendar.cs) + [SandboxGame/Content/Data/calendar.json](../SandboxGame/Content/Data/calendar.json). `Calendar` — `[SaveObject("calendar")]`, грузится в `Game1.LoadContent`, регистрируется в `ServiceLocator` и в `SaveGameManager`. Добавлен `GamePaths.Data` и `ContentPaths.DataRoot`.

#### 🟢 DONE — P0.3 `AgeComponent` + `AgingSystem` (пока только подсчёт лет, без смерти)
> **Куда легло:** [MTEngine/Npc/AgeComponent.cs](../MTEngine/Npc/AgeComponent.cs) с DataField-прокси `ageYears`; [MTEngine/Npc/AgingSystem.cs](../MTEngine/Npc/AgingSystem.cs) подписан на `DayChanged` + ленивая инициализация `BirthDayIndex` в `Update`. Зарегистрирован в `GameEngine.Initialize`.

#### 🟢 DONE — P0.4 Прокинуть `AgeComponent` на player-прототип
> **Куда легло:** [SandboxGame/Content/Prototypes/Actors/Player/proto.json](../SandboxGame/Content/Prototypes/Actors/Player/proto.json) — добавлено `"age": { "ageYears": 25 }`.

#### 🟢 DONE — P0.5 `SleepSystem` перематывает `TotalSeconds`, а не клампит день
> **Куда легло:** Сделано в P0.1: `SleepSystem.StopSleep` использует новый `GameClock.AdvanceToHour`, который при необходимости увеличивает `DayIndex` и публикует `DayChanged`.

#### 🟢 DONE — P0.6 `EntityDied` эвент в `HealthSystem`
> **Куда легло:** [MTEngine/Core/LifeEvents.cs](../MTEngine/Core/LifeEvents.cs) — `enum DeathCause` + `struct EntityDied`. `HealthSystem.PublishDeath` дёргает `EventBus` в момент перехода в `IsDead`.

### P1 — иерархия мира и area-zones (2–3 фазы кода)

#### 🟢 DONE — P1.1 `AreaZoneData` + `MapData.Areas`
> **Куда легло:** [MTEngine/World/AreaZone.cs](../MTEngine/World/AreaZone.cs) — `AreaZoneKinds`, `AreaPointData`, `AreaZoneData` (Id/Kind/Properties/Tiles/Points + `GetPoint`, `GetPointsByPrefix`). `MapData.Areas` + расширенная `Validate`.

#### 🟢 DONE — P1.2 Загрузка/сериализация в `MapManager` и `MapEntitySpawner`
> **Куда легло:** Работает через существующий `JsonSerializer.Deserialize<MapData>` / `Serialize` — поле `areas` подхватывается автоматически. Старые карты без `areas` грузятся как пустой список. `SaveGameManager.CloneMap` сохраняет `Areas`. `MapEntitySpawner` пока не использует `Areas` — это понадобится только в P3+.

#### 🟢 DONE — P1.3 MTEditor: режим рисования area-zones с пикером `Kind` и редактором `Properties` + named-точек
> **Куда легло:** [MTEditor/Tools/AreaZoneTool.cs](../MTEditor/Tools/AreaZoneTool.cs) — отдельный слой редактирования `MapData.Areas`: paint/erase в brush-mode, add/trim/select в mouse-mode, пикер `Kind`, редактирование одного `Properties`-ключа за раз, named-точки через `Point ID`. [MTEditor/EditorGame.cs](../MTEditor/EditorGame.cs) подключает инструмент как `Tool.AreaZone` / hotkey `5`, [MTEditor/UI/EditorHUD.cs](../MTEditor/UI/EditorHUD.cs) добавляет меню `Area Zones` и счётчик `Areas`, [MTEditor/ResizeMapHistory.cs](../MTEditor/ResizeMapHistory.cs) сохраняет/crop’ает `Areas` при resize.

#### 🟢 DONE — P1.4 Структуры `FactionDef`, `SettlementDef`, `DistrictDef`, `HouseDef`, `ProfessionSlotDef`
> **Куда легло:** [MTEngine/Npc/WorldDefinitions.cs](../MTEngine/Npc/WorldDefinitions.cs) — POCO с минимальными полями. `HouseDef` сразу с `BedSlots`, `ChildBedSlots`, `ResidentNpcSaveIds`, `ForSale` (вычислимое). `ProfessionSlotDef` с `WorkAnchor`, `OccupiedNpcSaveId`, `IsVacant`.

#### 🟢 DONE — P1.5 `WorldRegistry` — строится при старте после загрузки всех карт
> **Куда легло:** [MTEngine/Npc/WorldRegistry.cs](../MTEngine/Npc/WorldRegistry.cs) — `RebuildFromMaps(MapManager)` пробегает по `GetAvailableMaps`, читает `area.Kind` и наполняет таблицы. Прокидывает родительские связи (house → district → settlement → faction). Query: `HousesInDistrict`, `HousesInSettlement`, `EmptyHousesInSettlement`, `VacantProfessionsInSettlement`, `FindHouseByMapAndTile`. Регистрируется в `GameEngine` и `ServiceLocator`, `Game1.LoadContent` дёргает `RebuildFromMaps`.

#### 🟢 DONE — P1.6 Save для `WorldRegistry` (резидентность домов, занятость профессий)
> **Куда легло:** `WorldRegistry` помечен `[SaveObject("worldRegistry")]`, динамика лежит в `HouseResidency` / `ProfessionOccupation` (оба `[SaveField]`). Цикл: `CaptureDynamicState` перед save, `RehydrateDynamicState` после load. Подключено в `Game1.HandleSaveSlotConfirmed` и `Game1.RestoreLoadedSession`. Статика (Houses/Districts/…) каждый раз восстанавливается из карт — это правильно, потому что карты могут меняться между сейвами.

#### 🟢 DONE — P1.7 Документация по конвенциям точек
> **Куда легло:** Покрыто в §2.1–2.2 этого документа: house = минимум одна точка `bed_slot_*` + опц. `child_bed_*`; profession = `work_anchor` + `Properties.professionId`; school/inn/tavern/orphanage/wander = `wander_*` точки. Реализовано в `WorldRegistry.EnsureHouse/EnsureProfession`, `HouseDef.BedSlots/ChildBedSlots` через `area.GetPointsByPrefix("bed_slot")` / `"child_bed"`.

#### 🟢 DONE — Дополнительно: наследование прототипов через `base`
> **Куда легло:** [MTEngine/Core/PrototypeManager.cs](../MTEngine/Core/PrototypeManager.cs) резолвит `base` перед загрузкой tile/entity/substance-прототипов. Топ-левел поля дочернего прототипа заменяют базовые, а `components` мержатся по ключу компонента: если дочерний прототип объявляет тот же компонент, заменяется только этот компонент. `abstract: true` скрывает базовые прототипы из `GetAllTiles/GetAllEntities/GetAllSubstances`, но прямой `Get*("base_id")` остаётся доступен для наследования. Контент переведён на `SandboxGame/Content/Prototypes/Base/*`: `base_entity`, `base_actor`, `base_item`, `base_furniture`, `base_weapon`, `base_tile`, `base_substance`.

### P2 — NPC-каркас (один город, без социалки)

#### 🟢 DONE — P2.1 Базовые компоненты NPC
> **Куда легло:** `MTEngine/Npc/`:
> - [NpcTagComponent.cs](../MTEngine/Npc/NpcTagComponent.cs) — маркер
> - [IdentityComponent.cs](../MTEngine/Npc/IdentityComponent.cs) — `FirstName/LastName/Gender/FactionId/SettlementId/DistrictId` + `FullName`
> - [PersonalityComponent.cs](../MTEngine/Npc/PersonalityComponent.cs) — `Infidelity/Vengefulness/ChildWish/MarriageWish/Sociability/Pacifist`. Метод `RollMissing(Random)` — катает дефолты −1 в [0..10]; пацифист обнуляет Vengefulness
> - [SkillsComponent.cs](../MTEngine/Npc/SkillsComponent.cs) — `npcSkills`, `Get/Set/Add` + `Best()` для лучшего навыка; id разведен с уже существующим боевым компонентом `skills`
> - [ResidenceComponent.cs](../MTEngine/Npc/ResidenceComponent.cs) — `HouseId`, `BedSlotId`, `IsHomeless`
> - [KinComponent.cs](../MTEngine/Npc/KinComponent.cs) — список `KinLink(NpcSaveId, KinKind)` + `OfKind/Add/RemoveAll`
> Все компоненты с `[RegisterComponent]` и `[SaveField]`/`[DataField]` — автоматически грузятся из proto.json и попадают в save.

#### 🟢 DONE — P2.2 Прототип `NpcBase`
> **Куда легло:** [SandboxGame/Content/Prototypes/Actors/NpcBase/proto.json](../SandboxGame/Content/Prototypes/Actors/NpcBase/proto.json) — id `npc_base`, `base: "base_actor"`. Спрайт и анимации временно ссылаются на `../Player/sprite.png` и `../Player/animations.json`. Общий actor-набор (`transform`, `collider`, `hands`, `equipment`, `health`, `wounds`) наследуется из `base_actor`; сам `npc_base` добавляет/переопределяет `velocity` (скорость 70), `npc`, `identity`, `personality`, `npcSkills`, `residence`, `kin`, `age`, `schedule`, `interactable`, `info`.
> `npc_base` также содержит компонент `hair` с дефолтной причёской `hair_short_messy`; визуальный слой волос лежит отдельно от базового спрайта и может переопределяться в `.npc`.

#### 🟢 DONE — Дополнительно: слой причёсок NPC
> **Куда легло:** [MTEngine/Npc/HairComponents.cs](../MTEngine/Npc/HairComponents.cs), [MTEngine/Rendering/Renderer.cs](../MTEngine/Rendering/Renderer.cs), [SandboxGame/Content/Prototypes/Hair/](../SandboxGame/Content/Prototypes/Hair/). `hairStyle` — прототип причёски: пол (`Unisex/Male/Female`), белый `sprite.png`, `animations.json`, размеры и offset. `hair` — компонент NPC: `styleId`, `color`, `visible`. Рендер рисует волосы поверх тела/одежды, синхронизирует `idle_down/up/left/right` с основным спрайтом и использует те же transform/rotation/scale, поэтому слой поворачивается вместе с персонажем.

#### 🟢 DONE — P2.3 `WorldPopulationStore`
> **Куда легло:** [MTEngine/Npc/WorldPopulationStore.cs](../MTEngine/Npc/WorldPopulationStore.cs) — `NpcSnapshot` (SaveId / PrototypeId / Name / MapId / X / Y + `Dictionary<string, JsonObject> Components`) + `WorldPopulationStore` (`Has/Get/Put/Remove/InMap`). Помечен `[SaveObject("worldPopulation")]`. Зарегистрирован в `GameEngine` и `Game1.LoadContent` как save-объект. Активной записи/чтения снапшотов пока нет — заполнится в P6 (LOD); сейчас сохраняется и грузится как пустой словарь.

#### 🟢 DONE — P2.4 Загрузка стартовых NPC из `Maps/<settlement>.npc`
> **Куда легло:** [MTEngine/Npc/NpcRosterFile.cs](../MTEngine/Npc/NpcRosterFile.cs) — модель `NpcRosterEntry` (id, proto, ageYears, identity, personality, skills, residence, profession, kin, description, `components`, `outfit`, `hands`, `inventory`). [SandboxGame/Game/NpcRosterSpawner.cs](../SandboxGame/Game/NpcRosterSpawner.cs) подписан на `MapLoadedEvent`: если для карты нет savestate (проверяет `SaveGameManager.GetMapEntityStates`), читает `Maps/<mapId>.npc` (`<mapId>.npcs.json` остаётся fallback) и спаунит NPC. Для каждого NPC: создаётся entity из `proto`, предварительно смерженного с персональными `components`; проставляются `IdentityComponent`, `AgeComponent` (BirthDayIndex от `ageYears`), `PersonalityComponent` + `RollMissing`, `SkillsComponent`, `ResidenceComponent` (с регистрацией в `HouseDef.ResidentNpcSaveIds`), `ProfessionSlotDef.OccupiedNpcSaveId`, kin-связи (вторым проходом, чтобы все NPC уже имели SaveId), одежда, предметы в руках и инвентарь. Позиция спауна = `bed_slot_id` дома → любая bed-точка → первый тайл дома → spawn point карты.

#### 🟢 DONE — Дополнительно: MTEditor-вкладка `NPCs` и формат `.npc`
> **Куда легло:** [MTEditor/UI/NpcEditorPanel.cs](../MTEditor/UI/NpcEditorPanel.cs), [MTEditor/EditorGame.cs](../MTEditor/EditorGame.cs), [MTEditor/UI/EditorHUD.cs](../MTEditor/UI/EditorHUD.cs). Вкладка `NPCs` создаёт, редактирует, удаляет и сохраняет людей в `Maps/<mapId>.npc`, а также умеет сохранить текущие настройки как `npc_*` прототип-шаблон в `Prototypes/Actors/NpcTemplates/`. Поля ссылок переведены с ручного ввода на picker/dropdown: `proto`, фракции, поселения, дома, bed slots, profession slots, schedule templates, одежда, item-прототипы и причёски. `districtId` выводится из дома/карты и показывается как derived zone. Appearance-preview в карточке NPC рисует базовый спрайт с `Skin Color`, экипировку и волосы с выбранным цветом; кнопки `<`/`>` переключают направление. Та же панель умеет создать/обновить прототип причёски в `Prototypes/Hair/<Name>/`.

#### 🟢 DONE — Дополнительно: старт игры через Global Settings + Character Creator
> **Куда легло:** [MTEditor/UI/GlobalSettingsPanel.cs](../MTEditor/UI/GlobalSettingsPanel.cs) — список всех карт стал кликабельным, выбранная карта/`StartingSpawnId` и `startingOutfit` сохраняются в `Maps/world_data.json`. [SandboxGame/UI/MenuSystem.cs](../SandboxGame/UI/MenuSystem.cs), [SandboxGame/UI/CharacterCreatorModels.cs](../SandboxGame/UI/CharacterCreatorModels.cs), [SandboxGame/UI/CharacterPreviewRenderer.cs](../SandboxGame/UI/CharacterPreviewRenderer.cs), [SandboxGame/Game1.cs](../SandboxGame/Game1.cs) — `Начать игру` открывает character creator (пол, причёска, цвет волос, цвет кожи), затем грузит `WorldData.StartingMapId`; стартовая одежда отображается в превью и экипируется на player entity; хардкод стартовой карты из `settings.json`/`Game1` убран.

#### 🟢 DONE — Дополнительно: глобальный редактор профессий
> **Куда легло:** [MTEngine/Npc/ProfessionCatalog.cs](../MTEngine/Npc/ProfessionCatalog.cs), [SandboxGame/Content/Data/professions.json](../SandboxGame/Content/Data/professions.json), [MTEditor/UI/ProfessionEditorPanel.cs](../MTEditor/UI/ProfessionEditorPanel.cs), [MTEditor/Tools/AreaZoneTool.cs](../MTEditor/Tools/AreaZoneTool.cs). Профессии теперь задаются глобально (`id/name/primarySkill/isTrader/tradeTags/restock/description`), а profession-area на карте выбирает профессию из dropdown и хранит только `professionId`.

#### 🟢 DONE — P2.5 UI: расширить `InfoComponent` чтобы показывал имя/возраст/семейный статус
> **Куда легло:** [MTEngine/Components/InfoComponent.cs](../MTEngine/Components/InfoComponent.cs) — `OpenInfoWindow` теперь:
> - заголовок окна = `IdentityComponent.FullName`, если есть;
> - перед `Description` подмешивается блок NPC-фактов через `ComposeNpcInfo`: «Имя», «Пол», «Возраст N лет/года/год» (склонение через `YearWord`), «Прописан: settlement, district», «Родня: N» (по `KinComponent`).
> - Семейный статус выводится из `RelationshipsComponent` с P4.2; при наличии партнёра временно показывается его `SaveId`.
> Зависимость `MTEngine.Components → MTEngine.Npc` ОК (всё внутри `MTEngine.dll`).

### P3 — расписание + базовый AI движения

#### 🟢 DONE — P3.1 `ScheduleComponent` + `Data/schedule_templates.json`
> **Куда легло:**
> - [MTEngine/Npc/ScheduleComponent.cs](../MTEngine/Npc/ScheduleComponent.cs) — `ScheduleAction` (Sleep/EatAtHome/Work/Wander/Socialize/Visit/StayInTavern/SchoolDay/Free), `ScheduleSlot`, `FreetimeOption`, `ScheduleComponent` с `FindSlot(hour)` (поддерживает wrap через полночь).
> - [MTEngine/Npc/ScheduleTemplates.cs](../MTEngine/Npc/ScheduleTemplates.cs) — загрузчик + сервис; `Apply(component, templateId)` копирует слоты.
> - [SandboxGame/Content/Data/schedule_templates.json](../SandboxGame/Content/Data/schedule_templates.json) — 4 шаблона: `default_worker`, `default_unemployed`, `default_child`, `default_pensioner`. `targetAreaId` поддерживает плейсхолдеры `$house`, `$profession`, `$inn`, `$school` — конкретный area-id подставит ScheduleSystem.
> - Зарегистрировано в `Game1.LoadContent`. `npc_base.proto.json` получил `"schedule": { "templateId": "default_unemployed" }`.

#### 🟢 DONE — P3.2 `ScheduleSystem` с `ResolveCurrentSlot` и Freetime-роллом
> **Куда легло:**
> - [MTEngine/Npc/ScheduleSystem.cs](../MTEngine/Npc/ScheduleSystem.cs) — тикает раз в секунду по NPC активной карты. Применяет шаблон лениво (если `Slots` пустые), резолвит слот через `ScheduleComponent.FindSlot(hour)`, а для `Free` катает `FreetimeOption` с фильтром `dayOnly/nightOnly` и пока пропускает неподдержанные conditions. Затем разворачивает плейсхолдер `$house/$profession/$inn/$school` и кладёт результат в [NpcIntentComponent](../MTEngine/Npc/NpcIntentComponent.cs) (`Action`, `TargetAreaId`, `TargetPointId`, `TargetMapId`, `TargetX/Y`, `HasTarget`, `Arrived`).
> - Чтобы schedule видел работу NPC без рефлексии — введён мини-[ProfessionComponent](../MTEngine/Npc/ProfessionComponent.cs) (`ProfessionId`, `SlotId`, `JoinedDayIndex`); `NpcRosterSpawner` добавляет его, когда в roster есть `profession.slotId`.
> - Зарегистрирован в `GameEngine.Initialize` как `ScheduleSystem`.

#### 🟢 DONE — P3.3 A*-пасфайндер по `TileMap`
> **Куда легло:** [MTEngine/Npc/GridPathfinder.cs](../MTEngine/Npc/GridPathfinder.cs) — статический `FindPath(map, from, to, maxNodes=4000, isBlocked=null)`. 4-связный, манхэттенская эвристика, `PriorityQueue<Point,int>`. Возвращает список тайловых точек включая обе крайние; пустой список = пути нет. Запрет: ходить через solid-тайлы и через дополнительные блокеры, переданные вызывающей системой. Лимит узлов защищает от долгих обсчётов на больших картах.

#### 🟢 DONE — P3.4 `NpcMovementSystem`
> **Куда легло:** [MTEngine/Npc/NpcMovementSystem.cs](../MTEngine/Npc/NpcMovementSystem.cs) — на каждый NPC: если интент имеет цель в текущей карте — строит путь через `GridPathfinder.FindPath`, идёт к следующей waypoint-точке. Принудительный repath раз в секунду (`ReplanInterval`) или при смене цели. Достигнутая waypoint = расстояние ≤ 3px. Скорость берётся из `VelocityComponent.Speed`. Мёртвые NPC останавливаются. Для NPC `location_transition`-триггеры текущей карты считаются непроходимыми тайлами, чтобы фоновая прогулка и путь к работе не уводили их из локации. Во время движения NPC переключает directional idle-спрайт (`idle_up/down/left/right`) по направлению шага так же, как игрок. Зарегистрирован в `GameEngine.Initialize` после `ScheduleSystem`.

#### 🟢 DONE — P3.5 Расширить `BedComponent` до двух слотов сна
> **Куда легло:** [MTEngine/Metabolism/BedComponent.cs](../MTEngine/Metabolism/BedComponent.cs) — добавлен `BedSlot` и `SleepSlots` с сериализованным ключом `"slots"`, плюс owner/lie-offset поля для совместимости с одиночной кроватью. Название свойства намеренно `SleepSlots`, чтобы не попасть под общий save-skip для equipment `Slots`.

#### 🟢 DONE — P3.6 Базовый «Wander» — обход `wander_*` точек в area
> **Куда легло:** [MTEngine/Npc/ScheduleSystem.cs](../MTEngine/Npc/ScheduleSystem.cs) — wander-like действия (`Wander`, `SchoolDay`, `StayInTavern`) держат текущую цель до прибытия, затем идут по отсортированным `wander_*` точкам выбранной area по кругу. Если точек нет, обходят тайлы area. Дефолтный `Free` без area теперь выбирает случайную проходимую точку по всей карте, исключая чужие дома и `location_transition`-триггеры. Когда `Wander` доходит до точки, `NpcMovementSystem` держит NPC на месте 2 секунды и только потом разрешает `ScheduleSystem` выбрать следующую точку.
> **Дополнение:** `Socialize` добавлен как freetime/wander-event. [ScheduleSystem](../MTEngine/Npc/ScheduleSystem.cs) подбирает пару свободных NPC на активной карте, ищет две соседние проходимые точки вне чужих домов/переходов, ведёт обоих к месту встречи, разворачивает лицом друг к другу, показывает короткие popup-реплики и через 5–9 секунд отпускает обоих обратно в обычный freetime. Шаблоны в [schedule_templates.json](../SandboxGame/Content/Data/schedule_templates.json) получили `Socialize` на равном приоритете с `Wander` для дневного фритайма.

#### 🟢 DONE — P3.7 Эвенты `NpcArrivedAtArea` (для других систем)
> **Куда легло:** [MTEngine/Npc/NpcEvents.cs](../MTEngine/Npc/NpcEvents.cs) — `NpcArrivedAtArea`. [MTEngine/Npc/NpcMovementSystem.cs](../MTEngine/Npc/NpcMovementSystem.cs) публикует событие один раз на цель и отмечает `NpcIntentComponent.Arrived`.

### P4 — социальный слой (одиночное поселение, активная зона)

#### 🟢 DONE — P4.1 `RelationshipsComponent` + `MatchmakingSystem`
> **Куда легло:**
> - [MTEngine/Npc/RelationshipsComponent.cs](../MTEngine/Npc/RelationshipsComponent.cs) — `enum RelationshipStatus { Single, Dating, Engaged, Married, Widowed, Separated }` + поля `PartnerNpcSaveId`, `PartnerIsPlayer`, `ScheduledDateDayIndex`, `DatingStartedDayIndex`, `ScheduledWeddingDayIndex`, `MarriageDayIndex`, `ScheduledBirthDayIndex`, `LastMatchSearchDayIndex`, `OvernightStreak`. `−1L` используется как «не запланировано». `[RegisterComponent("relationships")]`, все поля `[DataField]`+`[SaveField]`.
> - [MTEngine/Npc/RelationshipEvents.cs](../MTEngine/Npc/RelationshipEvents.cs) — `RelationshipDateScheduled` (NpcASaveId, NpcBSaveId, DateDayIndex, WeddingDayIndex). `Started/Married` добавлены в P4.2; `NpcMovedHouse` добавлен в P4.4. `Separated` появится вместе с изменами/разрывами.
> - [MTEngine/Npc/MatchmakingSystem.cs](../MTEngine/Npc/MatchmakingSystem.cs) — подписан на `DayChanged`. Каждый игровой день: собирает eligible singles (Status=Single, нет partnerId, age≥18, есть SettlementId, есть SaveEntityIdComponent), группирует по `Identity.SettlementId`, по seeker’ам с `LastMatchSearchDayIndex + 7 ≤ today` ищет противоположный пол, не родственника, ещё не спаренного в этом тике; пара получает `daysToDate = avg(rand(2..14)*2) + today`, `weddingDay = dateDay + avg(rand(30..120)*2)`. Публикует `RelationshipDateScheduled`. Status пока остаётся `Single` — переход в `Dating` это P4.2.
> - [MTEngine/ECS/SaveEntityIdComponent.cs](../MTEngine/ECS/SaveEntityIdComponent.cs) — компонент-маркер вынесен из `SandboxGame.Save` в движок, чтобы движковые системы (включая Matchmaking) могли читать `SaveId` без обратной зависимости на слой сохранений. `SaveGameManager` продолжает его создавать/читать как раньше.
> - [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs) — `MatchmakingSystem` зарегистрирован после `AgingSystem` и до `ScheduleSystem`.
> - [SandboxGame/Content/Prototypes/Actors/NpcBase/proto.json](../SandboxGame/Content/Prototypes/Actors/NpcBase/proto.json) — добавлен пустой компонент `"relationships": {}`, всем спавнящимся NPC сразу есть в чём хранить статус.

#### 🟢 DONE — P4.2 Наступление запланированных событий: `Single → Dating → Married`
> **Куда легло:**
> - [MTEngine/Npc/RelationshipTickSystem.cs](../MTEngine/Npc/RelationshipTickSystem.cs) — подписан на `DayChanged`. По `today >= ScheduledDateDayIndex` переводит зеркальную пару `Single → Dating`, записывает `DatingStartedDayIndex`, очищает `ScheduledDateDayIndex` и публикует `RelationshipStarted`. По `today >= ScheduledWeddingDayIndex` переводит `Dating/Engaged → Married`, записывает `MarriageDayIndex`, очищает отработанные даты, сбрасывает `OvernightStreak` и публикует `RelationshipMarried`.
> - [MTEngine/Npc/RelationshipEvents.cs](../MTEngine/Npc/RelationshipEvents.cs) — добавлены `RelationshipStarted(NpcASaveId, NpcBSaveId, DayIndex)` и `RelationshipMarried(NpcASaveId, NpcBSaveId, DayIndex)`.
> - [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs) — `RelationshipTickSystem` зарегистрирован после `MatchmakingSystem`, до `ScheduleSystem`.
> - [MTEngine/Components/InfoComponent.cs](../MTEngine/Components/InfoComponent.cs) — окно «Инфо» теперь показывает семейный статус NPC из `RelationshipsComponent`.

#### 🟢 DONE — P4.3 Ночёвка вместе
> **Куда легло:** [MTEngine/Npc/ScheduleSystem.cs](../MTEngine/Npc/ScheduleSystem.cs) — при `ScheduleAction.Sleep` сначала проверяется `TryResolveOvernightSleepTarget`. Для пары `Dating/Engaged/Married` находится активный партнёр по `SaveEntityIdComponent`, проверяется зеркальная ссылка, затем стабильным hash-роллом выбирается шанс и дом-хост на текущую игровую ночь. Хозяин идёт в свой `Residence.BedSlotId`, гость — в другой `bed_slot_*` того же `HouseDef`. Шанс для Dating растёт от 0.10 до 0.95 между `DatingStartedDayIndex` и `ScheduledWeddingDayIndex`; для Married = 1.0. Если дом партнёра не на текущей карте или нет валидной пары, система откатывается к обычной цели сна.

#### 🟢 DONE — P4.4 Переезд жены в дом мужа
> **Куда легло:** [MTEngine/Npc/RelationshipTickSystem.cs](../MTEngine/Npc/RelationshipTickSystem.cs) — во время `RelationshipMarried` вызывается `TryMoveSpouseIntoSharedHome`. По умолчанию переезжает `Female` к `Male`; если дом принимающей стороны невалиден, система делает fallback в дом второго партнёра. Обновляются `ResidenceComponent.HouseId/BedSlotId`, `IdentityComponent.FactionId/SettlementId/DistrictId`, `HouseDef.ResidentNpcSaveIds` старого и нового дома. Свободный `bed_slot_*` выбирается по фактически занятым `Residence.BedSlotId`. Публикуется `NpcMovedHouse(NpcSaveId, OldHouseId, NewHouseId, DayIndex)`.
> - [MTEngine/Npc/RelationshipEvents.cs](../MTEngine/Npc/RelationshipEvents.cs) — добавлен `NpcMovedHouse`.

#### 🟢 DONE — Дополнительно: зрение NPC и защита дома без вечного преследования
> **Куда легло:** [MTEngine/Npc/NpcPerception.cs](../MTEngine/Npc/NpcPerception.cs), [MTEngine/Npc/HomeIntrusionSystem.cs](../MTEngine/Npc/HomeIntrusionSystem.cs), [MTEngine/Npc/NpcCombatReactionSystem.cs](../MTEngine/Npc/NpcCombatReactionSystem.cs), [MTEngine/Npc/NpcAggressionComponent.cs](../MTEngine/Npc/NpcAggressionComponent.cs). NPC видит цель через дистанцию 420px, FOV 150° и `TileMap.HasWorldLineOfSight`; вблизи 96px работает круговая осведомлённость. Домашняя агрессия помечается как `HomeIntrusion` с `ProtectedHouseId`. Если игрок не бил хозяина и вышел из этого дома, агрессия сразу снимается; если игрок ударил NPC, ситуация становится `SelfDefense` и дальше живёт по обычным правилам потери видимости.

#### 🟢 DONE — P4.5 `BirthSystem`, `ChildGrowthSystem` + наследование скиллов (§6)
> **Куда легло:**
> - [MTEngine/Npc/PregnancyPlanningSystem.cs](../MTEngine/Npc/PregnancyPlanningSystem.cs) — на `DayChanged` в 1-й день игрового месяца (`Calendar.FromDayIndex(today).Day == 1`) перебирает женатые пары обоих фертильных (18..40), без активной беременности, в дому с свободным `child_bed_*`. Шанс зачатия = `avg(ChildWish) * 0.10`. При успехе на обоих партнёрах ставится `RelationshipsComponent.ScheduledBirthDayIndex = today + rand(14..35)` и публикуется `PregnancyScheduled`.
> - [MTEngine/Npc/BirthSystem.cs](../MTEngine/Npc/BirthSystem.cs) — на `DayChanged` ловит дату рождения, спаунит ребёнка на текущей карте дома (off-map births пока пропускаются — API снапшотов есть в P6.4, автоматическая интеграция остаётся в P6.5). Прото `npc_base`, гендер случайный, фамилия отца, имя из встроенного пула, `AgeComponent.BirthDayIndex = today`, `ResidenceComponent` в свободный `child_bed_*`, `KinComponent` зеркально для матери/отца + сиблинги по матери, `ScheduleComponent.TemplateId = "default_child"`. Причёска ребёнка выбирается случайно из `hairStyle`-прототипов, совместимых с его полом; цвет волос берётся случайно от одного из родителей. Целевые скиллы фиксируются в `ChildGrowthComponent` (см. §6.3): для лучшего скилла каждого родителя `target = 0.8*max(parents)`, остальное — `avg(parents)`. После рождения у обоих родителей `ChildWish /= 2`. Публикуется `NpcBorn`.
> - [MTEngine/Npc/ChildGrowthComponent.cs](../MTEngine/Npc/ChildGrowthComponent.cs) — `[RegisterComponent("childGrowth")]` с `TargetSkills`, `FatherSaveId`, `MotherSaveId`. Все поля сериализуются.
> - [MTEngine/Npc/ChildGrowthSystem.cs](../MTEngine/Npc/ChildGrowthSystem.cs) — раз в день: для каждого ребёнка `skills[id] = target * (years/18)`. На совершеннолетии (`years >= 18`) проставляет финальные значения, удаляет `ChildGrowthComponent` и переключает шаблон расписания с `default_child` на `default_unemployed` (передача в `JobMarketSystem` — TODO P5.2).
> - [MTEngine/Npc/RelationshipEvents.cs](../MTEngine/Npc/RelationshipEvents.cs) — добавлены `PregnancyScheduled(MotherSaveId, FatherSaveId, BirthDayIndex)` и `NpcBorn(ChildSaveId, FatherSaveId, MotherSaveId, HouseId, DayIndex)`.
> - [MTEngine/Npc/ChildNameGenerator.cs](../MTEngine/Npc/ChildNameGenerator.cs) — встроенный пул имён по гендеру; позже заменим на чтение из `Data/names_<faction>.json`.
> - [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs) — `PregnancyPlanningSystem`, `BirthSystem`, `ChildGrowthSystem` зарегистрированы между `RelationshipTickSystem` и `ScheduleSystem`.

### P5 — профессии и торговля

#### 🟢 DONE — P5.1 `ProfessionCatalog` + редактор глобальных профессий
> **Куда легло:** [MTEngine/Npc/ProfessionCatalog.cs](../MTEngine/Npc/ProfessionCatalog.cs), [SandboxGame/Content/Data/professions.json](../SandboxGame/Content/Data/professions.json), [MTEditor/UI/ProfessionEditorPanel.cs](../MTEditor/UI/ProfessionEditorPanel.cs). Старую идею `ProfessionPrototype` заменили на глобальный JSON-каталог: профессия задаётся один раз на мир, а города/карты только ссылаются на неё через `professionId`. Торговля задаётся через `tradeTags` по тегам item-прототипов; параметры restock/качества описаны в §7.6.

#### 🟢 DONE — P5.2 `ProfessionComponent` + `JobMarketSystem`
> **Куда легло:**
> - [MTEngine/Npc/JobMarketSystem.cs](../MTEngine/Npc/JobMarketSystem.cs) — на `DayChanged`, на `EntityDied` (освобождает слот умершего) и по внешнему запросу `RequestFillNow()` собирает безработных совершеннолетних не-пенсионеров (`Identity.SettlementId != ""`, `Age.Years >= 18`, нет `ProfessionComponent`, нет `ChildGrowthComponent`). Перебирает свободные `ProfessionSlotDef` по поселениям, для каждой вакансии ищет лучшего по `primarySkill` из `ProfessionCatalog.Get(slot.ProfessionId)`. Назначает: ставит `ProfessionComponent { ProfessionId, SlotId, JoinedDayIndex }`, переключает `ScheduleComponent.TemplateId` на `default_worker`, обновляет `ProfessionSlotDef.OccupiedNpcSaveId/OccupiedSinceDayIndex` (через который `WorldRegistry.CaptureDynamicState()` уже сохраняет занятость).
> - [MTEngine/Npc/ChildGrowthSystem.cs](../MTEngine/Npc/ChildGrowthSystem.cs) — при совершеннолетии вызывает `JobMarketSystem.RequestFillNow()`, чтобы выпускник сразу попал в очередь без ожидания следующего DayChanged.
> - [SandboxGame/Game1.cs](../SandboxGame/Game1.cs) — загружает `ProfessionCatalog` из `Data/professions.json` и регистрирует в `ServiceLocator`, чтобы движковые системы могли его читать (раньше каталог жил только в редакторе).
> - [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs) — `JobMarketSystem` зарегистрирован между `ChildGrowthSystem` и `ScheduleSystem`.
> Резерв: при отсутствии подходящего поселения у NPC (например, бездомный пришлый) подбор не делается — это явное условие §7.4 (безработный → шаблон default_unemployed). Catch-up при пересечении зон оставлен для P6.5.

#### 🟢 DONE — P5.3 `ProfessionTickSystem` (рост навыка)
> **Куда легло:** [MTEngine/Npc/ProfessionTickSystem.cs](../MTEngine/Npc/ProfessionTickSystem.cs) — на `DayChanged` каждый NPC с `ProfessionComponent` получает `+SkillGainPerDay` к `PrimarySkill` своей профессии (берётся из `ProfessionCatalog`). Дети (`ChildGrowthComponent`) и пенсионеры пропускаются. Distant-NPC тикаются так же — catch-up по зонам отложен на P6.5.

#### 🟢 DONE — P5.4 `ShopComponent` + `ShopRestockSystem`
> **Куда легло:**
> - [MTEngine/Npc/ShopComponent.cs](../MTEngine/Npc/ShopComponent.cs) — save-компонент магазина: `ProfessionSlotId`, `OwnerNpcSaveId`, `NextRestockDayIndex`, `Stock` как список `proto + qty + price + qualityTier + displayName`. Физические entity товаров не висят в мире; предмет материализуется только при покупке.
> - [MTEngine/Npc/ShopRestockSystem.cs](../MTEngine/Npc/ShopRestockSystem.cs) — на загрузке карты/новом дне добавляет `ShopComponent` активным NPC с торговой профессией (`ProfessionCatalog.IsTrader`) и пересобирает ассортимент, когда stock пустой или наступил `NextRestockDayIndex`.
> - [MTEngine/Npc/ShopPricing.cs](../MTEngine/Npc/ShopPricing.cs) — общий расчёт цены и чтение `item.tags`/`qualityTier`.
> Restock выбирает кандидатов из item-прототипов по пересечению `profession.tradeTags` и `item.tags`. Качество фильтруется по навыку владельца: новичок видит в основном `tier 4`, с ростом `primarySkill` открываются `tier 3/2/1`. Предмет без `qualityTier` считается `tier 4`.

#### 🟢 DONE — P5.5 UI окна торговли
> **Куда легло:**
> - [MTEngine/Npc/TradeSystem.cs](../MTEngine/Npc/TradeSystem.cs) — overlay-окно торговли: левая колонка — товары продавца, правая — вещи игрока из рук/экипировки, клик покупает/продаёт, колесо скроллит список, `Esc` закрывает.
> - [MTEngine/Core/ITradeUiService.cs](../MTEngine/Core/ITradeUiService.cs), [MTEngine/Systems/InteractionSystem.cs](../MTEngine/Systems/InteractionSystem.cs), [SandboxGame/Systems/PlayerMovementSystem.cs](../SandboxGame/Systems/PlayerMovementSystem.cs) — пока торговое окно открыто, обычные interaction-меню/storage закрываются, а управление игроком замирает.
> - [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs), [SandboxGame/Game1.cs](../SandboxGame/Game1.cs) — `TradeSystem` и `ShopRestockSystem` зарегистрированы, шрифт для окна торговли прокинут из игры.
> Покупка создаёт item-entity из прототипа и пытается положить его в руки игрока, затем в доступные storage из рук/экипировки. Продажа принимает вещи из рук и экипировки, начисляет монеты игроку и добавляет проданный proto обратно в stock продавца.

#### 🟢 DONE — P5.6 Безработные → ночёвка в `Inn`
> **Куда легло:** [MTEngine/Npc/ScheduleSystem.cs](../MTEngine/Npc/ScheduleSystem.cs) — для `ScheduleAction.Sleep` бездомных NPC (нет валидного `Residence.HouseId`) добавлен `ResolveInnBedTarget`: ищет первый свободный `inn_bed_*` в Inn-зоне (любая на карте, либо явная через `$inn` → `concreteAreaId`), фиксирует `ResidenceComponent.HouseId = innArea.Id`, `BedSlotId = inn_bed_X`, чтобы NPC возвращался в ту же кровать. При проверке занятости учитываются `ResidenceComponent.BedSlotId` всех NPC, привязанных к этой Inn-зоне. Если все слоты заняты — фоллбэк на Tavern → Wander (NPC проводит ночь без кровати, как описано в §7.4). Старый «всегда первый inn_bed_*» удалён.

#### 🟢 DONE — Дополнительно: трактирщик, аренда комнат и торговые часы
> **Куда легло:** [MTEngine/Npc/InnRentalSystem.cs](../MTEngine/Npc/InnRentalSystem.cs), [MTEngine/Npc/MerchantWorkRules.cs](../MTEngine/Npc/MerchantWorkRules.cs), [MTEngine/Npc/ShopComponent.cs](../MTEngine/Npc/ShopComponent.cs), [MTEngine/Npc/TradeSystem.cs](../MTEngine/Npc/TradeSystem.cs), [MTEngine/Systems/SleepSystem.cs](../MTEngine/Systems/SleepSystem.cs), [MTEngine/Npc/WorldRegistry.cs](../MTEngine/Npc/WorldRegistry.cs), [MTEditor/Tools/AreaZoneTool.cs](../MTEditor/Tools/AreaZoneTool.cs), [MTEditor/UI/NpcEditorPanel.cs](../MTEditor/UI/NpcEditorPanel.cs). `innkeeper` обслуживает `inn` и `tavern`, сама `tavern` автоматически становится его рабочим слотом, он сдаёт комнаты/кровати на игровые сутки, подсвечивает дверь снятой комнаты жёлтым, а торговля доступна только в рабочий слот (`Work`, для innkeeper ещё `StayInTavern`).

### P6 — зональная симуляция и LOD

#### 🟢 DONE — P6.1 `LocationGraph` (BFS по `location_transition`-триггерам)
> **Куда легло:** [MTEngine/World/LocationGraph.cs](../MTEngine/World/LocationGraph.cs) — направленный граф карт, строится из всех `.map.json` через `MapManager.GetAvailableMaps()` / `LoadBaseMapData()` и рёбра `TriggerActionTypes.LocationTransition -> TargetMapId`. Даёт `Distance(fromMapId, toMapId)`, `IsReachable`, `GetOutgoing`, `MapsWithin`.
> [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs) регистрирует `LocationGraph` в `ServiceLocator`, [SandboxGame/Game1.cs](../SandboxGame/Game1.cs) перестраивает граф после создания `MapManager`. Недостижимая карта возвращает `int.MaxValue`; это будет входом для P6.2 `SimulationLodSystem.GetZone`.

#### 🟢 DONE — P6.2 `SimulationLodSystem.GetZone(npc)`
> **Куда легло:** [MTEngine/Npc/SimulationLodSystem.cs](../MTEngine/Npc/SimulationLodSystem.cs) — `SimulationLodZone { Active, Background, Distant }` и методы `GetZone(Entity)`, `GetZone(NpcSnapshot)`, `GetZone(mapId)`, `GetDistanceFromPlayerMap(mapId)`.
> Правило сейчас строго по §10.1: расстояние `0` от текущей карты игрока = `Active`, `1..2` = `Background`, недостижимо/дальше = `Distant`. Для live-entity текущая карта считается активной; для off-map NPC карта выводится из `NpcSnapshot.MapId`, `Residence.HouseId`, `Profession.SlotId` или первого district поселения.
> [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs) регистрирует систему в `World` и `ServiceLocator`. Фильтрация конкретных систем по зоне — следующий пункт P6.3.

#### 🟢 DONE — P6.3 Фильтрация в каждой системе: «работаю только если zone <= X»
> **Куда легло:** [MTEngine/Npc/NpcLod.cs](../MTEngine/Npc/NpcLod.cs) — общий helper `IsActive()` / `IsActiveOrBackground()` поверх [SimulationLodSystem](../MTEngine/Npc/SimulationLodSystem.cs). Сам `SimulationLodSystem` получил `IsWithin(Entity|NpcSnapshot, maxZone)`.
> **Active-only:** [ScheduleSystem](../MTEngine/Npc/ScheduleSystem.cs) (включая `Socialize`), [NpcMovementSystem](../MTEngine/Npc/NpcMovementSystem.cs), [HomeIntrusionSystem](../MTEngine/Npc/HomeIntrusionSystem.cs), [NpcCombatReactionSystem](../MTEngine/Npc/NpcCombatReactionSystem.cs) теперь работают только в `Active`.
> **Active/Background:** дневные жизненные системы — [AgingSystem](../MTEngine/Npc/AgingSystem.cs), [MatchmakingSystem](../MTEngine/Npc/MatchmakingSystem.cs), [RelationshipTickSystem](../MTEngine/Npc/RelationshipTickSystem.cs), [PregnancyPlanningSystem](../MTEngine/Npc/PregnancyPlanningSystem.cs), [BirthSystem](../MTEngine/Npc/BirthSystem.cs), [ChildGrowthSystem](../MTEngine/Npc/ChildGrowthSystem.cs), [JobMarketSystem](../MTEngine/Npc/JobMarketSystem.cs), [ProfessionTickSystem](../MTEngine/Npc/ProfessionTickSystem.cs), [ShopRestockSystem](../MTEngine/Npc/ShopRestockSystem.cs). Это подготавливает P6.4: когда NPC будут оживать из `WorldPopulationStore` вне активной карты, физика/боёвка/домовая охрана не будут их гонять, но социальные и экономические дневные события смогут тика́ть в background.

#### 🟢 DONE — P6.4 `WorldPopulationStore.Live()` / `Snapshot()`
> **Куда легло:** [MTEngine/Npc/WorldPopulationStore.cs](../MTEngine/Npc/WorldPopulationStore.cs) теперь умеет `Snapshot(entity, mapId)`, `SnapshotAndDespawn(entity, mapId)`, `Live(saveId|snapshot, prototypes, entityFactory)`.
> `Snapshot` гарантирует `SaveEntityIdComponent`, кладёт `PrototypeId`, имя, `MapId`, позицию и все `[SaveField]`-компоненты в `NpcSnapshot.Components`.
> `Live` создаёт entity из прототипа через [EntityFactory](../MTEngine/Core/EntityFactory.cs), возвращает тот же `SaveId`, применяет сохранённые поля поверх прототипа, фиксирует позицию из снапшота и обновляет визуальные `IPrototypeInitializable`-компоненты.
> Сериализация компонентов вынесена в [MTEngine/Npc/NpcSnapshotComponentSerializer.cs](../MTEngine/Npc/NpcSnapshotComponentSerializer.cs): формат совпадает по смыслу с map-save (`[SaveField]`, `RegisterComponent` type id), но живёт в движке и не тянет `SandboxGame.Save`.

#### 🟢 DONE — P6.5 Catch-up при пересечении границы зоны
> **Куда легло (первый слой):** [MTEngine/Npc/ScheduleSystem.cs](../MTEngine/Npc/ScheduleSystem.cs) получил `SettleCurrentMapNpcsFromBackground()`: когда карта материализуется из непрогруженного состояния, NPC сразу резолвят текущее расписание и ставятся в актуальную целевую точку (`work_anchor`, кровать, wander-точка и т.д.), а не появляются у стартовой кровати и начинают жить только после входа игрока. [SandboxGame/Game1.cs](../SandboxGame/Game1.cs) вызывает это после загрузки карты, если целевая карта отличается от предыдущей.
> **Куда легло (второй слой — `WorldCatchupSystem`):** [MTEngine/Npc/WorldCatchupSystem.cs](../MTEngine/Npc/WorldCatchupSystem.cs) подписывается на `MapLoadedEvent`, ведёт `LastSeenByMap` (save-объект `worldCatchup`) и при возврате игрока в карту, которой не было в Active дольше дня, прокатывает прибавку `def.SkillGainPerDay × daysElapsed` по `primarySkill` для каждой `ProfessionComponent` на этой карте — это симулирует ProfessionTickSystem-тик, пропущенный пока NPC были в Distant. После catch-up публикуется [`MapCatchUpRan`](../MTEngine/Npc/MapCatchUpEvent.cs); ShopRestockSystem и будущая экономика могут на это подписываться. Catch-up отложен на следующий `Update`-кадр, потому что `MapLoadedEvent` приходит раньше, чем `MapEntitySpawner`/`NpcRosterSpawner` спаунят entities (порядок подписок).
> **Также:** [MTEngine/ECS/World.cs](../MTEngine/ECS/World.cs) получил `FlushEntityChanges()`, а [SandboxGame/Save/SaveGameManager.cs](../SandboxGame/Save/SaveGameManager.cs) больше не вызывает полный `World.Update(0)` во время восстановления map-state. Это убирает re-entrant тик систем внутри `MapLoadedEvent`.
> **Что сознательно не делаем здесь:** автоматическая LOD-промоция/демоция NPC между Distant ↔ Background ↔ Active через `WorldPopulationStore.Live()/Snapshot()` (API готово в P6.4, но интеграция остаётся отдельным «жирным» подпунктом — открытый Q в §17.4); и distant-batch события из §10.2 (агрегированные свадьбы/смерти на distant-settlement) — пока не нужны, так как нет долговременной distant-симуляции.

### P7 — родственники и месть

#### 🟢 DONE — P7.1 Зеркальная синхронизация `KinComponent` при свадьбе/рождении/смерти
> **Куда легло:** [MTEngine/Npc/KinSyncSystem.cs](../MTEngine/Npc/KinSyncSystem.cs) — отдельная событийная система MTLiving, подписана на `RelationshipMarried`, `NpcBorn`, `EntityDied`.
> При свадьбе добавляет зеркальные `Spouse`-ссылки. При рождении гарантирует `Father/Mother` у ребёнка, `Child` у родителей и зеркальные `Sibling`-ссылки между новым ребёнком и уже известными детьми обоих родителей. При смерти супруга переводит живого партнёра в `RelationshipStatus.Widowed`, очищает `PartnerNpcSaveId`, сбрасывает pending date/wedding/birth и ставит `LastMatchSearchDayIndex = today`, чтобы новый поиск пары начался только после обычного cooldown.
> Система зарегистрирована в [MTEngine/Core/GameEngine.cs](../MTEngine/Core/GameEngine.cs). Текущая итерация работает по live NPC; off-map/snapshot-синхронизация останется частью будущей полной distant-симуляции.

#### 🟢 DONE — P7.2 `RevengeTrigger` + `RevengeSystem` (§8.3)
> **Куда легло:** [MTEngine/Npc/RevengeTriggerComponent.cs](../MTEngine/Npc/RevengeTriggerComponent.cs) — `RevengeTriggerComponent` (`List<RevengeTrigger>`) + `RevengeBehavior { MerchantPenalty, HostileOnSight, OpportunisticHunter, Avenger }`. Триггер хранит `VictimSaveId`, `KillerSaveId`, тип родства-основания, день создания, `TriggerAfterDayIndex`, выбранное поведение и `Ready`.
> [MTEngine/Npc/RevengeSystem.cs](../MTEngine/Npc/RevengeSystem.cs) слушает `EntityDied`: если killer — игрок, берёт `victim.KinComponent.Links`, находит живых родственников, пропускает пацифистов и `Vengefulness <= 0`, выбирает поведение по шкале §8.2 и ставит триггер через 5..20 дней. Несовершеннолетние получают дату не раньше 18 лет. На `DayChanged` due-триггеры становятся `Ready`.
> [MTEngine/Systems/HealthSystem.cs](../MTEngine/Systems/HealthSystem.cs) теперь запоминает последнего атакующего из `EntityDamagedEvent` и публикует `EntityDied(..., DeathCause.Combat, killer, dayIndex)`, если смерть наступила в течение окна боевой памяти. [SandboxGame/Systems/GodModeSystem.cs](../SandboxGame/Systems/GodModeSystem.cs) показывает активные revenge-триггеры в debug-карточке NPC.
> Реальное поведение `Avenger` при `Ready=true` — следующий пункт P7.3; малые шкалы `Vengefulness` применятся в P7.5.

#### 🟢 DONE — P7.3 `AvengerComponent` + специальное расписание преследования
> **Куда легло:** [MTEngine/Npc/AvengerComponent.cs](../MTEngine/Npc/AvengerComponent.cs) — состояние мстителя: цель (`TargetSaveId`, сейчас игрок), жертва, день старта, last-known позиция игрока, выдано ли оружие, применён ли speed boost, выбранный боевой skill.
> [MTEngine/Npc/RevengeSystem.cs](../MTEngine/Npc/RevengeSystem.cs) при due-триггере `RevengeBehavior.Avenger` снимает профессию и освобождает `ProfessionSlotDef`, ставит `AvengerComponent`, переключает `ScheduleComponent.TemplateId = "avenger"` с 24/7 `Visit $player`, чуть ускоряет NPC, прокачивает один из `melee_unarmed` / `melee_one_handed` / `melee_two_handed` и, если нужен weapon, пытается выдать `longsword` или `greatsword` в руки.
> [MTEngine/Npc/AvengerSystem.cs](../MTEngine/Npc/AvengerSystem.cs) работает в Active-зоне после обычного расписания: обновляет last-known позицию игрока и перезаписывает `NpcIntent` на текущую позицию игрока, так что [NpcMovementSystem](../MTEngine/Npc/NpcMovementSystem.cs) ведёт мстителя за игроком. [SandboxGame/Systems/GodModeSystem.cs](../SandboxGame/Systems/GodModeSystem.cs) показывает `Avenger`-состояние в debug-карточке.
> Фраза, запрет атаки спящего и собственно удар при сближении — следующий пункт P7.4.

#### 🟢 DONE — P7.4 Поведение при нахождении игрока
> **Куда легло:** [MTEngine/Npc/AvengerComponent.cs](../MTEngine/Npc/AvengerComponent.cs) хранит `AccusationSaid`, чтобы фраза не повторялась после save/load. [MTEngine/Npc/AvengerSystem.cs](../MTEngine/Npc/AvengerSystem.cs) при сближении с игроком берёт текущий `AttackProfile` из `CombatSystem`, один раз говорит `Ты убил моего близкого!` и атакует через общий combat-stack. Если игрок спит через `SleepSystem`, мститель останавливается рядом и не бьёт, сохраняя преследование на момент пробуждения.

#### 🟢 DONE — P7.5 Меньшие шкалы Vengefulness
> **Куда легло:** [MTEngine/Npc/RevengeSystem.cs](../MTEngine/Npc/RevengeSystem.cs) теперь каждый тик проверяет готовые `HostileOnSight` и `OpportunisticHunter` revenge-триггеры: если NPC активен, видит игрока и триггер направлен на игрока, он получает обычное `NpcAggressionComponent`-преследование без превращения в `Avenger`.
> [MTEngine/Npc/TradeSystem.cs](../MTEngine/Npc/TradeSystem.cs) применяет готовый `MerchantPenalty`: цены покупки у такого торговца для игрока ×1.5, а выплаты за продажу игроком делятся на тот же множитель. Работает как для NPC-магазина, так и для shop-entity с `OwnerNpcSaveId`.

#### 🟢 DONE — P7.6 Боевой рассудок и бегство (§8.6)
> **Куда легло:** [MTEngine/Npc/NpcFleeComponent.cs](../MTEngine/Npc/NpcFleeComponent.cs) хранит угрозу, последнюю позицию угрозы, причину бегства, escape-target и таймер безопасности.
> [MTEngine/Npc/CombatThreatSystem.cs](../MTEngine/Npc/CombatThreatSystem.cs) работает перед обычной боевой реакцией: для NPC в боевом контексте считает грубую силу (`HP`, оружие в руках, relevant combat skill, fortitude), роль (`civilian/guard/avenger/pacifist`) и принимает решение бежать по правилам §8.6. Также сканирует NPC враждебных фракций: слабый житель избегает сильного врага, пока тот жив и рядом/видим. Escape-точка выбирается дальше от угрозы, желательно вне line-of-sight, без чужих домов и без `location_transition`-тайлов; путь проверяется через `GridPathfinder`.
> [MTEngine/Npc/NpcCombatReactionSystem.cs](../MTEngine/Npc/NpcCombatReactionSystem.cs) и [MTEngine/Npc/AvengerSystem.cs](../MTEngine/Npc/AvengerSystem.cs) не атакуют и не перетирают интент NPC, пока у него есть `NpcFleeComponent`. Flee снимается только когда угроза мертва/пропала или отстала на безопасную дистанцию, а NPC ещё продержался в безопасности несколько секунд; затем расписание принудительно обновляется.

### P8 — измена, переезды между поселениями

#### 🟢 DONE — P8.1 `RelationshipWithPlayerComponent`
> [MTEngine/Npc/RelationshipWithPlayerComponent.cs](../MTEngine/Npc/RelationshipWithPlayerComponent.cs) — компонент `playerRel` с `Friendship`/`Romance` (0..100), bitmask `FactsRevealed` (см. §9.2) и `LastTalkAtSeconds` для кулдауна Talk. `[Flags] enum PlayerKnownFact` покрывает пять стартовых категорий из §9.2 (BestSkill, RandomKin, Profession, Residence, FamilyStatus). Хелперы `HasFact`/`RevealFact`.
> UI игнорирования партнёра NPC при `Infidelity == 10`.

#### 🟢 DONE — P8.1b Talk-интеракция «Поговорить» + Friendship-tick (§9.3)
> [MTEngine/Npc/IdentityComponent.cs](../MTEngine/Npc/IdentityComponent.cs) теперь реализует `IInteractionSource`. Интеракция «Поговорить» появляется только если actor — игрок, target — живой NPC, не в бою (`NpcAggressionComponent.Mode == None`), не во flee. Execute: добавляет `RelationshipWithPlayerComponent`, если его нет; проверяет кулдаун `4` игровых часа через `GameClock.TotalSecondsAbsolute`; на проход — `Friendship += 1` (cap 100), всплывающий текст «+1 дружба», `MarkDirty`. На кулдауне — «Мы уже поговорили...». Точка расширения под `IDialogTopic` пока не вводилась — добавим, когда появятся квестовые диалоги.

#### 🟢 DONE — P8.2 Уход партнёра-NPC из дома игрока (§5.6)
> Решающая часть кейса 3 из §5.6.
> [MTEngine/Npc/RelationshipsComponent.cs](../MTEngine/Npc/RelationshipsComponent.cs) — добавлены `LastSeenWithPlayerDayIndex` и `PendingRelocation` под `[SaveField]`.
> [MTEngine/Npc/RelationshipEvents.cs](../MTEngine/Npc/RelationshipEvents.cs) — новое событие `RelationshipSeparated(npcSaveId, otherSaveId, dayIndex, cause)`.
> [MTEngine/Npc/PlayerCohabitationSystem.cs](../MTEngine/Npc/PlayerCohabitationSystem.cs) — событийная система на `DayChanged`. Для женатых на игроке NPC: при пребывании в Active LOD обновляет `LastSeenWithPlayerDayIndex`; при `Personality.Infidelity > 0` считает порог `(11 - Infidelity) * 5` дней (мин. 3); по превышению с шансом 50% переключает `Status = Separated`, `PartnerIsPlayer = false`, `PartnerNpcSaveId = ""`, `PendingRelocation = true` и публикует `RelationshipSeparated` с причиной `"cohabitation_neglect"`. Зарегистрирована в [GameEngine](../MTEngine/Core/GameEngine.cs). Физический переезд + записка — задачи P8.3/P8.4.

#### ⚪ TODO — P8.3 Прототип `Note` + `NoteComponent`

#### ⚪ TODO — P8.4 Телепорт партнёра в другую settlement по правилам §5.6, с весами

#### ⚪ TODO — P8.5 `Matchmaking` после переезда

### P9 — отполировка, журналы, опционально

#### ⚪ TODO — P9.1 `JournalSystem` + UI журнала

#### ⚪ TODO — P9.2 Достижения / отслеживание «убил всех в городе»

#### ⚪ TODO — P9.3 Migration drift (если город пустеет)

#### ⚪ TODO — P9.4 Полировка smell-теста
> 50 NPC × 3 settlement × 100 игровых дней должны симулироваться без лагов.

#### ⚪ TODO — P9.5 Профайлинг
> Основные горячие точки `MatchmakingSystem`, `ScheduleSystem`, `NpcMovementSystem`.

---

## 19. Чеклист первого реального коммита

Если хочется завтра уже что-то закоммитить — минимальный полезный мердж:

1. `WorldClock` + `Calendar` + `DayChanged` + `AgeComponent` (P0.1–P0.4).
2. `EntityDied` эвент (P0.6).
3. `AreaZoneData` + загрузка/сохранение (P1.1–P1.2).

Это уже даёт календарь, возраст и площадки в карте, без NPC-логики. Дальше всё надстраивается над этим без переделок.
