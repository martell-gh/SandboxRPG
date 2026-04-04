# Освещение и тени — подробная документация

Этот файл описывает текущую систему:

- освещения;
- затемнения сцены;
- непроглядных тайлов (`opaque`);
- теней обзора игрока;
- теней от источников света;
- порядка рендера в движке.

Документ ориентирован в первую очередь на разработчика.

---

## 1. Что вообще есть в системе

Сейчас в проекте есть две связанные, но разные подсистемы:

1. `LightingSystem`
   Отвечает за световую карту (`lightmap`), источники света и тени, которые режут сам свет.

2. `VisibilityOcclusionSystem`
   Отвечает за обзор игрока: что скрывается за непроглядными тайлами и как рисуются чёрные тени поля зрения.

Обе системы используют одну и ту же геометрию теней из:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs)

Это важно: свет и обзор игрока теперь строятся не двумя случайными разными способами, а через один общий механизм shadow-полигонов.

---

## 2. Порядок рендера в движке

Ключевая точка входа:

- [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs)

### Как рисуется кадр

В `GameEngine.Draw()` кадр идёт в таком порядке:

1. Мир рисуется в `_sceneRT`
   Файл: [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs#L112)

2. Пока сцена ещё не на backbuffer, строится `lightmap`
   Файл: [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs#L121)

3. `_sceneRT` выводится на экран
   Файл: [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs#L126)

4. `lightmap` накладывается поверх сцены через multiply blend
   Файл: [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs#L135)

5. Потом сверху рисуется overlay/UI
   Файл: [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs#L138)

### Какие scene-системы участвуют

Системы сцены добавляются в таком порядке:

1. `TileMapRenderer`
2. `CollisionSystem`
3. `DayNightSystem`
4. `Renderer`
5. `VisibilityOcclusionSystem`
6. `LightingSystem`

Это видно в:

- [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs#L74)

Из этого следует важная вещь:

- `VisibilityOcclusionSystem` рисует чёрные тени обзора прямо в сцену;
- `LightingSystem` отдельно строит и накладывает свет уже после этого.

---

## 3. Тайлы: solid, transparent, opaque

У тайлов сейчас есть три разных свойства, и они означают разное.

Описание прототипа тайла лежит здесь:

- [PrototypeManager.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/PrototypeManager.cs#L17)

Runtime-тайл хранит это здесь:

- [Tile.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/Tile.cs#L15)

### `solid`

`solid` отвечает только за физическую непроходимость.

Если тайл `solid = true`, то:

- `CollisionSystem` не даст пройти через него.

### `transparent`

`transparent` сейчас исторически существует на тайле, но в логике обзора не играет главную роль.

Это больше про смысл тайла как “визуально прозрачного/непрозрачного” для старых частей движка.

### `opaque`

`opaque` — это главное свойство именно для обзора и теней.

Если тайл `opaque = true`, то:

- он блокирует `line of sight`;
- от него строятся тени обзора игрока;
- он режет свет от ламп и других источников.

Пример тестовой стены:

- [Wall proto](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Content/Prototypes/Tiles/Wall/proto.json)

У неё:

- `solid: true`
- `opaque: true`

То есть она и непроходимая, и непроглядная.

### Почему это разделено правильно

Такое разделение нужно, чтобы можно было делать разные типы тайлов:

- обычная стена: `solid = true`, `opaque = true`
- стекло: `solid = true`, `opaque = false`
- густой дым в будущем: `solid = false`, `opaque = true`
- обычный пол: `solid = false`, `opaque = false`

---

## 4. TileMap: как движок понимает обзор

Файл:

- [TileMap.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/TileMap.cs)

### Главные методы

#### `IsOpaque(int x, int y)`

Файл:

- [TileMap.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/TileMap.cs#L63)

Проверяет, есть ли в клетке хотя бы один `opaque`-тайл на любом слое.

#### `HasLineOfSight(Point from, Point to)`

Файл:

- [TileMap.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/TileMap.cs#L74)

Это tile-to-tile LOS через Bresenham-подобную трассировку по клеткам.

Используется там, где хватает грубой проверки “видно ли клетку”.

#### `HasWorldLineOfSight(Vector2 fromWorld, Vector2 toWorld)`

Файл:

- [TileMap.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/TileMap.cs#L97)

Это более точная world-space проверка.

Она:

1. Берёт два мировых вектора.
2. Делит отрезок на маленькие шаги.
3. На каждом шаге смотрит, в какой тайл попал sample.
4. Если попали в `opaque` до цели, LOS блокируется.

Этот метод добавлен специально для случаев, когда центра тайла недостаточно.

Например:

- чтобы понять, видна ли стена хотя бы частично;
- чтобы не было эффекта “соседняя стена полностью перекрыла другую”.

---

## 5. Геометрия тени: как строится shadow polygon

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs)

Это общий helper для:

- теней обзора игрока;
- теней света.

### Главный метод

#### `AppendShadow(...)`

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L10)

Он принимает:

- позицию источника (`origin`)
- координаты тайла
- размер тайла
- длину тени
- ширину feather-края
- цвета основной тени и мягкого края

На выходе он добавляет вершины в:

- `mainVertices`
- `featherVertices`

### Как именно строится тень

#### Шаг 1. Берутся углы тайла

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L94)

Тайл представляется четырьмя углами.

Там есть `CornerInset`.

Сейчас он отрицательный:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L8)

Это сделано специально, чтобы соседние тени чуть перекрывали друг друга и не было щелей на стыках стен.

#### Шаг 2. Углы сортируются по углу относительно источника

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L47)

#### Шаг 3. Выбирается “задняя” грань тайла

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L59)

Идея такая:

- у тайла есть две возможные цепочки между крайними tangent-углами;
- выбирается та, что дальше от источника;
- именно она считается гранью, от которой должна уходить тень.

Это и решает старую проблему, когда тень строилась “от всего тайла” и могла закрывать саму стену.

#### Шаг 4. От крайних точек этой задней грани бросаются два луча вдаль

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L68)

Получаются:

- ближние точки грани
- две дальние точки

Из этого собирается shadow polygon.

#### Шаг 5. Основная тень триангулируется

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L143)

#### Шаг 6. По краям строится feather

Файл:

- [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs#L156)

Feather — это два узких полигона по бокам тени, у которых:

- у внутреннего ребра цвет плотнее;
- у внешнего ребра альфа уходит в ноль.

Именно это даёт мягкий край, а не просто жёсткий чёрный клин.

---

## 6. Система обзора игрока

Файл:

- [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs)

### Что она делает

Система рисует чёрные тени за видимыми непроглядными стенами.

Это именно визуальный слой для игрока.

NPC-логика сейчас на него напрямую не завязана. Для NPC надо использовать LOS из `TileMap`.

### Как работает `Draw()`

Файл:

- [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs#L34)

Порядок:

1. Находит игрока и его позицию.
2. Находит видимую область экрана в мировых координатах.
3. Перебирает `opaque`-тайлы в пределах видимой зоны.
4. Для каждого тайла проверяет грубую LOS видимость тайла.
5. Если тайл видим, строит от него тень через `TileShadowGeometry.AppendShadow(...)`.
6. Рисует основные тени.
7. Рисует feather-полигоны.
8. После этого заново рисует видимые `opaque`-тайлы поверх теней.

### Почему стены перерисовываются поверх

Файл:

- [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs#L142)

Это сделано специально.

Без этого тень ложилась поверх самой стены, и получался баг:

- скрывалось не только то, что за стеной;
- сама стена тоже частично чернела.

Теперь поведение такое:

- тень скрывает то, что за стеной;
- сама видимая стена остаётся видимой.

### Как определяется “видна ли сама стена”

Файл:

- [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs#L180)

Метод `IsOpaqueTileVisible(...)` проверяет не центр тайла, а несколько sample-точек:

- углы
- середины сторон

И использует:

- [TileMap.HasWorldLineOfSight(...)](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/TileMap.cs#L97)

Это сделано потому, что проверка только центра тайла ломала стыки стен:

- одна стена могла перекрывать другую,
- хотя реально должна была быть видна хотя бы часть её лицевой грани.

### Текущие параметры обзора игрока

Важные константы:

- ширина feather:
  [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs#L14)

- плотность feather:
  [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs#L80)

Если визуал покажется:

- слишком жёстким — увеличивай `FeatherWidth` или внутреннюю альфу;
- слишком “грязным” — уменьшай их.

---

## 7. Система освещения

Файл:

- [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs)

### Что делает LightingSystem

Он не рисует свет прямо в сцену.

Он строит отдельную `lightmap`, где:

- фон = `AmbientColor`
- источники света = яркие аддитивные пятна
- потом поверх световой карты накладываются тени от `opaque`-тайлов

После этого `lightmap` умножается на сцену.

### Почему именно multiply

Потому что логика такая:

- белое на lightmap = ничего не затемняет;
- тёмное на lightmap = затемняет сцену;
- свет делает области ярче относительно ambient;
- тени света возвращают эти области обратно к ambient там, где свет не должен проходить.

### Главные этапы

#### `BuildLightMap(GraphicsDevice gd)`

Файл:

- [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs#L42)

Что делает:

1. Переключает рендер в `_lightRT`
2. Заливает его `AmbientColor`
3. Рисует все активные `LightComponent` как круглые аддитивные пятна
4. Потом режет их тенями через `DrawLightOcclusion(...)`

#### `ApplyLightMap(GraphicsDevice gd)`

Файл:

- [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs#L84)

Накладывает `_lightRT` на уже готовую сцену через `MultiplyBlend`.

### Как рисуются сами источники света

Файл:

- [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs#L60)

Система ищет все сущности с `LightComponent`.

Позиция света берётся не всегда просто из `Transform`.

#### `TryGetLightPosition(...)`

Файл:

- [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs#L250)

Логика:

- если сущность активна в мире и имеет `Transform` — свет от её позиции;
- если это предмет в руках или на экипировке — свет от носителя;
- если предмет лежит в рюкзаке/контейнере — свет не рисуется.

Именно поэтому лампа:

- светит на земле;
- светит в руках;
- не светит в рюкзаке.

### Как свет режется стенами

Файл:

- [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs#L106)

`DrawLightOcclusion(...)`:

1. Перебирает источники света.
2. Для каждого перебирает `opaque`-тайлы в видимом районе.
3. Для каждого подходящего тайла строит тень через `TileShadowGeometry`.
4. Рисует основную тень поверх lightmap.
5. Рисует feather.

Но тут цвет тени не чёрный.

Для света main shadow рисуется цветом `AmbientColor`, а не `Black`.

Почему:

- свет не должен создавать “абсолютную тьму” за стеной;
- за стеной должно оставаться хотя бы ambient-освещение сцены.

То есть:

- FOV игрока скрывает за стеной чёрным;
- свет за стеной обрезается до ambient, а не в ноль.

Это две разные визуальные задачи.

---

## 8. Где задаётся свет у сущности

Компонент:

- [LightComponent.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Components/LightComponent.cs)

Поля:

- `radius`
- `intensity`
- `color`
- `enabled`

Пример предмета-лампы:

- [Lamp proto](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Content/Prototypes/Tools/Lamp/proto.json)

Логика включения/выключения:

- [LampComponent.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Items/LampComponent.cs)

---

## 9. Как добавить новый непроглядный тайл

Пример:

```json
{
  "id": "wall",
  "name": "Wall",
  "category": "tile",
  "solid": true,
  "transparent": true,
  "opaque": true,
  "color": "#6F665C",
  "sprite": {
    "source": "sprite.png",
    "srcX": 0,
    "srcY": 0,
    "width": 32,
    "height": 32
  }
}
```

Что значит:

- `solid: true` — нельзя пройти;
- `opaque: true` — нельзя видеть и нельзя светить сквозь тайл.

Если нужен тайл, который:

- блокирует движение, но не обзор — ставь `solid: true`, `opaque: false`
- блокирует обзор, но не движение — ставь `solid: false`, `opaque: true`

---

## 10. Как добавить новый источник света

Нужны компоненты:

```json
"light": {
  "color": "#FFD98A",
  "radius": 150,
  "intensity": 0.95,
  "enabled": true
}
```

Если это предмет, который должен:

- включаться и выключаться;
- светить в руках, но не в рюкзаке,

то нужен marker/логический компонент типа:

- [LampComponent.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Items/LampComponent.cs)

Он должен управлять `LightComponent.Enabled`.

---

## 11. Ограничения текущей реализации

Система уже рабочая, но пока не идеальна.

### 1. Тени считаются по тайлам, а не по склеенному силуэту стены

Из-за этого на длинных стенах могут ещё встречаться:

- мелкие артефакты на стыках;
- лишние внутренние shadow-wedges.

Сейчас это частично смягчено:

- overlap через `CornerInset < 0`
- более точная видимость стен через world-space samples

Но идеальное решение — следующий пункт.

### 2. Нет объединения соседних стен в общий контур

Сейчас каждая стена строит тень сама по себе.

Лучший следующий шаг:

- собрать соседние `opaque`-тайлы в wall clusters;
- построить единый внешний контур;
- уже от него строить тени.

Тогда:

- уйдут почти все швы между соседними стенами;
- станут красивее длинные стены;
- свет и FOV будут заметно чище.

### 3. NPC пока используют только логический LOS, а не визуальные тени

И это нормально.

Для NPC нужен в первую очередь:

- `IsOpaque`
- `HasLineOfSight`

Визуальный `VisibilityOcclusionSystem` нужен именно игроку.

---

## 12. Что лучше делать дальше

Если продолжать полировку, рекомендованный порядок такой:

1. Склейка соседних `opaque`-тайлов в общий wall silhouette.
2. Единый helper для “видима ли лицевая грань стены целиком”.
3. Более умное feather:
   уже не просто один клин по краю, а 2-ступенчатое penumbra.
4. Отдельная NPC vision system на тех же LOS-методах `TileMap`.
5. Поддержка частично прозрачных преград:
   например стекла, решёток, дыма, кустов.

---

## 13. Краткая карта файлов

Главные файлы системы:

- Рендер-пайплайн: [GameEngine.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/GameEngine.cs)
- Свет: [LightingSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/LightingSystem.cs)
- Тени обзора игрока: [VisibilityOcclusionSystem.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Systems/VisibilityOcclusionSystem.cs)
- Геометрия shadow polygons: [TileShadowGeometry.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Rendering/TileShadowGeometry.cs)
- Карта и LOS: [TileMap.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/TileMap.cs)
- Прототипы тайлов: [PrototypeManager.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/Core/PrototypeManager.cs)
- Runtime-тайл: [Tile.cs](/Users/more_tel/Desktop/SandboxRPG/MTEngine/World/Tile.cs)
- Пример стены: [Wall proto](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Content/Prototypes/Tiles/Wall/proto.json)
- Пример лампы: [Lamp proto](/Users/more_tel/Desktop/SandboxRPG/SandboxGame/Content/Prototypes/Tools/Lamp/proto.json)

