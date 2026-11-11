# Дуэль 1×1 (F-030)

Арена `duel` (16×12 м, два спавна друг напротив друга, два «куба» и две «сферы» вне прямой между собаками), правила `Duel`. Числа — `Game.Core/Data/rules/duel.json`, арена и длина матча — `Data/arenas/duel.json` (блок `data` кладёт `GameData.WithArena`: 2 участника, `match.seconds` 170, `game.session.max_seconds` 180).

**Урон удара** = `max(damage_min, damage_per_strength × Hit.Strength)`, сильный удар × `strong_multiplier`; `Hit.Strength` = масса хозяина × квадрат относительной скорости (1500 на пороге 5 м/с … 11760). ХП не уходит ниже нуля. Пример при нынешних числах: подросток на 7 м/с — 17.6 из 40, качок на 7 м/с — 32.3, сильный удар качка (10 м/с) — нокаут с одного раза.

**Раунд.** ХП ≤ 0 → `knocked_out` (Reward 1 нападающему, Threat 1 жертве, `first_blood` у первого в матче), очко через `Match.Award(…, "round")`. Истёк `round_s` без нокаута — очко тому, у кого больше ХП, при равенстве никому. Пауза `between_rounds_s`: пары возвращаются на спавны сразу и удерживаются там каждый тик, ввод их не ведёт; потом полные ХП и `arena_event` `round_start`. Матч кончается на `rounds_to_win` очках (`MatchModule.SetTarget`), таймер матча — запасной конец.

**Тюнить** (в json): `hp` 40, `damage_per_strength` 0.006, `damage_min` 12, `strong_multiplier` 1.1, `rounds_to_win` 3, `round_s` 30, `between_rounds_s` 3; в арене — `width`, `height`, `obstacles`, `spawns`. Потолок: 5 раундов по 30 с с паузами 162 с ≤ 170 ≤ 180. Подбирали так, чтобы `smart` против `idle` выбивал соперника на всех сидах за обоих хозяев: `smart` бьёт около трёх раз в минуту, поэтому ХП мало. Сильный удар качка выбивает сразу — гейт и плейтест решат, не слишком ли это.

Сенсоры бота: `hp_<q>` (доля 0..1), `round`, `round_time_left`, `score_<q>`. Метрики на игрока: `rounds_won`, `damage_dealt`, `damage_taken`, `hits`, `hits_taken`, `near_misses_dodged`.
