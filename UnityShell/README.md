# Оболочка Unity

Готовые скрипты, которые превращают ядро в игру, в которую Nick может сыграть: точка входа, фиксированный шаг, ввод → `TickInputs`, боты для остальных участников, рестарт, лог ввода, опрос в конце сессии, выбор варианта под слепой меткой и сборка демо `FactoryBuild.Demo`, которую диспетчер вызывает в конце спринта.

Первая Unity-фича игры (спринт 1) делает так:
1. `factory tool run unity --set mode=create --out .factory/unity` — пустой проект в `Unity/`.
2. Копирует `UnityShell/Assets/` в `Unity/Assets/` как есть.
3. В `Unity/Packages/manifest.json` добавляет ядро локальным пакетом: `"com.factory.game-core": "file:../../Game.Core"`.
4. `factory tool run unity --set mode=open` — компиляция без ошибок; `mode=execute --set method=FactoryBuild.Demo` — билд собирается.
5. Заменяет пример рендера `Assets/Game/ExampleRaceView.cs` своим (примитивы, камера, HUD) — это единственная игровая часть оболочки.

Что где:
- `Factory/Runtime/FactoryGame.cs` — точка входа, сама появляется в любой сцене. Шагает `Simulation` по `TicksPerSecond`, человек — участник 0, остальных ведут боты (`Game.Core/Data/bots/session.json → opponents`). R — рестарт.
- `Factory/Runtime/InputReader.cs` — клавиатура → `PlayerInput`: WASD/стрелки — стик, пробел/Shift/E/Q — кнопки 0–3. Работает и со старым, и с новым Input System.
- `Factory/Runtime/NickFiles.cs` — папка `nick/` рядом с exe: `survey-*.json` (вердикт, «сыграл бы ещё», где стало скучно, вариант) и `inputs-*.txt` (лог ввода для повтора `Game.Sim --replay`). Диспетчер забирает их в `playtest/nick/S-N/`.
- `Factory/Runtime/Overlay.cs` — выбор варианта (A/B/C) и опрос, на `OnGUI`, без ассетов.
- `Factory/Editor/FactoryBuild.cs` — `FactoryBuild.Demo`: Windows-билд в `FACTORY_BUILD_OUT`, данные ядра в `StreamingAssets/Data`. Если в Build Settings нет сцен, создаёт `Assets/Scenes/Main.unity`.

Логики игры в оболочке нет и не должно появиться: правила — в `Game.Core`.
