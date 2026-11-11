# Честный кадр (F-039)

Правила, по которым рисуется и проверяется фейк-скрин игры в этом спринте. Все числа переписаны из кода и данных (источник указан рядом); ничего не вычислено вручную и не оценено на глаз. Проценты экрана, позиции фигур и точные значения HUD — только в «Кадре-основе» (F-040, `art/frame-base.json`, `ViewMath.ToScreen` из F-034).

Координаты — плоскость симуляции XY в метрах, центр газона (0, 0); Unity кладёт её на землю XZ (`ViewMath.ToWorld(simX, simY, height) = (simX, height, simY)`), то есть sim-Y идёт от камеры вглубь кадра.

## Сцена

Арена `park`, «Парк: Царь газона» (`Game.Core/Data/arenas/park.json`, `"rules": "KingOfLawn"`).

- **Газон** `width` 24 × `height` 16 м, цвет `(0.40, 0.62, 0.30)`. Вокруг — **песочная окантовка** («sand paths») на 8 м шире и выше газона, цвет `(0.78, 0.72, 0.60)` (`ArenaView.BuildGround`). Фон камеры за ней — небесный `(0.70, 0.82, 0.92)` (`ArenaView.PlaceCamera`).
- **Спавны** (`spawns`, индекс = игрок):

| Игрок | Собака (x, y) | Хозяин (x, y) |
|---|---|---|
| 0 | (−6, −4) | (−8, −5.5) |
| 1 | (6, −4) | (8, −5.5) |
| 2 | (6, 4) | (8, 5.5) |
| 3 | (−6, 4) | (−8, 5.5) |

- **Деревья-столбы** — 4 круга `radius` 0.4 (`obstacles`, `shape: circle`): (−3.5, −2.5), (3.5, 2.5), (−9, 0), (9, 0). Вид (`ArenaView.BuildObstacles`): ствол-цилиндр диаметром 2·r, центр на высоте 1.0, цвет `(0.45, 0.32, 0.22)`; крона-шар диаметром 5·r на высоте 2.3, цвет `(0.25, 0.48, 0.22)`.
- **Лавочки** — 3 прямоугольника (`shape: rect`, ширина × высота в метрах): (0, −6.5) 2 × 0.6; (0, 6.5) 2 × 0.6; (−3, 1) 0.6 × 2. Вид: сиденье-куб высотой 0.5, спинка толщиной 0.08 и высотой 0.5 по длинной стороне, цвет дерева `(0.62, 0.44, 0.27)`, спинка ×0.85.
- `props` пуст: других объектов на арене нет.
- **Зона очков** — светлый круг на земле радиуса `zone_radius_m` = 3.5 (`Game.Core/Data/rules/king_of_lawn.json`), цвет `(0.80, 0.92, 0.58)`. **Следующая зона** — более бледный круг того же радиуса, цвет `(0.60, 0.76, 0.44)`, появляется за `announce_s` = 3 с до переезда; круг переезжает каждые `period_s` = 15 с на `move_min_m`–`move_max_m` = 8–14 м (`king_of_lawn.json`; цвета — `ArenaView`, объекты `Zone`, `NextZone`).
- **Свет** (`ArenaView.SetupLight`): один направленный «Sun», поворот (60°, −25°, 0), интенсивность 0.75, цвет `(1, 0.97, 0.9)`, мягкие тени `LightShadows.Soft`, сила тени 0.55; плоский эмбиент `(0.45, 0.47, 0.50)`. Материалы — `Legacy Shaders/Diffuse` (матовые).

## Камера

Константы `Unity/Assets/Game/ArenaView.cs`:

- `CameraPitch = 60f` — наклон вниз 60° (`art/style.md` → «Камера»), рыскание 0, крен 0: **без вращения**, камера смотрит вдоль sim-Y.
- `CameraFov = 40f` — вертикальный угол обзора 40°, перспектива (`orthographic = false`).
- `CameraMargin = 1.2f` — поле вокруг газона, м.
- Кадр **16:9**: при неизвестном экране `aspect = 16f / 9f`.
- Положение: `rot = Quaternion.Euler(CameraPitch, 0f, 0f)`, камера в `rot * Vector3.back * d`, смотрит в центр газона, где `d` — расстояние из формулы ниже, вызванной как `ViewMath.CameraDistance(arena.Width, arena.Height, CameraPitch, CameraFov, aspect, CameraMargin)`.

Формула `ViewMath.CameraDistance` из `Unity/Assets/Game/ViewMath.cs`, как есть:

```csharp
/// <summary>Camera distance along its viewing axis such that the whole w×h arena
/// (plus a margin, metres) is in frame at the given pitch and vertical field of view.</summary>
public static float CameraDistance(float width, float height, float pitchDeg, float vFovDeg, float aspect, float margin)
{
    var rot = Quaternion.Euler(pitchDeg, 0f, 0f);
    var forward = rot * Vector3.forward;
    float hw = width / 2 + margin, hh = height / 2 + margin;
    float tanV = Mathf.Tan(vFovDeg * Mathf.Deg2Rad / 2), tanH = tanV * aspect;
    float need = 0f;
    foreach (var corner in new[] { new Vector3(-hw, 0, -hh), new Vector3(hw, 0, -hh), new Vector3(-hw, 0, hh), new Vector3(hw, 0, hh) })
    {
        // camera at -forward*d looking at the origin: view-space coordinates of the corner
        var right = rot * Vector3.right;
        var up = rot * Vector3.up;
        float x = Vector3.Dot(corner, right), y = Vector3.Dot(corner, up), z = Vector3.Dot(corner, forward);
        // need d + z >= |x| / tanH and d + z >= |y| / tanV
        need = Mathf.Max(need, Mathf.Abs(x) / tanH - z, Mathf.Abs(y) / tanV - z);
    }
    return need;
}
```

Смысл словами: весь газон плюс 1.2 м поля целиком в кадре, ближний край газона внизу кадра, дальний — вверху, перспектива сжимает дальний край. Численное `d` и экранные проценты здесь не приводятся — их даёт «Кадр-основа».

## Момент

**Первый сильный удар матча** (событие `strong_hit`), кадр в момент удара:

- Хозяин нападающего **в полёте**: поднят над землёй на `ViewMath.OwnerHeight(airborne) = airborne * MaxOwnerHeight`, `MaxOwnerHeight = 1.2f` м; на сильном ударе `Airborne` близко к 1, то есть высота почти 1.2 м.
- Его **тень** на земле растянута по `ViewMath.ShadowScale(airborne) = 1f + 0.8f * airborne` и бледнее: `ViewMath.ShadowAlpha(airborne) = Mathf.Lerp(0.35f, 0.18f, airborne)` — по тени читается высота полёта.
- Его **поводок натянут**: ширина `ViewMath.LeashWidth(tension) = Mathf.Lerp(0.05f, 0.16f, tension)` — от 0.05 м провисший до 0.16 м натянутый; цвет `ViewMath.LeashColor` идёт от цвета игрока ×0.7 к цвету игрока, смешанному с белым на 0.35 — натянутый поводок светлеет к белому.
- **Собака-жертва вспыхивает белым**: цвет тела `Color.Lerp(fur, Color.white, Flash)`, `ViewMath.FlashAmount(strength, attackerMass, strongThresholdMps) = Mathf.Clamp01(Mathf.Sqrt(strength / attackerMass) / strongThresholdMps)` — сильный удар даёт полную вспышку (1).
- **Хотя бы одна собака стоит в круге** зоны.
- **Следующий круг уже отмечен** бледным кругом на газоне (значит, на экране и подсказка «Круг скоро переедет…»).
- **Таймер — середина матча** (матч `seconds` = 120, `Game.Core/Data/match.json`), не финальные секунды: таймер белый, без «ФИНАЛ!».
- **Счёт у игроков разный** — четыре разных числа, у лидера рамка.
- Над собакой **всплывает «+N»** (текст `HudMath.PopupText`: «+N в круге», «ВЫБИЛ! +N» или «ВЫБИЛ ЛИДЕРА! +N»).

Кто где стоит, кто бьёт кого, кто в круге и точные числа таймера и счёта — не здесь, а в «Кадре-основе» (F-040).

## Пары

4 игрока (`game.json` → `players: 4`). Цвет по индексу — `ViewMath.PlayerColors`, он же на ошейнике, поводке и акценте одежды хозяина:

| Игрок | Цвет | RGB (`ViewMath.PlayerColors`) | Собака | Хозяин |
|---|---|---|---|---|
| 0 (ты, «ТЫ») | красный | (0.88, 0.17, 0.17) | бордер-колли | подросток в телефоне |
| 1 | синий | (0.18, 0.41, 0.92) | The Stack (четыре чихуахуа, четыре поводка к одному хозяину) | качок с шейкером |
| 2 | жёлтый | (0.97, 0.80, 0.12) | бордер-колли | бабка с зонтом и сумкой на колёсиках |
| 3 | фиолетовый | (0.58, 0.27, 0.82) | дог | подросток в телефоне |

В кадре все три собаки M1 (колли, дог, The Stack) и все три хозяина M1 (подросток, бабка, качок) из `design/roster.md`. Игрок 0 — человек (`FactoryGame.HumanPlayer = 0`, `game.json` → `human_owner: "teen"`), поэтому у него колли с подростком, как в ядре. Синий и жёлтый — пары, которые `art/style.md` просит к следующему заходу («чихуахуа с качком, колли с бабкой»); фиолетовый — дог с подростком, как в согласованном превизе `art/refs/dane-teen-phone-01.png`.

**Честно:** в ядре сейчас одна собака на всех — `game.json` → `pairs: [{ "dog": "collie" }]`, в `Game.Core/Data/dogs/` только `collie.json` (`radius` 0.35); хозяева — только `teen` и `jock` (`human_choices`). Ни дога, ни The Stack, ни бабки в ядре нет. Поэтому в «Кадре-основе» тела, радиусы и позиции у всех одинаковы колли-шные, а порода и типаж из таблицы — назначение вида поверх них, не данные симуляции.

## Что может нарисовать простое 3D

По этому разделу сверяется каждая картинка спринта; несовпадения записываются честно, а не ретушируются.

**Разрешено**
- Матовые однотонные материалы, почти без текстур (в игре — `Legacy Shaders/Diffuse` с одним цветом).
- Скруглённые формы без видимых граней — `concept.md`: «простое 3D, не лоу-поли, что-то среднее»; `art/style.md` → «Форма»: игрушечные формы, слегка большие головы, глаза-точки.
- Один направленный свет, мягкие короткие тени (см. «Сцена» → «Свет»).
- Поводок — **прямой отрезок** от ошейника собаки к руке хозяина: изломов, намоток и провисания дугой в ядре пока нет.
- Масштабы из данных: радиус собаки 0.35 м (`Game.Core/Data/dogs/collie.json`), рост хозяина 1.7–1.9 м (в игре: тело-капсула с центром на 0.85 м, голова на 1.95 м — `ArenaView.BuildOwner`), радиус зоны `zone_radius_m` = 3.5 м, деревья и лавочки — по «Сцене».
- Цвета игроков только на ошейнике, поводке и акценте одежды хозяина; окружение приглушённое.
- Белая вспышка жертвы и тень полёта хозяина — это и есть все «эффекты».

**Запрещено**
- Шерсть, ткань и волосы с симуляцией.
- Частицы сложнее вспышки: искры, пыль, клочья травы, следы скорости, звёздочки.
- Толпа и зрители; любые фигуры, кроме четырёх пар.
- Погода (дождь, лужи, листопад, туман); время суток кроме дня.
- Объекты, которых нет в json арены: ларьки, фонтаны, заборы, фонари, урны, дорожные знаки, дома в кадре. На арене только газон, песочная окантовка, 4 дерева и 3 лавочки.
- Рэгдолл-позы с деталями пальцев, мимикой, анатомией.
- Всё из `art/style.md` → «Что точно не делаем»: кровь, травмы, злые или страдающие лица, туалетный юмор; реалистичные текстуры, шерсть, сложные шейдеры; пиксель-арт и резкое гранёное лоу-поли; тёмные и ночные сцены; мелкие декоративные детали, не читаемые с игровой камеры.

## HUD

Только то, что рисует `Unity/Assets/Game/HudView.cs` в режиме «Царь газона» (`OnGUI`: `DrawTimer`, `DrawScores`, `DrawNextZone`, `DrawHelp`, `DrawPopups`). Размеры в коде даны для высоты экрана 720 и масштабируются `k = Screen.height / 720f`; шрифт жирный, белая/цветная надпись с тёмной обводкой.

1. **Таймер** — сверху по центру, формат `м:сс` (`HudMath.Clock`), белый, кегль 40. На финальных секундах (`match.final_seconds` = 10) — красный `(1, 0.25, 0.2)`, пульсирует крупнее (до +15 %), под ним красное «ФИНАЛ!» (кегль 22). В кадре этого спринта — середина матча, то есть белый и без «ФИНАЛ!».
2. **Ряд табличек счёта** — под таймером по центру, по одной на игрока в порядке индекса: тёмная полупрозрачная плашка `(0, 0, 0, 0.55)` 118 × 44, слева полоска цвета игрока, число очков в цвете игрока (кегль 28); у лидера (`HudMath.Leader`) белая рамка; на табличке игрока 0 в левом верхнем углу белая пометка «ТЫ».
3. **Подсказка** «Круг скоро переедет — следующий уже отмечен на газоне» — по центру под табличками, светло-зелёная `(0.75, 0.95, 0.6)`, кегль 18; видна, пока отмечен следующий круг.
4. **Всплывающие «+N»** над собаками — в цвете игрока, поднимаются на `HudMath.PopupRiseM` = 1.8 м и гаснут за `HudMath.PopupLifeS` = 1.6 с; тексты «+N в круге» (кегль 22), «ВЫБИЛ! +N» и «ВЫБИЛ ЛИДЕРА! +N» (кегль 30).
5. **Строка помощи** — внизу слева, две строки (кегль 16): «Режим мыши: ТОЛЧОК (курсор скрыт, двигай мышь)   [Tab — переключить]» или «Режим мыши: КУРСОР (собака бежит к точке)   [Tab — переключить]»; под ней «ЛКМ/Q — абилка собаки   ПКМ/E — абилка хозяина   R — заново».

Экран победителя (`DrawWinner`) — только после конца матча, в кадр середины матча не входит.

**Планируется — дуэль (F-031), в картинках этого спринта не рисуется.** `DrawDuel`: часы раунда, счёт раундов, «Раунд N», полоска ХП над каждой собакой в цвете игрока, баннер («Раунд N», «Нокаут!»), итог матча. По `progress.md` F-031 уже влит (2026-11-11), но дуэльная панель заменяет панель «Царя газона» только при правилах дуэли (`DuelHudModel.Applies`), а кадр — арена `park` с `KingOfLawn`, поэтому её элементов в кадре нет. Если F-035 решит рисовать дуэль — это отдельный кадр, не этот.

## Промпт

Каркас запроса для `image-gen`. Размер — `1536x1024` (так тул пишет `size` в `art/refs/arena-park-overview-01.provenance.json`; ближайший к 16:9 альбомный). Плейсхолдеры каркаса F-039 (таймер, четыре числа счёта, нападающий, жертва, кто в круге) заменены значениями из «Кадра-основы» (F-040, лист переснят F-049): таймер «1:39», счёт 8 / 10 / 0 / 0, нападающий жёлтый, жертва синий, в круге красный. Там, где лист кадра расходится с «Моментом» (нет следующего круга, хозяйка летит на 0.76 м, а не 1.2 м, летят два хозяина), абзац следует листу кадра; «+N» убран — его текста в листе нет.

```json
{
  "prompt": "<АБЗАЦ СЦЕНЫ>\n\n<АБЗАЦ НАПРАВЛЕНИЯ>",
  "size": "1536x1024",
  "n": 1,
  "refs": ["art/refs/arena-park-overview-01.png", "art/refs/dane-teen-phone-01.png"],
  "name": "frame-<направление>-01"
}
```

**Абзац сцены — итоговый, литеральный** (одинаковый для всех направлений; F-035 копирует его без правок):

> In-game screenshot of a 4-player top-down party brawler, 16:9 landscape. Perspective camera tilted 60° down, vertical field of view 40°, no rotation: it looks straight along the lawn, the whole lawn plus a narrow 1.2 m margin fits the frame, near edge at the bottom, far edge at the top, slightly narrower by perspective. Arena: a flat rectangular green lawn 24 × 16 m surrounded by a sand-coloured border; on it exactly four round tree trunks with ball crowns (two near the centre placed diagonally, one at the middle of the left edge, one at the middle of the right edge), exactly three short wooden benches (one at the middle of the near edge, one at the middle of the far edge, one standing lengthwise just left of the centre); nothing else. On the ground one pale light-green circle 3.5 m in radius (the scoring zone), centred right of the middle of the frame, slightly above mid-height; no second circle. Four dog-and-owner pairs, each joined by straight leashes; player colour only on the collar, the leash and a band on the owner's shirt: red — border collie with a teenager staring at his phone; blue — a pack of four chihuahuas on four leashes to one muscular gym guy with a shaker; yellow — border collie with a granny holding an umbrella and pulling a wheeled shopping bag; purple — great dane with a teenager staring at his phone. Where they are: the red collie stands inside the scoring circle near its centre, its teenager just off the ground (about 0.1 m) further right and higher, its leash pulled taut; the yellow collie is right of the centre just above the middle, left of the circle; the purple great dane stands in the upper-left quarter of the lawn, far from the circle, its teenager standing still further left and higher, near the far-left part of the lawn. Moment: the first strong hit of the match — the yellow owner, the granny, is flying through the air about 0.8 m above the lawn just right of the centre of the frame, slightly below the middle, her stretched, paler shadow on the grass below, her leash pulled taut, thicker and lighter; the flying granny slams into the blue chihuahuas right beside her, which flash white; the blue gym guy is also lifted about 0.9 m off the ground right of and above his dogs, between them and the circle, his leashes nearly taut. The red dog stands inside the scoring circle. The owners stay calm and busy with their own thing. HUD overlay, bold white text with a dark outline: top centre the timer "1:39" in white; under it a row of four dark semi-transparent score tiles left to right red 8, blue 10, yellow 0, purple 0, each with a colour strip on the left and the number in the player's colour, the blue tile (the leader) with a thin white frame, the red tile marked "ТЫ"; bottom left two small lines "Режим мыши: КУРСОР (собака бежит к точке)   [Tab — переключить]" and "ЛКМ/Q — абилка собаки   ПКМ/E — абилка хозяина   R — заново". No other UI. Sunny weekday daylight, one directional sun, soft short shadows. No crowd, no weather, no extra objects (no stalls, fountains, fences, lamps, buildings), no particles beyond the white flash, no blood, no angry faces, no fur or cloth simulation.

**Абзац направления** (слот; меняет только форму, материалы, свет, палитру и контур — ничего из сцены, камеры, момента и HUD):

> Style direction «{direction}»: shapes — {shape}; materials — {materials}; light — {light}; palette — {palette}; outline — {outline}.

Пример заполнения по `art/style.md`: shapes — rounded toy-like stylised 3D, between low-poly and realistic, no visible facets, slightly big heads, dot eyes; materials — matte flat single colours, almost no textures; light — soft daylight; palette — muted grass green, warm grey, sand, brick beige, saturated player colours only on collars, leashes and shirt bands; outline — none.

## Чеклист приёмки

F-035 сверяет каждую картинку по пунктам; каждое несовпадение записывается словами, картинка не ретушируется.

- [ ] **Камера:** вид сверху под наклоном ~60°, перспектива, без поворота; весь газон с узким полем в кадре, 16:9 (файл 1536×1024).
- [ ] **Арена:** газон с песочной окантовкой; ровно 4 дерева и 3 лавочки на местах из «Сцены» (две у центра по диагонали, по одному у середин левого и правого края; лавочки у ближнего и дальнего края по центру и одна вдоль левее центра).
- [ ] **Четыре пары в своих цветах:** красный, синий, жёлтый, фиолетовый на ошейнике, поводке и акценте хозяина; собаки и хозяева по «Парам»; цвет игрока нигде больше.
- [ ] **Полёт на натянутом поводке:** летит **жёлтая** бабка (~0.8 м) чуть правее центра кадра, под ней растянутая бледная тень, жёлтый поводок прямой, толстый и светлый; **синие** чихуахуа вспыхнули белым; синий качок тоже в воздухе (~0.9 м) правее и выше них. Красный подросток почти на земле (~0.1 м), фиолетовый подросток на земле.
- [ ] **Круг:** один светлый круг правее центра кадра чуть выше середины высоты; в круге стоит **красная** колли, фиолетовый дог далеко от круга слева вверху; **следующего круга нет** (`next_zone: null`).
- [ ] **HUD с числами из листа кадра:** таймер **«1:39»** белый; таблички слева направо **красный 8, синий 10, жёлтый 0, фиолетовый 0**; белая рамка на **синей**; «ТЫ» на красной; строка помощи; **нет** подсказки про круг и нет «+N» (в листе кадра их нет); нет элементов, которых нет в `HudView.cs`, и нет дуэльных.
- [ ] **Ничего лишнего:** ни одного пункта из «Запрещено».

## Кадр-основа

Источник — лист кадра `art/frame-base.json` (F-034: `-arena park -capture` на ботах; с F-049 сид и тик не константы — тест кадра парка ищет их сам по условиям кадра, `ViewCapture.FindParkMoment`; экранные доли — `ViewMath.ToScreen`). Все числа переписаны из него как есть. Доли экрана — как у вьюпорта Unity: x слева направо, y **снизу вверх** (0 — нижний край кадра, 1 — верхний). Чего в листе нет, так и помечено.

### Момент и HUD

- Арена `park`, сид `2`, кадр `16:9`.
- Тик `630`, `time_left_s` = 99 → таймер **«1:39»** (`HudMath.Clock` округляет секунды вверх, формат `м:сс`). Белый, без «ФИНАЛ!»: до финальных 10 с далеко.
- Счёт `scores` = **[8, 10, 0, 0]**: красный 8, синий 10, жёлтый 0, фиолетовый 0. Лидер — синий (10), рамка на его табличке.
- Всплывающее «+N» (текст и над кем) — в листе кадра нет.
- Режим мыши для строки помощи — в листе кадра нет (кадр снят на ботах).

### Удар

- `hit`: `attacker` = **2 → жёлтый → бордер-колли с бабкой**; `victim` = **1 → синий → The Stack с качком**; `flash` = 1 — полная белая вспышка жертвы.
- Сила удара и тип события (`hit` или `strong_hit`) — в листе кадра нет.
- Красный (игрок «ТЫ») в ударе не участвует; назначение пар из «Пар» не менялось.

### Круг и следующий круг

- Круг `zone`: центр (8.1716, 1.9568) м, `radius` 3.5 м; центр на экране `zone_screen` = (0.7299, 0.5848) — правее центра кадра, чуть выше середины.
- Следующий круг: `next_zone` = `null` — **в листе кадра нет**, то есть бледный круг не отмечен и подсказки «Круг скоро переедет…» в этом кадре нет.
- Флаг «кто в круге» — в листе кадра нет. Из позиций собак ниже ближе всех к центру круга стоит красная; синяя и жёлтая — левее, вне круга; фиолетовая — далеко слева.

### Пары в кадре

| Игрок | Цвет → пара | Собака x, y (м) | Собака на экране | Хозяин x, y (м) | Хозяин на экране | `height_m` | `airborne` | `speed_mps` | `tension` | `width_m` |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | красный → колли + подросток | (6.9168, 1.1349) | (0.6975, 0.5499) | (9.2382, 3.0363) | (0.7559, 0.6327) | 0.129 | 0.1075 | 0.9676 | 1 | 0.16 |
| 1 | синий → The Stack + качок | (3.2009, −0.9008) | (0.595, 0.4588) | (5.578, 0.1094) | (0.6674, 0.5297) | 0.9223 | 0.7686 | 6.9176 | 0.8609 | 0.1447 |
| 2 | жёлтый → колли + бабка | (4.1515, 1.183) | (0.6185, 0.552) | (2.4949, −1.325) | (0.5766, 0.4581) | 0.7608 | 0.634 | 5.7057 | 1 | 0.16 |
| 3 | фиолетовый → дог + подросток | (−6, 4) | (0.3373, 0.667) | (−8, 5.5) | (0.2886, 0.7238) | 0 | 0 | 0 | 0.8333 | 0.1417 |

Словами по долям экрана:
- **Жёлтая пара** (нападающий): собака правее центра чуть выше середины; хозяйка-бабка в воздухе чуть правее центра кадра и чуть ниже середины, на 0.76 м, поводок натянут до предела (0.16 м).
- **Синяя пара** (жертва): The Stack правее центра чуть ниже середины, рядом с летящей бабкой; качок **тоже в воздухе** (0.92 м) правее и выше своих собак, поводки натянуты почти до предела (0.8609).
- **Красная пара:** колли в круге правее центра; подросток почти на земле (0.13 м) ещё правее и выше, поводок натянут до предела.
- **Фиолетовая пара:** дог в левой верхней четверти далеко от круга, подросток стоит неподвижно ещё левее и выше — на своём спавне.
- Для The Stack лист кадра даёт одну точку собаки: раскладка четырёх чихуахуа вокруг неё — в листе кадра нет.

### Газон на экране

- Углы газона `lawn_corners_screen`: ближние (низ кадра) (0.0877, 0.0768) и (0.9123, 0.0768); дальние (верх кадра) (0.8041, 0.8121) и (0.1959, 0.8121) — газон трапецией, сужается вглубь.
- Центр газона на экране — в листе кадра нет.

### Снимок из билда

Снимок из билда ещё не снят (`art/previews/frame-base.png` нет).
