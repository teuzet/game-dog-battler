# Оболочка Unity

Готовые скрипты, которые превращают ядро в игру, в которую Nick может сыграть: точка входа, фиксированный шаг, ввод → `TickInputs`, боты для остальных участников, рестарт, лог ввода, опрос в конце сессии, выбор варианта под слепой меткой и сборка демо `FactoryBuild.Demo`, которую диспетчер вызывает в конце спринта.

Первая Unity-фича игры (спринт 1) делает так:
1. `factory tool run unity --set mode=create --out .factory/unity` — пустой проект в `Unity/`.
2. Копирует `UnityShell/Assets/` в `Unity/Assets/` как есть.
3. В `Unity/Packages/manifest.json` добавляет ядро локальным пакетом: `"com.factory.game-core": "file:../../Game.Core"`.
4. `factory tool run unity --set mode=open` — компиляция без ошибок; `mode=execute --set method=FactoryBuild.Demo` — билд собирается.
5. Заменяет пример рендера `Assets/Game/ExampleRaceView.cs` своим (примитивы, камера, HUD) — это единственная игровая часть оболочки.

Каждый прогон `factory tool run unity` долгий и многословный: фича укладывается в один-два прогона (компиляция и тесты — одна фича, билд и скриншоты — следующая), а в логе ищут `error CS`, `Exception`, `Failed` и итог тестов, не читая его целиком.

Что где:
- `Factory/Runtime/FactoryGame.cs` — точка входа, сама появляется в любой сцене. Шагает `Simulation` по `TicksPerSecond`, человек — участник 0, остальных ведут боты (`Archetype.Opponents(data)`: `Game.Core/Data/rules/<правила>.json → opponents`, например манекены песочницы, иначе `bots/session.json → opponents`). R — рестарт.
- `Factory/Runtime/InputReader.cs` — мышь → `PlayerInput` (логика без вызовов Unity — в `MouseInput.cs`): Tab — режим «толчок» (мышь захвачена, сдвиг мыши копится между тиками в `AimX/AimY`, сантиметры арены, `CmPerMouseUnit` — чувствительность) или «курсор» (собака бежит к точке под курсором); ЛКМ или Q — абилка собаки, ПКМ или E — абилка хозяина; R — рестарт. Читает старый Input Manager. `FactoryGame` даёт видам событие `Stepped`, `Alpha` (доля кадра между тиками) и `MenuOpen`; аргумент `-arena <имя>` берёт другую арену из `Data/arenas`.
- `Factory/Runtime/NickFiles.cs` — папка `nick/` рядом с exe: `survey-*.json` (вердикт, «сыграл бы ещё», где стало скучно, вариант) и `inputs-*.txt` (лог ввода для повтора `Game.Sim --replay`). Диспетчер забирает их в `playtest/nick/S-N/`.
- `Factory/Runtime/Overlay.cs` — выбор варианта (A/B/C), меню перед матчем и опрос, на `OnGUI`, без ассетов. В меню: арена (`Data/arenas`, по `title`; по умолчанию `game.arena` или `-arena`), соперник-бот (`bots.session.tracked` без `breaker-*`; заменяет первого из `opponents` на любой арене), переключатель «Я играю / Бот против бота» с архетипом за участника 0, хозяин и режим мыши. В режиме «Бот против бота» участника 0 ведёт `BotDriver`, человек смотрит: R — рестарт на тех же настройках, Esc — назад в меню. Логику выбора (без Unity) держит `MatchSetup.cs`; арена, режим и архетипы пишутся в `survey-*.json`, арена — в лог ввода.
- `Factory/Editor/FactoryBuild.cs` — `FactoryBuild.Demo`: Windows-билд в `FACTORY_BUILD_OUT`, данные ядра в `StreamingAssets/Data`. Если в Build Settings нет сцен, создаёт `Assets/Scenes/Main.unity`.

Логики игры в оболочке нет и не должно появиться: правила — в `Game.Core`.
