# Система веществ — руководство разработчика

## Главный принцип

Полное описание вещества живёт отдельно от предметов.

То есть:
- в `SandboxGame/Content/Prototypes/Substances/.../proto.json` лежит сам прототип вещества
- в еде, напитках, таре, сырье, рецептах и книгах лежат только ссылки на вещество

Это обязательная модель данных текущей системы.

Правильно:

```json
{ "id": "taurine", "amount": 12 }
```

Неправильно:
- дублировать в предмете цвет
- дублировать запахи
- дублировать дозовые профили
- дублировать рецепты

---

## Где лежат прототипы веществ

```text
SandboxGame/Content/Prototypes/Substances/
├── Taurine/proto.json
├── Khryashchitin/proto.json
├── Babijonin/proto.json
└── ...
```

Загрузка происходит через:
- [Game1.cs](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Game1.cs)
- [GamePaths.cs](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Game/GamePaths.cs)
- [PrototypeManager.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/PrototypeManager.cs)

В `PrototypeManager` вещества идут как отдельная категория `substance`.

---

## Архитектура

Ключевые файлы:

```text
MTEngine/Metabolism/
├── SubstanceModels.cs
├── ConsumableComponent.cs
├── LiquidContainerComponent.cs
├── SubstanceSourceComponent.cs
├── MortarComponent.cs
├── KnowledgeBookComponent.cs
├── MetabolismComponent.cs
└── MetabolismSystem.cs
```

За что отвечает каждый узел:

`SubstanceModels.cs`
- прототип вещества
- ссылка на вещество
- доза вещества в рантайме
- эффекты
- дозовые профили
- рецепты

`ConsumableComponent.cs`
- берёт ссылки из предмета
- резолвит их в реальные дозы через реестр веществ
- добавляет их в организм

`LiquidContainerComponent.cs`
- хранит резолвнутые дозы
- смешивает вещества
- запускает рецепты
- переливает дозы

`SubstanceSourceComponent.cs`
- делает предмет сырьём для толкушки

`MortarComponent.cs`
- хранит извлечённые вещества
- передаёт их в тару

`KnowledgeBookComponent.cs`
- хранит ids веществ и ids рецептов из книги
- подтягивает описания вещества из реестра

`MetabolismSystem.cs`
- считает текущее количество вещества в теле
- суммирует дозы одного вещества
- применяет профили реакции

Непосредственные обработчики эффектов зарегистрированы внизу `SubstanceModels.cs` через `SubstanceEffectRegistry`.

---

## Формат прототипа вещества

Пример полного прототипа:

```json
{
  "id": "taurine",
  "name": "Taurine",
  "category": "substance",
  "color": "#F2D44AFF",
  "defaultAmount": 1,
  "volumePerUnit": 1,
  "absorptionTime": 5,
  "clearanceTime": 90,
  "smells": ["сладко", "резко"],
  "preparationHint": "Часто встречается в тонизирующих напитках и вытяжках.",
  "effects": [],
  "responseProfiles": [],
  "recipes": []
}
```

### Поля

| Поле | Тип | Значение |
|------|-----|----------|
| `id` | string | уникальный id вещества |
| `name` | string | отображаемое имя |
| `category` | string | всегда `substance` |
| `color` | string | цвет вещества |
| `defaultAmount` | float | дефолтное количество, если ссылка не задала `amount` |
| `volumePerUnit` | float | объём на единицу вещества |
| `absorptionTime` | float | скорость усвоения |
| `clearanceTime` | float | скорость выветривания |
| `smells` | string[] | дескрипторы запаха |
| `preparationHint` | string | описание способа получения |
| `effects` | array | прямые эффекты отдельной дозы |
| `responseProfiles` | array | реакции на суммарное количество вещества в теле |
| `recipes` | array | рецепты, которые это вещество знает |

---

## Формат ссылки на вещество

Во всех предметах, таре и рецептах используется короткая ссылка:

```json
{ "id": "taurine", "amount": 12 }
```

### Поля ссылки

| Поле | Тип | Значение |
|------|-----|----------|
| `id` | string | id отдельного прототипа вещества |
| `amount` | float | количество вещества |

Если `amount` не указан или `<= 0`, используется `defaultAmount`.

---

## Где можно ссылаться на вещества

### В еде и питье

```json
"consumable": {
  "substances": [
    { "id": "taurine", "amount": 12 }
  ]
}
```

### В таре

```json
"liquidContainer": {
  "contents": [
    { "id": "taurine", "amount": 30 }
  ]
}
```

### В сырье для толкушки

```json
"substanceSource": {
  "substances": [
    { "id": "fructose", "amount": 10 },
    { "id": "malic_acid", "amount": 4 }
  ]
}
```

### В рецептах

```json
"results": [
  { "id": "babijonin", "amount": 20 }
]
```

### В книгах

```json
"knowledgeBook": {
  "substances": [
    "taurine",
    "khryashchitin",
    "babijonin"
  ]
}
```

---

## Как добавить новое вещество

### Шаг 1. Создай отдельный файл

Например:

```text
SandboxGame/Content/Prototypes/Substances/MyNewSubstance/proto.json
```

Шаблон:

```json
{
  "id": "my_new_substance",
  "name": "My New Substance",
  "category": "substance",
  "color": "#FFFFFFFF",
  "defaultAmount": 1,
  "volumePerUnit": 1,
  "absorptionTime": 5,
  "clearanceTime": 60,
  "smells": [],
  "preparationHint": "",
  "effects": [],
  "responseProfiles": [],
  "recipes": []
}
```

### Шаг 2. Подключи вещество в предмет

Например:

```json
"substances": [
  { "id": "my_new_substance", "amount": 8 }
]
```

Всё. Больше в предмете про это вещество писать не нужно.

---

## Как добавить новый тип эффекта

Система устроена в два слоя:
- в `proto.json` ты только выбираешь `type`
- в коде должен существовать обработчик этого `type`

### Пошагово

1. Открой [SubstanceModels.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Metabolism/SubstanceModels.cs).
2. Найди `SubstanceEffectRegistry`.
3. Зарегистрируй новый обработчик через `Register(new YourEffectHandler())`.
4. Ниже создай класс, реализующий `ISubstanceEffectHandler`.
5. Верни строковое имя эффекта в `EffectType`.
6. В `Apply(SubstanceEffectContext context)` опиши, что именно должно происходить с сущностью.
7. После этого используй новый `type` в любом `substance`-прототипе.

### Пример: новый эффект замедления

В коде добавлен отдельный эффект:

```csharp
public sealed class SlowMultiplierSubstanceEffect : ISubstanceEffectHandler
{
    public string EffectType => "slowMultiplier";

    public void Apply(SubstanceEffectContext context)
    {
        if (!context.IsActive)
            return;

        var penalty = Math.Clamp(context.Effect.Magnitude, 0f, 1f);
        context.Metabolism.SubstanceSpeedModifier *= 1f - penalty;
    }
}
```

Что это значит:
- `magnitude: 0.15` = замедлить на 15%
- `magnitude: 0.40` = замедлить на 40%
- значение зажимается в диапазон `0..1`, чтобы случайно не получить отрицательную скорость

### Как использовать в прототипе вещества

```json
{
  "id": "malic_acid_slow",
  "type": "slowMultiplier",
  "magnitude": 0.15
}
```

Это уже добавлено как живой пример в:
- [proto.json](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Content/Prototypes/Substances/MalicAcid/proto.json)

### Когда делать отдельный тип, а когда не делать

Делай отдельный тип эффекта, если:
- в данных должно быть ясно, что это именно другой смысл, а не просто другое число
- у эффекта отдельные правила ограничения
- у эффекта потом могут появиться особые условия

Поэтому `slowMultiplier` лучше, чем писать отрицательное значение в `speedMultiplier`.

---

## Два режима эффектов

У вещества есть два слоя поведения.

### 1. `effects`

Это прямые эффекты отдельной дозы.

Используй, если нужно:
- что-то одноразовое после употребления
- персональный эффект конкретной дозы
- сообщение, которое относится к одной дозе

Пример:

```json
"effects": [
  {
    "id": "spring_popup",
    "type": "popup",
    "message": "Вода приятно освежает.",
    "color": "#7BC6F5FF"
  }
]
```

### 2. `responseProfiles`

Это реакции на суммарное количество вещества в организме.

Используй, если:
- вещество должно накапливаться
- маленькая доза и большая доза должны работать по-разному
- вещество может менять характер действия при росте дозы

Это основной режим для:
- ядов
- стимуляторов
- наркотиков
- мутагенов
- сывороток
- странных фантазийных веществ

---

## Как работает накопление

Каждая доза вещества в теле:
- постепенно усваивается
- потом постепенно выводится

У каждой активной дозы считается `CurrentAmount`.

Дальше:
- все дозы одного `substanceId` суммируются
- получается текущая нагрузка вещества в организме
- эта нагрузка сравнивается с `responseProfiles`

То есть система смотрит не на “сколько было в одном стакане”, а на:
- сколько вещества реально сейчас находится в теле

---

## Формат `responseProfiles`

Пример:

```json
"responseProfiles": [
  {
    "id": "boost",
    "name": "Разгон",
    "minAmount": 20,
    "maxAmount": 45,
    "effects": [
      { "id": "boost_speed", "type": "speedMultiplier", "magnitude": 0.2 },
      { "id": "boost_popup", "type": "popup", "message": "Таурин разгоняет кровь." }
    ]
  }
]
```

### Поля профиля

| Поле | Тип | Значение |
|------|-----|----------|
| `id` | string | стабильный id диапазона |
| `name` | string | читаемое имя диапазона |
| `minAmount` | float | нижняя граница включительно |
| `maxAmount` | float? | верхняя граница не включительно |
| `effects` | array | эффекты активного диапазона |

### Чтение диапазонов

```json
{ "minAmount": 0, "maxAmount": 10 }
```

это:
- `0 <= amount < 10`

```json
{ "minAmount": 10, "maxAmount": 25 }
```

это:
- `10 <= amount < 25`

```json
{ "minAmount": 25 }
```

это:
- `amount >= 25`

---

## Как проектировать дозовые профили

Система не навязывает стадии типа `safe/toxic/lethal`.

Ты можешь сделать что угодно:
- мало вещества лечит
- средне ускоряет
- много калечит
- очень много даёт бессмертие

Пример:

```json
"responseProfiles": [
  {
    "id": "small_bad",
    "minAmount": 3,
    "maxAmount": 10,
    "effects": [
      { "type": "popup", "message": "Тебя мутит..." },
      { "type": "speedMultiplier", "magnitude": -0.15 }
    ]
  },
  {
    "id": "medium_good",
    "minAmount": 10,
    "maxAmount": 25,
    "effects": [
      { "type": "speedMultiplier", "magnitude": 0.2 }
    ]
  },
  {
    "id": "large_divine",
    "minAmount": 40,
    "effects": [
      { "type": "popup", "message": "Тело перестаёт подчиняться законам природы." },
      { "type": "speedMultiplier", "magnitude": 1.5 }
    ]
  }
]
```

Да, такая логика полностью допустима.

---

## Поддерживаемые типы эффектов

### `speedMultiplier`

Пример:

```json
{ "type": "speedMultiplier", "magnitude": 0.2 }
```

Это даёт `+20%` скорости.

Отрицательное значение:

```json
{ "type": "speedMultiplier", "magnitude": -0.35 }
```

Это даёт `-35%` скорости.

### `needDeltaOverTime`

Пример:

```json
{
  "type": "needDeltaOverTime",
  "need": "thirst",
  "magnitude": -30,
  "duration": 15
}
```

Это постепенно уменьшит жажду примерно на `30`.

Поддерживаемые `need`:
- `hunger`
- `thirst`
- `bladder`
- `bowel`

### `popup`

Пример:

```json
{
  "type": "popup",
  "message": "Края мира становятся вязкими.",
  "color": "#D08155FF"
}
```

Работает хорошо для:
- входа в новую стадию профиля
- предупреждений
- flavour-сообщений

---

## Когда использовать `effects`, а когда `responseProfiles`

Используй `effects`, если:
- важна отдельная доза
- нужен локальный индивидуальный триггер
- это простое одноразовое действие

Используй `responseProfiles`, если:
- действие зависит от суммарной нагрузки вещества
- вещество накапливается
- нужны разные стадии действия

Практическое правило:
- почти все сложные вещества должны жить через `responseProfiles`

---

## Как редактировать силу действия вещества

### Изменить силу эффекта

```json
{ "type": "speedMultiplier", "magnitude": 0.2 }
```

Меняешь:
- `0.1` — слабее
- `0.35` — сильнее
- `-0.2` — дебафф

### Изменить растянутое изменение потребности

```json
{ "type": "needDeltaOverTime", "magnitude": -20, "duration": 10 }
```

Чем больше `duration`, тем дольше распределяется действие.

### Изменить точку включения стадии

Это делается профилем:

```json
{
  "minAmount": 20,
  "maxAmount": 45
}
```

Если стадия должна включаться позже:
- увеличь `minAmount`

Если стадия должна кончаться раньше:
- уменьши `maxAmount`

---

## Как работают рецепты

Рецепты описываются в самом прототипе вещества.

Пример:

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
      { "id": "babijonin", "amount": 20 }
    ]
  }
]
```

Важно:
- `ingredients` смотрят на ids веществ
- `results` тоже ссылаются на ids веществ
- при срабатывании результат создаётся через отдельный прототип вещества

---

## Как писать книги

Книга должна хранить ids веществ, а не копию их описаний.

Пример:

```json
"knowledgeBook": {
  "bookId": "substances_vol_1",
  "title": "Вещества. Том I",
  "category": "substances",
  "substances": [
    "taurine",
    "khryashchitin",
    "babijonin"
  ],
  "recipes": [
    {
      "id": "babijonin_recipe",
      "name": "Бабиджонин",
      "description": "Смешать таурин и хрящитин в одной таре."
    }
  ]
}
```

При чтении:
- книга подтянет имя, запах и способ получения из прототипа вещества

---

## Как добавить новый тип эффекта

Если существующих типов мало, добавляй новый обработчик.

Пример:

```csharp
public sealed class StressSubstanceEffect : ISubstanceEffectHandler
{
    public string EffectType => "stress";

    public void Apply(SubstanceEffectContext context)
    {
        if (!context.IsActive)
            return;

        // Любая логика здесь
    }
}
```

Потом зарегистрируй его в `SubstanceEffectRegistry`.

После этого в JSON можно писать:

```json
{ "type": "stress", "magnitude": 15 }
```

---

## Рекомендуемый порядок работы

1. Создай отдельный прототип вещества в `Content/Prototypes/Substances`.
2. Опиши там цвет, запах, профили дозы, прямые эффекты и рецепты.
3. Сошлись на вещество из предметов через `{ "id": "...", "amount": ... }`.
4. Если нужно новое поведение, добавь новый `ISubstanceEffectHandler`.

Это и есть правильный путь расширения системы.
