# Отчёт: обнаружение завершения миссий Лестера в GTA V (ScriptHookVDotNet 3)

**Область:** Hotel Assassination, Multi-Target Assassination, Vice Assassination, Bus Assassination, Construction Assassination.
**Ограничение заказчика:** не использовать координаты и случайные UI-тексты — только штатные API/native-функции.
**Код проекта не изменялся** — отчёт является документом.

Метод: анализ исходников ScriptHookVDotNet 3 (клон репозитория), базы native-функций alloc8or (`natives.json`) и декомпилированных скриптов миссий (репозиторий YimMenu). Все формулировки разделены на **подтверждённые факты** (воспроизводимо по указанным источникам) и **предположения** (требуют проверки в игре).

---

## 1. Подтверждённые факты

### 1.1 ScriptHookVDotNet 3 — что доступно

- SHVDN 3 — ASI-плагин поверх C++ ScriptHookV; скрипты на любом .NET-языке, требования: **.NET Framework ≥ 4.8**, ScriptHookV, VC++ Redistributable ([README](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/README.md)).
- Стабильные сборки **v3.6.0 / v3.5.1 несовместимы с игрой v1.0.3258.0 и новее**; для новых версий игры нужен nightly **v3.6.0-nightly.89+** ([README](https://github.com/scripthookvdotnet/screpthookvdotnet/blob/main/README.md), [issue #1451](https://github.com/scripthookvdotnet/scripthookvdotnet/issues/1451)).
- Вызов native-функций: `GTA.Native.Function.Call<T>(Hash hash, params InputArgument[] args)` — исходник: [`source/scripting_v3/GTA.Native/Native.cs`](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/source/scripting_v3/GTA.Native/Native.cs) (класс `Function`, ~строка 476; класс `OutputArgument` для out-параметров — ~строка 316; неявные преобразования `InputArgument` для `int/uint/string/…`).
- Перечисление `GTA.Native.Hash` содержит все перечисленные ниже native-функции — проверено по локальному клону: [`source/scripting_v3/GTA.Native/NativeHashes.cs`](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/source/scripting_v3/GTA.Native/NativeHashes.cs) (номера строк из клона: 274–275, 2035–2036, 4591, 4633, 4829–4848, 5001–5003).
- Обёртка «идёт ли сейчас миссия»: свойство **`GTA.Game.IsMissionActive`** → `GET_MISSION_FLAG` / `SET_MISSION_FLAG` — [`source/scripting_v3/GTA/Game.cs`](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/source/scripting_v3/GTA/Game.cs) (~строки 284–290).
- Запись `Game.IsMissionActive = false` ненадёжна (баг в SHVDN, связан с очисткой флага при завершении скрипта-хоста) — [issue #1636](https://github.com/scripthookvdotnet/scripthookvdotnet/issues/1636). **Для детекта используется только чтение** — чтение штатно.
- Хеши строк: собственный код SHVDN использует `StringHash.AtStringHash("SP0_TOTAL_CASH")` ([`GTA/Player.cs`, свойство `Money`](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/source/scripting_v3/GTA/Player.cs)); старый `Game.GetHashKey` помечен `[Obsolete]` в пользу `StringHash.AtStringHash` ([`GTA/Game.Obsolete.cs`](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/source/scripting_v3/GTA/Game.Obsolete.cs)).
- Там же в `Player.Money` подтверждён образец чтения статов через unsafe-указатель:
  `unsafe { Function.Call(Hash.STAT_GET_INT, stat, &result, -1); }`.
- В исходниках SHVDN присутствует `SHVDN.NativeMemory.GetGlobalPtr(int index)` ([`source/core/Game/NativeMemory.cs`](https://github.com/scripthookvdotnet/scripthookvdotnet/blob/main/source/core/Game/NativeMemory.cs), класс объявлен `public`) — потенциальный доступ к глобалам скриптов; см. предположения в §2.

### 1.2 Внутренние имена скриптов миссий (подтверждено двумя независимыми источниками)

| Миссия | Имя скрипта (thread name) |
|---|---|
| Hotel Assassination | `assassin_valet` |
| Multi-Target Assassination | `assassin_multi` |
| Vice Assassination | `assassin_hooker` |
| Bus Assassination | `assassin_bus` |
| Construction Assassination | `assassin_construction` |

Источники: [Gogsi/GTAV-Research — «Scripts name detail.txt»](https://github.com/Gogsi/GTAV-Research/blob/main/Scripts%20name%20detail.txt), [YimMenu/GTA-V-Decompiled-Scripts — `all_script_names.txt`](https://github.com/YimMenu/GTA-V-Decompiled-Scripts/blob/master/all_script_names.txt) (там же существует `assassin_rankup` — не относится к пяти SP-миссиям), внутреннее имя `assassin_valet` для Hotel подтверждено и на [gta.wiki](https://gta.wiki/w/The_Hotel_Assassination).

### 1.3 Поведение скриптов миссий (по декомпилированным исходникам)

Репозиторий: [YimMenu/GTA-V-Decompiled-Scripts, `decompiled_scripts/`](https://github.com/YimMenu/GTA-V-Decompiled-Scripts/tree/master/decompiled_scripts). Номера строк — из скачанных копий; для других билдов игры могут отличаться.

Подтверждено для **всех пяти** скриптов:

1. **Старт миссии:** `MISC::SET_MISSION_FLAG(true)` в начале скрипта
   (`assassin_valet.c:1506`, `assassin_multi.c:2209`, `assassin_hooker.c:1981`, `assassin_bus.c:2250`, `assassin_construction.c:1513`).
2. **Экран «Mission Passed»:** на пути успеха вызывается
   `AUDIO::PLAY_MISSION_COMPLETE_AUDIO("FRANKLIN_BIG_01")`, и показ UI gated условием
   `AUDIO::IS_MISSION_COMPLETE_READY_FOR_UI()`:
   - `assassin_valet.c:7763/7766`, `assassin_multi.c:4054/4057`, `assassin_hooker.c:2149/2152`,
     `assassin_bus.c:2408/2411`, `assassin_construction.c:4742/4745`.
3. **Книговодство завершения:** после того как игрок закрывает экран passed, выполняется
   `Global_112922.f_19963++` и установка бита `2048` в `Global_112922.f_19963.f_1`
   (функция `func_197`, см. `assassin_valet.c:7798` и `func_197` → `func_541(..., 2048)`).
   Инкремент `Global_112922.f_19963++` присутствует во всех пяти скриптах; общий глобал
   `Global_112922.f_19963` (поля: `.f_1` — битовые флаги, `.f_5` — float) используется всеми пятью.
4. **Завершение потока:** общий cleanup-путь `func_534()` заканчивается
   `SCRIPT::TERMINATE_THIS_THREAD()` (`assassin_valet.c:26196–26234`; аналоги в остальных:
   `multi:26213`, `hooker:25441`, `bus:26871`, `construction:25089`).
5. **В cleanup есть несколько точек вызова** (в `assassin_valet` — строки 1504, 7803, 20875),
   т.е. поток завершается и на успехе, и на других путях (отказ/прерывание).
   Наличие отказных путей подтверждается `PLAYER::SPECIAL_ABILITY_CHARGE_ON_MISSION_FAILED(...)`
   во всех пяти скриптах.
6. **Статов, специфичных для конкретной миссии, в `assassin_valet` не найдено** — `STAT_SET/STAT_GET`
   используют только общие (`sp0/sp1/sp2_total_cash`, `money_made_from_missions`, оружие и т.п.).
   Для остальных четырёх скриптов глубокая проверка статов **не выполнялась** (см. §2).

### 1.4 Ключевые native-функции (хеши — из `natives.json`, alloc8or)

| Native | Хеш | Namespace / сигнатура | В `GTA.Native.Hash` (SHVDN) |
|---|---|---|---|
| `GET_MISSION_FLAG` | `0xA33CDCCDA663159E` | MISC · `BOOL GET_MISSION_FLAG()` | да (стр. 2036) |
| `SET_MISSION_FLAG` | `0xC4301E5121A0ED73` | MISC · `void SET_MISSION_FLAG(BOOL)` | да (стр. 2035) |
| `IS_MISSION_COMPLETE_READY_FOR_UI` | `0x6F259F82D873B8B8` | AUDIO · `BOOL ()` | да (стр. 275) |
| `IS_MISSION_COMPLETE_PLAYING` | `0x19A30C23F5827F8A` | AUDIO · `BOOL ()` | да (стр. 274) |
| `GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH` | `0x2C83A9DA6BFFC4F9` | SCRIPT · `int (Hash scriptHash)`; old names: `_GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT`, `_GET_NUMBER_OF_INSTANCES_OF_SCRIPT_WITH_NAME_HASH`, `_GET_NUMBER_OF_REFERENCES_OF_SCRIPT_WITH_NAME_HASH` | да (стр. 4842) |
| `IS_THREAD_ACTIVE` | `0x46E9AE36D8FA6417` | SCRIPT · `BOOL (int threadId)` | да (стр. 4836) |
| `GET_NAME_OF_SCRIPT_WITH_THIS_ID` | `0x05A42BA9FC8DA96B` | SCRIPT · `const char* (int threadId)`; old: `_GET_THREAD_NAME`, `_GET_NAME_OF_THREAD` | да (стр. 4837) |
| `GET_ID_OF_THIS_THREAD` | `0xC30338E8088E2E21` | SCRIPT · `int ()` | да (стр. 4840) |
| `GET_THIS_SCRIPT_NAME` | `0x442E0A7EDE4A738A` | SCRIPT · `const char* ()` | не проверялось |
| `GET_HASH_OF_THIS_SCRIPT_NAME` | `0x8A1C8B1738FFE87E` | SCRIPT · `Hash ()`; old: `_GET_THIS_SCRIPT_HASH` | не проверялось |
| `TERMINATE_THIS_THREAD` | `0x1090044AD1DA76FA` | SCRIPT · `void ()` | да (стр. 4841) |
| `DOES_SCRIPT_EXIST` | `0xFC04745FBE67C19A` | SCRIPT · `BOOL (const char* scriptName)` | да (стр. 4830) |
| `HAS_SCRIPT_LOADED` | `0xE6CC9F3BA0FB9EF1` | SCRIPT · `BOOL (const char* scriptName)` | да (стр. 4829) |
| `HAS_SCRIPT_WITH_NAME_HASH_LOADED` | `0x5F0F0C783EB16C04` | SCRIPT · `BOOL (Hash)` | да (стр. 4833) |
| `FORCE_CLEANUP_FOR_ALL_THREADS_WITH_THIS_NAME` | `0x4C68DDDDF0097317` | PLAYER · `void (const char* name, int cleanupFlags)` | да (стр. 4633) |
| `CAN_PLAYER_START_MISSION` | `0xDE7465A27D403C06` | PLAYER · `BOOL (Player player)` | да (стр. 4591) |
| `GET_NUMBER_OF_EVENTS` | `0x5F92A689A06620AA` | SCRIPT · `int (int eventGroup)` | да (стр. 4845) |
| `GET_EVENT_AT_INDEX` | `0xD8F66A3A60C62153` | SCRIPT · `int (int, int)` | да (стр. 4847) |
| `GET_EVENT_DATA` | `0x2902843FCD2B2D79` | SCRIPT · `BOOL (int, int, Any*, int)` | да (стр. 4848) |
| `STAT_GET_INT` | `0x767FBC2AC802EF3D` | STATS · `BOOL (Hash, int*, int)` | да (стр. 5001) |
| `STAT_GET_BOOL` | `0x11B5E6D2AE73F48E` | STATS · `BOOL (Hash, BOOL*, Any)` | да (стр. 5003) |
| `STAT_SET_INT` | `0xB3271D7AB655B441` | STATS · `BOOL (Hash, int, BOOL)` | не проверялось |

Хеши и сигнатуры — из [`alloc8or/gta5-nativedb-data`, `natives.json`](https://github.com/alloc8or/gta5-nativedb-data) (6439 native-функций; зеркало-сайт: [alloc8or.re/gta5/nativedb](https://alloc8or.re/gta5/nativedb/)). Наличие в SHVDN — по grep в `NativeHashes.cs` локального клона (см. §1.1).

Отдельно подтверждено по базе LCPDFR (описания): [`IS_MISSION_COMPLETE_PLAYING` / `IS_MISSION_COMPLETE_READY_FOR_UI`](https://www.lcpdfr.com/resources/nativedb/index/audio/page/5/), [`GET_MISSION_FLAG` / `SET_MISSION_FLAG`](https://www.lcpdfr.com/resources/nativedb/index/misc/).

---

## 2. Предположения (не проверено в игре)

1. **`GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(joaat("assassin_valet")) > 0` означает «миссия сейчас выполняется».** Опирается на старое имя натива (`_GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT`) и на то, что миссии — отдельные скрипт-потоки. Требуется runtime-проверка (в т.ч. что натив считает именно SP-миссионные потоки, а не только стримовые скрипты).
2. **`IS_MISSION_COMPLETE_READY_FOR_UI()` становится `true` именно во время passed-экрана любой миссии.** Сигнатура без параметров ⇒ натив глобальный (не различает миссии); в декомпилированных скриптах он используется именно как gate passed-UI. Семантика `IS_MISSION_COMPLETE_PLAYING` (вероятно, «играет mission-complete аудио») не документирована в используемых источниках.
3. **Интерпретация `Global_112922.f_19963`:** счётчик завершённых убийств (инкремент на passed) + битовые флаги состояния (`.f_1`, бит `2048` = успешное завершение). Сам факт чтений/записей подтверждён, **семантика и адресация** (как декомпилированное имя отображается на абсолютный индекс глобала для `GetGlobalPtr`) — нет.
4. **Пер-миссионных статов не существует** — проверено только на `assassin_valet` (§1.3 п.6); для четырёх оставшихся и для утверждения «вовсе нет» — нет.
5. **Event-подход** (`GET_NUMBER_OF_EVENTS` / `GET_EVENT_AT_INDEX` / `GET_EVENT_DATA`): нет данных о том, что завершение миссии порождает удобный script-event. Не проверялось.
6. **Доступность `SHVDN.NativeMemory.GetGlobalPtr` из пользовательского скрипта** (какая сборка/namespace видна при референсе на `ScriptHookVDotNet3.dll`) — не проверялось.
7. **Поведение на краевых случаях:** отказ миссии, выход из игры на passed-экране, replay миссии из паузы, сохранение/загрузка сейва — детально не проверялось. Известно только то, что cleanup-функция имеет несколько точек вызова (§1.3 п.5), т.е. исчезновение потока само по себе ≠ успех.
8. **`Tick` скрипта SHVDN** считается occurring каждый игровой кадр (стандартная семантика SHVDN; в данном исследовании не верифицировалась по исходникам).

---

## 3. Ограничения и риски

1. **Нет native «миссия X завершена».** Ни одна из 6439 функций не возвращает пер-миссионный флаг завершения; детект собирается из двух сигналов: (а) активность скрипт-потока миссии и (б) глобальное состояние passed-последовательности.
2. **`GET_MISSION_FLAG` / `Game.IsMissionActive` — только «какая-то миссия идёт»**, без указания какой; сам по себе недостаточен.
3. **`IS_MISSION_COMPLETE_*` — глобальные и кратковременные:** подходят только в связке с отслеживанием нужного потока в том же окне времени; опрос обязателен на каждом `Tick`.
4. **Исчезновение потока без passed-сигнала = отказ/прерывание,** не завершение — логика детекта обязана это разделять.
5. **После загрузки сейва** (миссия уже была выполнена ранее) ни поток не запущен, ни passed-сигнал не приходит — «ретро-детект» нативами невозможен. Варианты: собственное состояние в save мода (в репозитории уже есть `SaveManager`/`save.json`) либо чтение глобалов (§2 п.3, п.6 — непроверено).
6. **Replay миссии из паузы** повторно запустит скрипт и повторно покажет passed → нужны дедупликация/идемпотентность.
7. **Версионная совместимость:** SHVDN stable 3.6.0 сломан на игре ≥ v1.0.3258.0 → nightly ≥ v3.6.0-nightly.89 (§1.1); номера строк декомпилированных скриптов привязаны к билду, из которого они сделаны.
8. **Запись `Game.IsMissionActive = false` ненадёжна** ([issue #1636](https://github.com/scripthookvdotnet/scripthookvdotnet/issues/1636)) — не использовать SET-путь.
9. Детект **не** использует координаты и UI-тексты — только имена скриптов, native-состояние и потоки (требование выполнено).

---

## 4. Рекомендуемая схема детекта (state machine, опирается на §1)

Для каждой из пяти строк `name_i` на каждом `Tick`:

```
running_i  := GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(joaat(name_i)) > 0
passedNow  := IS_MISSION_COMPLETE_READY_FOR_UI()

if running_i and passedNow: seenPassed_i := true          // экран passed внутри окна жизни потока i

if prevRunning_i and not running_i:                       // поток i только что завершился
    if seenPassed_i:  → событие «миссия name_i ЗАВЕРШЕНА (успех)»
    else:             → поток завершён без passed (отказ/прерывание) — НЕ считать завершённой
    seenPassed_i := false
prevRunning_i := running_i
```

Почему это коррелирует с игрой (по §1.3): passed-экран показывается **внутри** жизни потока (`IS_MISSION_COMPLETE_READY_FOR_UI` — gate), поток вызывает `TERMINATE_THIS_THREAD` **после** закрытия passed-экрана ⇒ окно «поток жив && passed=true» существенно и ловится опросом.

Коарс-фолбэк (недостаточен сам по себе): переход `Game.IsMissionActive` true→false одновременно с `IS_MISSION_COMPLETE_READY_FOR_UI` даёт «какая-то миссия завершена», но не говорит какая — требует корреляции с одним из пяти имён потоков.

Долгосрочное состояние («выполнена ли миссия до текущей сессии») нативами не даётся — хранить в save мода (§3 п.5).

---

## 5. Минимальный пример для ScriptHookVDotNet 3

Скомпилированный скрипт (assembly C#, .NET Framework 4.8, референс `ScriptHookVDotNet3.dll`). Используются только API, подтверждённые в §1.

```csharp
using System;
using GTA;
using GTA.Native;

public class LesterAssassinationDetector : Script
{
    // Внутренние имена скриптов (§1.2)
    private static readonly string[] ScriptNames =
    {
        "assassin_valet",          // Hotel Assassination
        "assassin_multi",          // Multi-Target Assassination
        "assassin_hooker",         // Vice Assassination
        "assassin_bus",            // Bus Assassination
        "assassin_construction"    // Construction Assassination
    };

    private readonly uint[] _hashes;
    private readonly bool[] _prevRunning;
    private readonly bool[] _seenPassed;

    public LesterAssassinationDetector()
    {
        _hashes = new uint[ScriptNames.Length];
        _prevRunning = new bool[ScriptNames.Length];
        _seenPassed = new bool[ScriptNames.Length];

        for (int i = 0; i < ScriptNames.Length; i++)
        {
            // Тот же алгоритм, что у GTA GetHashKey; в SHVDN — StringHash.AtStringHash
            // (используется самим SHVDN, см. GTA/Player.cs). Если IDE не находит тип —
            // смотрите namespace GTA.Native / GTA.
            _hashes[i] = StringHash.AtStringHash(ScriptNames[i]);
        }

        Tick += OnTick;
    }

    private void OnTick(object sender, EventArgs e)
    {
        // Глобальное состояние passed-последовательности (AUDIO, без параметров)
        bool passedNow = Function.Call<bool>(Hash.IS_MISSION_COMPLETE_READY_FOR_UI);

        for (int i = 0; i < ScriptNames.Length; i++)
        {
            // Сколько потоков с именем миссии сейчас запущено
            int instances = Function.Call<int>(
                Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,
                _hashes[i]);

            bool running = instances > 0;

            if (running && passedNow)
                _seenPassed[i] = true;

            if (_prevRunning[i] && !running)
            {
                if (_seenPassed[i])
                    OnAssassinationCompleted(ScriptNames[i]);   // успех
                // иначе — отказ/прерывание: не считаем завершённой

                _seenPassed[i] = false;
            }

            _prevRunning[i] = running;
        }
    }

    private void OnAssassinationCompleted(string scriptName)
    {
        // Точка интеграции с BAWSAQ-движком (триггер рыночного события и т.п.)
        GTA.UI.Screen.ShowSubtitle("Assassination completed: " + scriptName, 4000);
    }
}
```

Дополнительные полезные вызовы:

```csharp
// «Сейчас идёт какая-то миссия» (глобальный флаг, без указания какой):
bool anyMission = Game.IsMissionActive;               // GET_MISSION_FLAG

// Чтение стата с out-параметром — паттерн из самого SHVDN (GTA/Player.cs):
unsafe
{
    int value;
    uint stat = StringHash.AtStringHash("sp1_total_cash");
    Function.Call(Hash.STAT_GET_INT, stat, &value, -1);
}
```

**Проверка гипотезы до продакшена** (см. §2 п.1): на время выполнения любой из пяти миссий вывести в лог `instances` для всех пяти хешей — должен быть `1` ровно у текущей и `0` у остальных; затем убедиться, что `IS_MISSION_COMPLETE_READY_FOR_UI()` возвращает `true` в окне между `PLAY_MISSION_COMPLETE_AUDIO("FRANKLIN_BIG_01")` и закрытием passed-экрана, а после `TERMINATE_THIS_THREAD` — снова `false`.

---

## 6. Источники

1. ScriptHookVDotNet 3 (исходники, README, issues): https://github.com/scripthookvdotnet/scripthookvdotnet
   - `source/scripting_v3/GTA/Game.cs` — `Game.IsMissionActive`
   - `source/scripting_v3/GTA.Native/Native.cs` — `Function.Call`, `InputArgument`, `OutputArgument`
   - `source/scripting_v3/GTA.Native/NativeHashes.cs` — enum `Hash`
   - `source/scripting_v3/GTA/Player.cs` — образец `STAT_GET_INT` и `StringHash.AtStringHash`
   - issue #1451 — совместимость версий игры/SHVDN; issue #1636 — семантика `SET_MISSION_FLAG`
2. База native-функций (хеши, сигнатуры, old names): https://github.com/alloc8or/gta5-nativedb-data (`natives.json`); сайт: https://alloc8or.re/gta5/nativedb/
3. Описания native: https://www.lcpdfr.com/resources/nativedb/index/audio/page/5/ , https://www.lcpdfr.com/resources/nativedb/index/misc/
4. Декомпилированные скрипты GTA V: https://github.com/YimMenu/GTA-V-Decompiled-Scripts
   - `decompiled_scripts/assassin_{valet,multi,hooker,bus,construction}.c`
   - `all_script_names.txt`
5. Имена миссий/скриптов: https://github.com/Gogsi/GTAV-Research/blob/main/Scripts%20name%20detail.txt
6. Внутреннее имя `assassin_valet`: https://gta.wiki/w/The_Hotel_Assassination
7. Список миссий: https://gta.fandom.com/wiki/Missions_in_GTA_V
