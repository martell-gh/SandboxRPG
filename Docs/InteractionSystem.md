# Система взаимодействий

Этот документ описывает текущую систему взаимодействий целиком: как действия собираются, как показываются в меню, как исполняются мгновенно или через `do-after`, и какие правила нужно учитывать при добавлении новых интеракций.

---

## Общая схема

Система построена вокруг трёх сущностей:

- `IInteractionSource` — интерфейс компонента, который умеет отдавать действия.
- `InteractionContext` — контекст конкретного взаимодействия: кто действует, на кого действует, в каком мире.
- `InteractionEntry` — одно действие в контекстном меню.

Основной рантайм находится в [InteractionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/InteractionSystem.cs).

Поток работы такой:

1. Игрок наводится на сущность.
2. `InteractionSystem` определяет hovered target.
3. Для цели собираются все `InteractionEntry` от её компонентов.
4. Если цель не равна актёру, дополнительно собираются действия от компонентов самого актёра.
5. Действия сортируются по `Priority`.
6. При выборе действия оно либо исполняется сразу, либо запускается как delayed action.

---

## Основные типы

### `IInteractionSource`

Файл: [IInteractionSource.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Interactions/IInteractionSource.cs)

Компонент должен реализовать:

```csharp
IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
```

Метод ничего не исполняет сам по себе. Он только описывает, какие действия доступны в текущем контексте.

Обычно внутри `GetInteractions` компонент:

- проверяет, что взаимодействие вообще валидно;
- формирует `InteractionEntry`;
- возвращает `yield return`.

### `InteractionContext`

Файл: [InteractionContext.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Interactions/InteractionContext.cs)

Содержит:

- `Actor` — кто совершает действие;
- `Target` — цель действия;
- `World` — мир.

Это один и тот же контекст и для построения меню, и для финального `Execute`.

### `InteractionEntry`

Файл: [InteractionEntry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Interactions/InteractionEntry.cs)

Поддерживаемые поля:

- `Id` — уникальный id действия.
- `Label` — текст в контекстном меню.
- `Execute` — код, который выполняется при завершении действия.
- `Priority` — сортировка в меню. Чем больше, тем выше.
- `Delay` — опциональный `do-after`.
- `InterruptsCurrentAction` — прерывает ли текущее delayed action.

---

## Как собираются действия

В [InteractionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/InteractionSystem.cs) метод `CollectActions` работает так:

1. Берёт все компоненты `ctx.Target`.
2. Для каждого компонента, который реализует `IInteractionSource`, вызывает `GetInteractions(ctx)`.
3. Если `ctx.Actor != ctx.Target`, делает то же самое для компонентов `ctx.Actor`.
4. Убирает дубликаты по `Id`.
5. Сортирует по `Priority` по убыванию.

Это значит:

- действия могут добавляться как целью, так и актёром;
- актёрские действия нужно обязательно фильтровать по `ctx.Target`, если они должны работать только на себе или только на конкретных целях.

Пример: в `MetabolismComponent` есть явная проверка `ctx.Target == Owner`, иначе действие не выдаётся.

---

## Наведение и открытие меню

`InteractionSystem` ищет hovered entity по экранной позиции мыши:

- у сущности должен быть хотя бы один `IInteractionSource` или `InteractableComponent`;
- цель должна быть в радиусе взаимодействия;
- курсор должен попадать в визуальные bounds сущности.

Для персонажа с одеждой bounds считаются по общему силуэту тела и экипировки.

Если курсор находится на самом игроке, доступно self-interaction меню.

---

## Мгновенные действия

Если у `InteractionEntry.Delay == null`, действие выполняется сразу:

```csharp
yield return new InteractionEntry
{
    Id = "lamp.turnOn",
    Label = "Включить лампу",
    Priority = 24,
    Execute = c => Toggle(c.Actor)
};
```

Такие действия подходят для:

- открытия UI;
- простого осмотра;
- мгновенного переключения;
- действий без анимации ожидания.

---

## Delayed Actions (`do-after`)

### Что это такое

Если у действия задан `Delay`, оно не выполняется мгновенно. Вместо этого запускается ожидающее действие.

Пока оно идёт:

- над актёром рисуется маленький прогресс-бар;
- по завершении вызывается `Execute`;
- действие может быть прервано.

### Как задать

```csharp
yield return new InteractionEntry
{
    Id = "healing.use.Slash",
    Label = "Перевязать (порезы)",
    Priority = 20,
    Delay = InteractionDelay.Seconds(1.8f, "Перевязать"),
    Execute = c => UseHealing(c.Actor, c.Target)
};
```

### Поля `InteractionDelay`

- `Duration` — длительность в секундах.
- `ProgressLabel` — подпись над прогресс-баром.
- `CancelOnMove` — прерывать ли действие при движении.
- `CancelOnOtherAction` — прерывать ли действие другим действием.

Хелпер:

```csharp
InteractionDelay.Seconds(1.5f, "Действие")
```

---

## Прерывание delayed actions

Сейчас система поддерживает один активный delayed action.

### Что прерывает

Если текущее действие помечено как `CancelOnOtherAction = true`, его прерывают:

- смена активной руки;
- выбрасывание предмета;
- use в руке;
- drag/drop предметов;
- операции с экипировкой;
- операции со storage;
- запуск другого interaction action, которое помечено как `InterruptsCurrentAction = true`.

Если `CancelOnMove = true`, действие прерывается при смещении актёра.

### Что не прерывает

Лёгкие действия можно пометить так:

```csharp
InterruptsCurrentAction = false
```

Сейчас так сделано для `Инфо`, чтобы можно было открыть описание предмета, не сбивая текущее действие.

---

## Правила для разработчика

### 1. Всегда фильтруй контекст

Если действие должно быть только на себе:

```csharp
if (ctx.Target != Owner)
    yield break;
```

Если действие только на предмете в руке:

```csharp
var item = Owner?.GetComponent<ItemComponent>();
if (item?.ContainedIn != ctx.Actor)
    yield break;
```

### 2. `GetInteractions` не должен иметь побочек

Не меняй состояние мира в `GetInteractions`. Там только проверки и описание действий.

Вся логика должна жить в `Execute`.

### 3. Выбирай `Id` стабильно

`Id` используется для дедупликации. Если два компонента выдают одинаковый `Id`, в меню останется только один action.

Хороший формат:

- `lamp.turnOn`
- `storage.open`
- `healing.use.Slash`
- `mortar.load.<entityId>`

### 4. Не все действия должны быть delayed

Используй delay только там, где это действительно добавляет смысл:

- лечение;
- длительное применение предмета;
- сложные манипуляции;
- действия, которые должны прерываться движением.

Не стоит ставить delay на:

- `Инфо`;
- открытие окон;
- простые toggle-действия;
- обычный осмотр.

### 5. Если действие не должно сбивать текущий do-after, явно укажи это

```csharp
InterruptsCurrentAction = false
```

Иначе по умолчанию действие считается прерывающим.

---

## Текущее поведение в проекте

На данный момент:

- `Инфо` не прерывает текущее delayed action.
- Лечение через `HealingComponent` идёт с задержкой `1.8` секунды.
- Progress bar рисуется над актёром, который совершает действие.

---

## Где что лежит

- Интерфейс источника действий: [IInteractionSource.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Interactions/IInteractionSource.cs)
- Описание действия: [InteractionEntry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Interactions/InteractionEntry.cs)
- Контекст действия: [InteractionContext.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Interactions/InteractionContext.cs)
- Основная рантайм-система: [InteractionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/InteractionSystem.cs)
- Пример не прерывающего действия: [InfoComponent.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Components/InfoComponent.cs)
- Пример delayed action: [HealingComponent.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Wounds/HealingComponent.cs)

---

## Минимальный шаблон нового действия

### Мгновенное

```csharp
yield return new InteractionEntry
{
    Id = "example.instant",
    Label = "Сделать сразу",
    Priority = 10,
    Execute = c => DoSomething(c.Actor, c.Target)
};
```

### С задержкой

```csharp
yield return new InteractionEntry
{
    Id = "example.delayed",
    Label = "Делать медленно",
    Priority = 10,
    Delay = InteractionDelay.Seconds(2.5f, "Выполняется"),
    Execute = c => DoSomething(c.Actor, c.Target)
};
```

### Не прерывающее текущее действие

```csharp
yield return new InteractionEntry
{
    Id = "example.info",
    Label = "Посмотреть",
    Priority = -5,
    InterruptsCurrentAction = false,
    Execute = c => OpenInfo(c.Target)
};
```
