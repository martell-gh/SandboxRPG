# Система метаболизма — руководство разработчика

Дополнительные документы по новой подсистеме веществ:
- `SUBSTANCES_PLAYER.md` — как этим пользоваться в игре
- `SUBSTANCES_DEV.md` — как расширять систему в коде и прототипах

Важное архитектурное правило:
- полные свойства вещества лежат в отдельных прототипах в `SandboxGame/Content/Prototypes/Substances`
- в еде, напитках, таре и сырье хранится только ссылка на вещество и количество

## Обзор

4 потребности: **голод**, **жажда**, **мочевой пузырь**, **кишечник**.

- Голод/жажда: 100 → 0 со временем. Восстанавливаются едой/питьём.
- Пузырь/кишечник: 0 → 100 со временем + от еды/питья. Опустошаются в туалете.
- Еда переваривается постепенно (не мгновенно).
- Нужду можно справить в туалете (приемлемо) или на месте (неприемлемо).
- Конфуз при 100% пузыря/кишечника — тоже неприемлемо.
- Все типы справления нужды публикуют `ReliefEvent` в `EventBus` — готово для реакций NPC.

---

## Файловая структура

```
MTEngine/Metabolism/
├── MetabolismComponent.cs   — компонент: данные + IInteractionSource (туалет, нужда на месте)
├── ConsumableComponent.cs   — компонент: делает предмет съедобным/питьевым
├── MetabolismSystem.cs      — система: распад, пищеварение, штрафы, предупреждения, конфузы
└── ReliefEvent.cs           — событие: кто, что, приемлемо/неприемлемо

SandboxGame/Content/UI/
└── MetabolismWindow.xml     — XML-окно (полоски потребностей)

SandboxGame/Systems/
└── MetabolismUI.cs          — привязка окна к данным (открытие по M)

Новые файлы:

```
MTEngine/Metabolism/
├── SubstanceModels.cs         — модели веществ, эффектов, рецептов и обработчики свойств
├── LiquidContainerComponent.cs — тара для смесей/жидкостей, смешивание, переливание, запах
└── SubstanceDebugSystem.cs     — быстрый вывод состава по P
```
```

---

## Как всё зарегистрировано

```
GameEngine.cs:
  World.AddSystem(MetabolismSystem)   — тикает логику
  World.AddSystem(SubstanceDebugSystem) — быстрые клавиши веществ
  World.AddSystem(UIManager)          — рисует XML-окна

Game1.cs:
  World.AddSystem(new MetabolismUI()) — привязка окна к данным
  UIManager.SetFont(font)             — шрифт для отрисовки
```

Компоненты `metabolism` и `consumable` регистрируются автоматически через `[RegisterComponent]` + рефлексию. Ничего руками добавлять не нужно — просто напиши в proto.json.

То же самое теперь касается:
- `liquidContainer`

---

## Новая подсистема: вещества

Теперь в `consumable` и `liquidContainer` можно прописывать список `substances`.

Каждое вещество — это не просто число, а отдельная структура:
- `id`, `name`
- `color`
- `amount`, `volume`
- `absorptionTime` — как быстро усвоится
- `clearanceTime` — как долго держится в организме
- `smells` — словарь запахов/дескрипторов
- `effects` — свойства, которые реализованы в коде
- `recipes` — рецепты смешивания

### Поддерживаемые свойства вещества прямо сейчас

Сейчас в коде есть обработчики:

| `type`             | Что делает |
|--------------------|------------|
| `speedMultiplier`  | Временно умножает скорость движения |
| `needDeltaOverTime`| Постепенно меняет `hunger/thirst/bladder/bowel` |
| `popup`            | Показывает текст при срабатывании |

Это уже позволяет сделать пример из задачи:
- таурин даёт `speedMultiplier` на 60 сек
- потом через `delay` включается негативный эффект
- негативным эффектом может быть ещё один `speedMultiplier` с минусом или `needDeltaOverTime`

### Как устроено время

Для эффекта:

```json
{
  "type": "speedMultiplier",
  "magnitude": 0.2,
  "duration": 60,
  "delay": 0
}
```

- `magnitude: 0.2` = `+20%`
- `magnitude: -0.15` = `-15%`
- `delay` — сколько ждать после усвоения перед стартом эффекта
- `duration` — сколько эффект живёт

---

## Вещества в еде и питье

У `consumable` появился новый массив:

```json
"substances": [
  {
    "id": "taurine",
    "name": "Taurine",
    "color": "#F2D44AFF",
    "amount": 15,
    "volume": 15,
    "absorptionTime": 5,
    "clearanceTime": 90,
    "smells": ["сладко", "резко"],
    "effects": [
      {
        "type": "speedMultiplier",
        "magnitude": 0.2,
        "duration": 60
      }
    ]
  }
]
```

При поедании/выпивании:
- обычная еда всё ещё идёт в `DigestingItems`
- вещества параллельно добавляются в `ActiveSubstances`
- их эффекты тикают автоматически в `MetabolismSystem`

---

## Тара и смеси

Новый компонент: `liquidContainer`.

Что умеет уже сейчас:
- хранить смесь веществ
- иметь ёмкость (`capacity`)
- переливать содержимое из одной тары в другую
- вычислять смешанный цвет
- собирать смешанный запах
- выпиваться как единое содержимое
- автоматически варить рецепты, если в таре сошлись нужные вещества

### Пример тары

```json
"liquidContainer": {
  "name": "Glass Cup",
  "capacity": 100,
  "transparent": true,
  "showContents": true,
  "contents": [
    {
      "id": "taurine",
      "name": "Taurine",
      "color": "#F2D44AFF",
      "amount": 30,
      "volume": 30,
      "absorptionTime": 5,
      "clearanceTime": 90,
      "smells": ["сладко", "резко"],
      "effects": [
        {
          "type": "speedMultiplier",
          "magnitude": 0.2,
          "duration": 60
        }
      ]
    }
  ]
}
```

### Что уже есть в управлении

- `E` на таре в руке:
  - выпить содержимое
  - понюхать содержимое
- ПКМ по другой таре, пока в активной руке держишь заполненную:
  - перелить смесь в цель
- `P`, пока держишь тару:
  - вывести точный состав в консоль

### Рецепты смешивания

Рецепты сейчас можно прописывать прямо внутри вещества:

```json
"recipes": [
  {
    "id": "babijonin_recipe",
    "name": "Бабиджонин",
    "description": "Таурин + хрящитин дают бабиджонин.",
    "ingredients": [
      { "substanceId": "taurine", "amount": 10 },
      { "substanceId": "khryashchitin", "amount": 10 }
    ],
    "results": [
      {
        "id": "babijonin",
        "name": "Бабиджонин",
        "color": "#D08155FF",
        "amount": 20,
        "volume": 20
      }
    ]
  }
]
```

Когда в таре оказываются нужные ингредиенты, рецепт срабатывает автоматически:
- ингредиенты списываются
- результат добавляется в смесь

---

## Что уже заложено на будущее

Под следующие шаги фундамент уже есть:
- прозрачная тара с визуальным заполнением по уровням 25/50/75/100
- толкушка и извлечение веществ из предметов
- частичное переливание по выбранным веществам
- более богатые типы эффектов

Сейчас это не всё доведено до полного UX-интерфейса, но архитектура уже рассчитана на расширение данными и новыми кодовыми обработчиками.

---

## Жизненный цикл потребностей

### 1. Распад (MetabolismSystem.UpdateDecay, каждый кадр)

```
Hunger  -= HungerDecay  * deltaTime   (дефолт 0.8/сек)
Thirst  -= ThirstDecay  * deltaTime   (дефолт 1.2/сек)
Bladder += BladderFillRate * deltaTime (дефолт 0.15/сек)
Bowel   += BowelFillRate   * deltaTime (дефолт 0.08/сек)
```

Все значения clamp'ятся в 0–100. Скорости настраиваются в proto.json.

### 2. Пищеварение (MetabolismSystem.UpdateDigestion)

Еда не усваивается мгновенно. При вызове `ConsumableComponent.Consume()`:

1. Создаётся `DigestingItem` с `RemainingNutrition`, `RemainingHydration`, `BladderLoad`, `BowelLoad`, `Duration`.
2. Каждый кадр система отщипывает пропорциональную долю: `chunk = remaining * (dt / Duration) / (1 - progress)`.
3. Chunk прибавляется к Hunger/Thirst и Bladder/Bowel.
4. Когда `Elapsed >= Duration` — элемент удаляется из очереди.

Пример: яблоко (`nutrition: 25, digestTime: 25`) → ~1 hunger/сек в течение 25 секунд.

### 3. Штрафы скорости (MetabolismSystem.UpdateEffects)

| Потребность | Excellent (≥80) | Normal (30–80) | Warning (10–30) | Critical (<10) |
|-------------|-----------------|----------------|------------------|-----------------|
| Голод       | +5%*            | —              | ×0.90            | ×0.75           |
| Жажда       | +5%*            | —              | ×0.88            | ×0.65           |

\* Бонус ×1.05 только если ОБА в Excellent одновременно.

| Потребность | Fine (0–60) | Warning (60–85) | Critical (85–100) | =100       |
|-------------|-------------|-----------------|---------------------|------------|
| Пузырь      | —           | —               | ×0.80               | Конфуз     |
| Кишечник    | —           | —               | ×0.85               | Конфуз     |

Множители перемножаются. Итоговый `SpeedModifier` применяется к `VelocityComponent.Speed`.  
Базовая скорость запоминается через `MetabolismBaseSpeedTag` (создаётся автоматически).

### 4. Предупреждения

Попапы каждые 8 секунд (const `WarningInterval`):

| Приоритет | Условие                     | Текст                      | Цвет       |
|-----------|-----------------------------|-----------------------------|------------|
| 1         | HungerStatus == Critical    | "Голодаю!"                  | OrangeRed  |
| 2         | ThirstStatus == Critical    | "Обезвоживание!"            | OrangeRed  |
| 3         | BladderStatus == Critical   | "Срочно нужен туалет!"      | Yellow     |
| 4         | BowelStatus == Critical     | "Срочно нужен туалет!"      | Yellow     |
| 5         | HungerStatus == Warning     | "Хочу есть..."              | Khaki      |
| 6         | ThirstStatus == Warning     | "Хочу пить..."              | Khaki      |

Показывается только одно самое приоритетное.

### 5. Конфуз (accident)

Если `Bladder >= 100` или `Bowel >= 100`:
- Значение сбрасывается до 20.
- Попап "Не удержал..." (фиолетовый).
- Публикуется `ReliefEvent` с `Type = Unacceptable`.
- Флаг `HadAccident = true` предотвращает повторный конфуз.
- Флаг сбрасывается когда оба значения < 60.

---

## Система справления нужды (ReliefType)

### Два типа

| Тип            | Когда                          | Последствия                        |
|----------------|--------------------------------|------------------------------------|
| **Acceptable** | ПКМ на объект с тегом `toilet` | Нормально, без последствий         |
| **Unacceptable** | ПКМ на себя (пустое место) ИЛИ конфуз | Публикуется событие для NPC реакций |

### Как это работает в коде

`MetabolismComponent` реализует `IInteractionSource`. В `GetInteractions`:

- Если `ctx.Target` имеет тег `toilet` → показывает "Сходить (малая/большая нужда)", `ReliefType.Acceptable`
- Если `ctx.Target == ctx.Actor` (самоинтеракция) → показывает "Справить нужду на месте (малая/большая)", `ReliefType.Unacceptable`

Самоинтеракция: ПКМ в пустое место (нет сущностей рядом) → открывается меню на себя.

### ReliefEvent

```csharp
// Подписка (например в будущей NPC-системе):
EventBus.Subscribe<ReliefEvent>(e =>
{
    if (e.Type == ReliefType.Unacceptable)
    {
        // NPC рядом видят это и реагируют
        Console.WriteLine($"{e.Actor.Name} справил {e.Need} неприемлемо!");
    }
});
```

Поля:
- `Actor` — кто это сделал (Entity)
- `Need` — `ReliefNeed.Bladder` или `ReliefNeed.Bowel`
- `Type` — `ReliefType.Acceptable` или `ReliefType.Unacceptable`

---

## Как есть и пить

1. Предмет должен иметь компонент `consumable` + `item` в proto.json.
2. Игрок подбирает предмет (ПКМ → "Взять" или E).
3. С предметом в руке нажимает **E** → открывается меню → "Съесть (Apple)" / "Выпить (Water Bottle)".
4. `ConsumableComponent.Consume()`:
   - Создаёт `DigestingItem` в очереди.
   - Убирает предмет из руки.
   - Уничтожает предмет (или -1 к стаку если stackable).
   - Попап "Съесть: Apple" (зелёный).

---

## Как ходить в туалет

**Вариант A — Туалет (приемлемо):**
1. Подойти к объекту с тегом `toilet`.
2. ПКМ → "Сходить (малая нужда)" / "Сходить (большая нужда)".
3. Пузырь/кишечник −80. Попап "Облегчение..." (голубой).
4. `ReliefEvent` с `Type = Acceptable`.

**Вариант B — На месте (неприемлемо):**
1. ПКМ в пустое место (нет сущностей рядом) → открывается меню на себя.
2. "Справить нужду на месте (малая)" / "Справить нужду на месте (большая)".
3. Пузырь/кишечник −80. Попап "Справил нужду на месте!" (фиолетовый).
4. `ReliefEvent` с `Type = Unacceptable`.

**Вариант C — Конфуз (автоматически):**
1. Пузырь или кишечник достигают 100.
2. Сброс до 20. Попап "Не удержал..." (фиолетовый).
3. `ReliefEvent` с `Type = Unacceptable`.

---

## Окно метаболизма (M)

XML: `SandboxGame/Content/UI/MetabolismWindow.xml`  
Логика: `SandboxGame/Systems/MetabolismUI.cs`

Показывает:
- 4 полоски (голод, жажда, пузырь, кишечник) с динамическим цветом.
- Статус (Нормальное / Плохое / Критическое).
- Список переваривающейся еды.
- Модификатор скорости (если != 100%).

Обновляется каждый кадр через `window.OnUpdate`.

---

## Как добавить новую еду / питьё

Создай прототип в подходящей категории внутри `Content/Prototypes/.../ИмяПредмета/proto.json` + `sprite.png`.

### Шаблон еды:

```json
{
  "id": "meat_steak",
  "name": "Meat Steak",
  "category": "entity",
  "components": {
    "sprite": { "source": "sprite.png", "srcX": 0, "srcY": 0, "width": 32, "height": 32, "layerDepth": 0.5 },
    "transform": { "x": 0, "y": 0 },
    "item": {
      "name": "Meat Steak",
      "slots": 1,
      "size": "Small",
      "tags": ["food"]
    },
    "consumable": {
      "nutrition": 55,
      "hydration": 8,
      "bladderLoad": 4,
      "bowelLoad": 30,
      "digestTime": 60,
      "type": "Food"
    }
  }
}
```

### Шаблон питья:

```json
{
  "id": "juice",
  "name": "Juice",
  "category": "entity",
  "components": {
    "sprite": { "source": "sprite.png", "srcX": 0, "srcY": 0, "width": 32, "height": 32, "layerDepth": 0.5 },
    "transform": { "x": 0, "y": 0 },
    "item": {
      "name": "Juice",
      "slots": 1,
      "size": "Small",
      "tags": ["drink"]
    },
    "consumable": {
      "nutrition": 5,
      "hydration": 50,
      "bladderLoad": 30,
      "bowelLoad": 3,
      "digestTime": 8,
      "type": "Drink",
      "eatVerb": "Выпить"
    }
  }
}
```

### Шаблон еды+питья:

```json
"consumable": {
  "nutrition": 15,
  "hydration": 25,
  "bladderLoad": 15,
  "bowelLoad": 10,
  "digestTime": 20,
  "type": "Both",
  "eatVerb": "Употребить"
}
```

### Все поля consumable:

| Поле          | Тип    | Описание                                  | По умолчанию |
|---------------|--------|-------------------------------------------|-------------|
| `nutrition`   | float  | Восстановление голода (0–100)             | 25          |
| `hydration`   | float  | Восстановление жажды (0–100)              | 0           |
| `bladderLoad` | float  | Наполнение пузыря (0–100)                 | 5           |
| `bowelLoad`   | float  | Наполнение кишечника (0–100)              | 15          |
| `digestTime`  | float  | Время переваривания (секунды)             | 30          |
| `type`        | enum   | `Food`, `Drink`, `Both`                   | Food        |
| `eatVerb`     | string | Глагол в меню (null = автоопределение)    | null        |

Автоглаголы: Food → "Съесть", Drink → "Выпить", Both → "Употребить".

---

## Как добавить место для туалета

Любая сущность с тегом `toilet` в `item.tags` становится туалетом:

```json
"item": {
  "name": "Outhouse",
  "size": "Huge",
  "tags": ["toilet", "furniture"]
}
```

Не обязательно именно `item` — тег `toilet` проверяется через `ItemComponent.HasTag()`.

---

## Настройка метаболизма сущности

Добавь в proto.json сущности:

```json
"metabolism": {
  "hunger": 85,
  "thirst": 90,
  "hungerDecay": 0.8,
  "thirstDecay": 1.2,
  "bladderFill": 0.15,
  "bowelFill": 0.08,
  "wellFedThreshold": 80,
  "hungryThreshold": 30,
  "starvingThreshold": 10,
  "needToGoThreshold": 60,
  "urgentThreshold": 85
}
```

Все поля опциональны. Можно дать разным NPC разные скорости метаболизма.

### Все поля metabolism:

| Поле                | Тип   | Описание                                | По умолчанию |
|---------------------|-------|-----------------------------------------|-------------|
| `hunger`            | float | Начальный голод (100 = сыт)             | 100         |
| `thirst`            | float | Начальная жажда (100 = напоён)          | 100         |
| `bladder`           | float | Начальный пузырь (0 = пуст)            | 0           |
| `bowel`             | float | Начальный кишечник (0 = пуст)          | 0           |
| `hungerDecay`       | float | Скорость потери голода/сек              | 0.8         |
| `thirstDecay`       | float | Скорость потери жажды/сек              | 1.2         |
| `bladderFill`       | float | Пассивное наполнение пузыря/сек        | 0.15        |
| `bowelFill`         | float | Пассивное наполнение кишечника/сек     | 0.08        |
| `wellFedThreshold`  | float | Порог для статуса Excellent             | 80          |
| `hungryThreshold`   | float | Порог для статуса Warning               | 30          |
| `starvingThreshold` | float | Порог для статуса Critical              | 10          |
| `needToGoThreshold` | float | Порог "нужно в туалет"                  | 60          |
| `urgentThreshold`   | float | Порог "срочно нужно"                    | 85          |

---

## Баланс

Текущие предметы:

| Предмет      | nutrition | hydration | bladderLoad | bowelLoad | digestTime |
|--------------|-----------|-----------|-------------|-----------|------------|
| Apple        | 25        | 10        | 5           | 15        | 25с        |
| Bread        | 40        | 5         | 3           | 25        | 40с        |
| Water Bottle | 0         | 40        | 25          | 2         | 10с        |

Цикл при дефолтных скоростях:
- Голод: 100→0 за ~125 сек. Нужно ~2.5 хлеба или ~4 яблока.
- Жажда: 100→0 за ~83 сек. Нужно ~2.5 бутылки воды.
- Пузырь: 0→100 за ~667 сек пассивно, быстрее с питьём.
- Кишечник: 0→100 за ~1250 сек пассивно, быстрее с едой.
- 3 яблока → +45 кишечник → ещё 2-3 и пора в туалет.
- 2 воды → +50 пузырь → ещё 1-2 и пора в туалет.

---

## Как подписаться на события метаболизма

```csharp
// В любой системе:
var bus = ServiceLocator.Get<EventBus>();

// Реакция NPC на неприемлемое поведение
bus.Subscribe<ReliefEvent>(e =>
{
    if (e.Type == ReliefType.Unacceptable)
    {
        // Найти NPC в радиусе
        // Изменить их отношение к Actor
        // Показать реплику "Фу!" и т.д.
    }
});
```

---

## Как менять метаболизм из кода

```csharp
// Получить компонент
var metab = entity.GetComponent<MetabolismComponent>();

// Прямое изменение
metab.Hunger = 100f;  // насытить
metab.Thirst = 0f;    // обезводить
metab.Bladder = 90f;  // срочно в туалет

// Программное справление нужды
MetabolismComponent.DoRelief(entity, ReliefNeed.Bladder, ReliefType.Acceptable);

// Программное потребление (без предмета)
metab.DigestingItems.Add(new DigestingItem
{
    Name = "Magical Food",
    RemainingNutrition = 50f,
    RemainingHydration = 50f,
    BladderLoad = 0f,
    BowelLoad = 0f,
    Duration = 5f,
    Elapsed = 0f
});

// Проверки
if (metab.HungerStatus == NeedStatus.Critical) { ... }
if (metab.BladderStatus == NeedStatus.Critical) { ... }
float speed = metab.SpeedModifier;  // текущий множитель скорости

// Выключить метаболизм
metab.Enabled = false;
```

---

## Как добавить свой UI для метаболизма

Текущий UI в `MetabolismUI.cs` — пример. Можно:

1. Изменить XML (`MetabolismWindow.xml`) — добавить элементы, поменять layout.
2. Получить окно программно: `ServiceLocator.Get<UIManager>().GetWindow("metabolism")`.
3. Подписаться на события окна: `window.OnOpened += ...`, `window.OnClosed += ...`.
4. Добавить элементы динамически: `window.AddElement(new UILabel { Name = "x", Text = "..." })`.
5. Получить элементы по имени: `window.Get<UIProgressBar>("hungerBar")`.
