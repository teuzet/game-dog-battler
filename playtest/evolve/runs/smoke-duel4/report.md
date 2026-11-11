# evolve: duel4

Поколений: 1, закончен; без улучшения подряд: 0.
Чемпион последнего поколения: g0-00, оценка 0.3333. Лучшая оценка за прогон: 0.3333 (g0-00, на своих сидах).

| поколение | лучший | средний |
|---|---|---|
| 0 | 0.3333 | -0.5729 |

## Параметры чемпиона

| параметр | значение |
|---|---|
| `bots.decide.decide_period_s` | 0.479481 |
| `bots.decide.switch_margin` | 0.408635 |
| `bots.decide.approach_m` | 2.229622 |
| `bots.decide.bias_move` | 0.284687 |
| `bots.decide.w_move_dist` | -1.199581 |
| `bots.decide.w_move_in_reach` | -0.140094 |
| `bots.decide.w_move_my_owner_speed` | -0.212096 |
| `bots.decide.w_move_rival_owner_speed` | 0.768911 |
| `bots.decide.w_move_threat` | 0.677596 |
| `bots.decide.w_move_cover` | 0.843555 |
| `bots.decide.w_move_my_hp` | -0.763036 |
| `bots.decide.w_move_rival_hp` | -0.273999 |
| `bots.decide.w_move_my_speed` | 0.112357 |
| `bots.decide.w_move_round_time` | 0.358132 |
| `bots.decide.w_move_my_owner_closing` | -1.849411 |
| `bots.decide.w_move_rival_owner_closing` | -0.446261 |
| `bots.decide.bias_spin` | -0.630337 |
| `bots.decide.w_spin_dist` | -1.336756 |
| `bots.decide.w_spin_in_reach` | 2.0 |
| `bots.decide.w_spin_my_owner_speed` | 0.967732 |
| `bots.decide.w_spin_rival_owner_speed` | -0.71218 |
| `bots.decide.w_spin_threat` | 1.000857 |
| `bots.decide.w_spin_cover` | 1.861553 |
| `bots.decide.w_spin_my_hp` | 2.0 |
| `bots.decide.w_spin_rival_hp` | 0.586073 |
| `bots.decide.w_spin_my_speed` | 1.123626 |
| `bots.decide.w_spin_round_time` | -0.709017 |
| `bots.decide.w_spin_my_owner_closing` | -1.259057 |
| `bots.decide.w_spin_rival_owner_closing` | -2.0 |
| `bots.decide.bias_strike` | 1.0 |
| `bots.decide.w_strike_dist` | 1.060073 |
| `bots.decide.w_strike_in_reach` | -1.513973 |
| `bots.decide.w_strike_my_owner_speed` | 1.281723 |
| `bots.decide.w_strike_rival_owner_speed` | 1.24412 |
| `bots.decide.w_strike_threat` | 0.395042 |
| `bots.decide.w_strike_cover` | -0.07794 |
| `bots.decide.w_strike_my_hp` | 0.03285 |
| `bots.decide.w_strike_rival_hp` | 0.561972 |
| `bots.decide.w_strike_my_speed` | 1.639185 |
| `bots.decide.w_strike_round_time` | -0.129131 |
| `bots.decide.w_strike_my_owner_closing` | -0.262473 |
| `bots.decide.w_strike_rival_owner_closing` | -0.405657 |
| `bots.decide.bias_dodge` | 0.586668 |
| `bots.decide.w_dodge_dist` | 0.553754 |
| `bots.decide.w_dodge_in_reach` | -1.710141 |
| `bots.decide.w_dodge_my_owner_speed` | -0.726757 |
| `bots.decide.w_dodge_rival_owner_speed` | 0.370372 |
| `bots.decide.w_dodge_threat` | 0.358587 |
| `bots.decide.w_dodge_cover` | 2.0 |
| `bots.decide.w_dodge_my_hp` | 1.863414 |
| `bots.decide.w_dodge_rival_hp` | -0.461096 |
| `bots.decide.w_dodge_my_speed` | 0.050935 |
| `bots.decide.w_dodge_round_time` | -0.122565 |
| `bots.decide.w_dodge_my_owner_closing` | -0.552105 |
| `bots.decide.w_dodge_rival_owner_closing` | -0.711215 |
| `bots.decide.bias_brake` | -0.741796 |
| `bots.decide.w_brake_dist` | -0.037416 |
| `bots.decide.w_brake_in_reach` | 1.963378 |
| `bots.decide.w_brake_my_owner_speed` | -1.398034 |
| `bots.decide.w_brake_rival_owner_speed` | -2.0 |
| `bots.decide.w_brake_threat` | -1.804895 |
| `bots.decide.w_brake_cover` | -1.573784 |
| `bots.decide.w_brake_my_hp` | 0.642433 |
| `bots.decide.w_brake_rival_hp` | 0.236682 |
| `bots.decide.w_brake_my_speed` | 0.911067 |
| `bots.decide.w_brake_round_time` | 0.542165 |
| `bots.decide.w_brake_my_owner_closing` | 0.920027 |
| `bots.decide.w_brake_rival_owner_closing` | 0.077582 |
| `bots.skills.spin.ring_m` | 8.0 |
| `bots.skills.spin.lead_deg` | 5.0 |
| `bots.skills.spin.target_speed_mps` | 8.287966 |
| `bots.skills.move.clearance_m` | 1.103093 |
| `bots.skills.move.wide_k` | 1.223589 |
| `bots.skills.brake.stop_gain_s` | 0.104994 |

## Разбор оценки чемпиона

```json
{
  "dealt_in_brake": 0,
  "dealt_in_dodge": 0,
  "dealt_in_move": 0,
  "dealt_in_spin": 0.514286,
  "dealt_in_strike": 0.485714,
  "duel_smart": 0.333333,
  "pick_brake": 0.010093,
  "pick_dodge": 0.156988,
  "pick_move": 0,
  "pick_spin": 0.376248,
  "pick_strike": 0.456672,
  "taken_in_brake": 0,
  "taken_in_dodge": 0.029412,
  "taken_in_move": 0,
  "taken_in_spin": 0.588235,
  "taken_in_strike": 0.382353,
  "winrate_smart": 0.416667
}
```
