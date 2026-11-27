# E2a — Определение активной пары YSC в GTA V Enhanced `2.0.1158.16`

Настоящий документ фиксирует результаты этапа **E2a** по установлению того, какие именно файлы `appinternet` и `stock_controller` (`*1.ysc` или `*2.ysc`) исполняет **GTA V Enhanced `2.0.1158.16`**, откуда они загружаются, каковы возможности read-only runtime-верификации через SHVDNE `v1.1.0.6` / ScriptHookV, и подтверждает применимость выводов этапа E2 к активной паре скриптов.

> **Ограничения этапа E2a соблюдены:** ни один файл `game-scripts/*.ysc` не изменён, бинарные патчи, инъекции, записи в глобалы, перехваты нативов и изменения сохранений не выполнялись.

---

## 1. Итоговый вывод: активная пара скриптов

**Подтверждённая активная пара в GTA V Enhanced `2.0.1158.16`:**
- **`game-scripts/appinternet1.ysc`** (из `update\update2.rpf\x64\levels\gta5\script\script_rel.rpf\appinternet.ysc`)
- **`game-scripts/stock_controller1.ysc`** (из `update\update2.rpf\x64\levels\gta5\script\script_rel.rpf\stock_controller.ysc`)

**Неактивная (перекрываемая обновлением) базовая пара:**
- **`game-scripts/appinternet2.ysc`** и **`game-scripts/stock_controller2.ysc`** — оригинальные базовые PC-скрипты эпохи релиза 2015 г. (`1.27` / PC Day-1) из `x64b.rpf\levels\gta5\script\script_rel.rpf\`. В среде Enhanced `2.0.1158.16` их адреса глобалов (`53868`, `53869`, `66227`, `67010`) **не совпадают** с адресами реального рантайма.

Важно: суффиксы `*1` и `*2` в именах файлов внутри `game-scripts/` — **не** доказательство приоритета сами по себе (в инструментах вроде CodeWalker `ExtractScripts` суффиксы возникают из-за дедупликации одинаковых имён при выгрузке из нескольких RPF-архивов). Активная пара доказана тремя независимыми способами: (1) архивной иерархией Enhanced, (2) сигнатурой сборки/таблицей нативов/строками, (3) прямым сопоставлением адресов глобалов с живыми наблюдениями E1 в процессе `GTA5_Enhanced.exe` `2.0.1158.16`.

---

## 2. Задача 1 — Сравнение 4 YSC и приоритет `x64b.rpf` vs `update2.rpf`

### 2.1. Метаданные и маркеры сборки всех 4 файлов

| Свойство | `stock_controller1.ysc` | `appinternet1.ysc` | `stock_controller2.ysc` | `appinternet2.ysc` |
|---|---:|---:|---:|---:|
| **Размер, байт** | `18 204` | `1 379 054` | `17 655` | `171 377` |
| **SHA256** | `89e833f89eff1772833643067d282e7f28514651bd996efab789f4c583a7b846` | `dc24bf0851d41981148e9b37f425536512074c70a47b7d6475e4c31f9ee2d473` | `15d6d081840cfa00ab5a0ad739e4a6457ec2b1f45b4b01a0e945d368baac24a4` | `dbf2347a39ea68d0315ffa6acc9559389ca61b5efa00f3b88884e46d46650be2` |
| **RSC7 version** | `12` (`0x0C`) | `12` (`0x0C`) | `10` (`0x0A`) | `10` (`0x0A`) |
| **`sys_flags` / `sys_size`** | `0x880` / `49 152` | `0xd20002` / `3 440 640` | `0x880` / `49 152` | `0x4b880` / `425 984` |
| **`script_name` (`0x60`)** | `"stock_controller"` | `"appInternet"` (заглавная `I`) | `"stock_controller"` | `"appinternet"` (строчная `i`) |
| **`name_hash` (`0x58`)** | `0xc9833b07` | `0x6a172273` | `0xc9833b07` | `0x6a172273` |
| **`globals_signature`** | `0xa2be51ac` (`-1564585556`) | `0xa2be51ac` (`-1564585556`) | `0x86ef03cb` (`-2031156277`) | `0x86ef03cb` (`-2031156277`) |
| **Слово `0x00` (vptr dump)** | `0x00007ff65ec0ecc0` | `0x00007ff69c3eecc0` | `0x00000001406b8820` | `0x00000001406b8820` |
| **`code_size` / `statics`** | `38 622` / `51` | `3 242 324` / `2 189` | `37 725` / `31` | `351 139` / `2 638` |
| **Функций / инструкций** | `109` / `15 665` | `9 777` / `1 120 191` | `96` / `15 289` | `671` / `122 819` |
| **Таблица опкодов** | `v2` (`131`, `0..130`) | `v2` (`131`, `0..130`) | `v1` (`128`, `0..127`) | `v1` (`128`, `0..127`) |
| **`natives_count`** | `75` (`75/75` ротир. crossmap) | `1 315` (`1315/1315` ротир. crossmap) | `70` (`70/70` Day-1/1.27 хэши) | `440` (`440/440` Day-1/1.27 хэши) |
| **`string_size` (кол-во)** | `2 979` (`254`) | `161 855` (`10 879`) | `2 979` (`254`) | `42 526` (`3 556`) |
| **Gen9-маркеры в строках** | нет | есть `"SET_GEN9"` | нет | нет |
| **Архив-источник** | `update\update2.rpf` | `update\update2.rpf` | `x64b.rpf` | `x64b.rpf` |

### 2.2. Исследование структуры архивов и приоритета `x64b.rpf` vs `update2.rpf`

По результатам анализа открытых инструментов и каталогов GTA V Enhanced (`SSStuart/gta5files` `Tool/RawFIlesList/RGL_Enhanced.txt`, `citizenfx/fivem` `BaseGameRpfHeaderHashes.h` & `UpdateRpfHeaderHashes.h`, `dexyfex/CodeWalker` `GameFileCache.cs` / `RpfFile.cs` / `ExtractScriptsForm.cs`, `peter-w07/los-santos-reskate` `ScriptScan.cs`, `memorx/gta-web-local` `v/extract.py`):

1. **Оба архива присутствуют в установке GTA V Enhanced:** `RGL_Enhanced.txt` фиксирует наличие в корне игры `x64b.rpf`, `update\update.rpf` и `update\update2.rpf`.
2. **Точные внутренние пути RPF:**
   - Базовый архив (`x64b.rpf`): `x64b.rpf\levels\gta5\script\script_rel.rpf\{appinternet.ysc, stock_controller.ysc}` (игровой релиз 2015 г. / Day-1 PC, RSC7 `v10`, без title-update патчей).
   - Архив обновления (`update\update2.rpf`): `update\update2.rpf\x64\levels\gta5\script\script_rel.rpf\{appinternet.ysc, stock_controller.ysc}` (введён в GTA V начиная со сборки `2612` для патч-скриптов и перенесён в Enhanced как актуальный контейнер `script_rel.rpf`, RSC7 `v12`).
3. **Иерархия переопределения (override priority):**
   - Как подтверждает `ScriptScan.FindScriptArchive` (`los-santos-reskate`) и `v/extract.py` (`gta-web-local`), RAGE монтирует `update\update2.rpf` поверх `update\update.rpf` и базовых `x64*.rpf`:
     $$\text{update\textbackslash update2.rpf} \;(\text{приоритет } 3) \;>\; \text{update\textbackslash update.rpf} \;(\text{приоритет } 2) \;>\; \text{x64b.rpf} \;(\text{приоритет } 1).$$
   - Именно поэтому при выгрузке скриптов из всех RPF без разбора папок оба файла (`update2.rpf` и `x64b.rpf`) попадают в один каталог с дедупликацией имён (`*1.ysc` и `*2.ysc`).
4. **Независимое подтверждение по таблицам нативов и строкам:**
   - `*2.ysc` использует неротированные хэши нативов эпохи PC Day-1 / 1.27 (например, `0x1077788e268557c2` для `NETWORK_IS_SIGNED_ONLINE` и `0xa921aa820c25702f` для `MISC::IS_BIT_SET`, который в более новых сборках заменён на опкод `130`), а 64-битное поле `0x00` в заголовке содержит не-ASLR базу `0x00000001406b8820` раннего `GTA5.exe` (`1.27`).
   - `*1.ysc` использует актуальные ротированные Enhanced-хэши нативов (`1315/1315` и `75/75` совпадают с `xmap_app1.json` / `xmap_stock1.json`), содержит строку `"SET_GEN9"` (Gen9 / Enhanced-ветка внутрисайтового контента, отсутствующая в `*2.ysc`) и в поле `0x00` имеет современные 64-битные адреса `0x00007ff6...`.

### 2.3. Прямая привязка к живому рантайму `2.0.1158.16` (измерения E1)

Сравнение адресов глобалов в байткоде с фактическими значениями, зафиксированными в ходе read-only эксперимента **E1** в `GTA5_Enhanced.exe` `2.0.1158.16`:

| Переменная в скриптах | Индекс в `*1.ysc` (`update2.rpf`) | Индекс в `*2.ysc` (`x64b.rpf`) | Наблюдение в живом рантайме E1 (`2.0.1158.16`) |
|---|---:|---:|---|
| Флаг init `stock_controller` | `Global_65064` | `Global_53868` | **`G65064: 1 -> 0`** при инициализации SP |
| Флаг блокировки BAWSAQ | `Global_65065` | `Global_53869` | **`G65065 = 1`** устойчиво в SP |
| Активная вкладка (`0=LCN, 1=BAWSAQ`) | `Global_77568` | `Global_66227` | **`G77568: 0 -> 1`** ровно при переходе на BAWSAQ |
| Внешний флаг гейта | `Global_80362` | `Global_67010` | **`G80362 = 0`** |
| **Ссылки на `[65064, 65065, 77568, 80362]` в байткоде** | **Есть в обоих `*1.ysc`** | **0 ссылок в обоих `*2.ysc`** | **Однозначно доказывает исполнение `*1.ysc`** |

Так как `appinternet2.ysc` и `stock_controller2.ysc` не содержат ни единого обращения к глобалам `65064`, `65065`, `77568` или `80362`, зафиксированные в E1 переходы `G65064 (1→0)` и `G77568 (0→1)` физически не могли исходить от пары `*2.ysc` и однозначно подтверждают исполнение пары **`*1.ysc`** (`appinternet1.ysc` + `stock_controller1.ysc`).

---

## 3. Задача 2 — Возможности SHVDNE `v1.1.0.6` / ScriptHookV и read-only диагностика потоков

### 3.1. Аудит штатного API `SHVDNE v1.1.0.6` и `ScriptHookV.dll`

Проверка исходного кода `Chiheb-Bacha/ScriptHookVDotNetEnhanced` (`v1.1.0.6`, файлы `NativeMemory.cs`, `Game.cs`, `NativeHashes.cs`) показывает:

1. **Что `ScriptHookV.dll` и SHVDNE НЕ предоставляют:**
   - `ScriptHookV.dll` экспортирует только `getGameVersion`, `getGlobalPtr`, `createTexture`, `drawTexture`, а также базовую регистрацию/вызов нативов (`scriptRegister`, `nativeInit`, `nativeCall`, и т.д.).
   - В SHVDNE `v1.1.0.6` **нет** управляемого класса-обёртки для внутренних структур движка `scrThread` или `scrProgram` — поэтому через штатный API нельзя напрямую прочитать `scrProgram->GlobalsSignature`, `scrProgram->CodeSize` или RPF-путь загруженного `.ysc` без сканирования памяти.
2. **Что штатный API SHVDNE `v1.1.0.6` ПРЕДОСТАВЛЯЕТ (через `GTA.Native.Function.Call` и `GTA.Native.Hash`):**
   - `SCRIPT::SCRIPT_THREAD_ITERATOR_RESET` (`0xDADFADA5A20143A8`)
   - `SCRIPT::SCRIPT_THREAD_ITERATOR_GET_NEXT_THREAD_ID` (`0x30B4FA1C82DD4B9F`)
   - `SCRIPT::GET_NAME_OF_SCRIPT_WITH_THIS_ID` (`0x05A42BA9FC8DA96B`) — возвращает строковое имя потока скрипта
   - `SCRIPT::IS_THREAD_ACTIVE` (`0x46E9AE36D8FA6417`)
   - `SCRIPT::GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH` (`0x2C83A9DA6BFFC4F9`)
   - `SCRIPT::DOES_SCRIPT_WITH_NAME_HASH_EXIST` (`0xF86AA3C56BA31381`) / `HAS_SCRIPT_WITH_NAME_HASH_LOADED` (`0x5F0F0C783EB16C04`)
   - `GlobalVariable.Get(idx).Read<int>()` (через `getGlobalPtr`)

### 3.2. Достаточность штатного API для `appinternet` и `stock_controller`

Так как штатные нативы уже позволяют перечислить все активные потоки (без какого-либо memory scanner):
- **Для `appinternet`:** в заголовках YSC `appinternet1.ysc` имеет `script_name = "appInternet"` (заглавная **`I`**), а `appinternet2.ysc` — `"appinternet"` (строчная **`i`**), при одинаковом JOAAT-хэше `0x6a172273` (так как JOAAT в RAGE нечувствителен к регистру). `GET_NAME_OF_SCRIPT_WITH_THIS_ID` возвращает имя активного потока, а совместная проверка с глобалами `G65064/G65065/G77568` против `G53868/G53869/G66227` полностью устраняет неоднозначность даже независимо от регистра.
- **Для `stock_controller`:** оба файла (`stock_controller1.ysc` и `stock_controller2.ysc`) имеют идентичное имя `"stock_controller"` и хэш `0xc9833b07`. Поэтому один только `GET_NAME_OF_SCRIPT_WITH_THIS_ID` доказывает, что поток `stock_controller` запущен, а **различение `*1` vs `*2` в штатном read-only API** обеспечивается перекрёстной проверкой непересекающихся глобалов: `*1` (`G65064/G65065/G65066`) против `*2` (`G53868/G53869/G53870`) — которая уже подтвердила `*1` в эксперименте E1.
- **Вывод по memory scanner:** собственный memory scanner **не требуется и не создавался**, так как комбинация штатного итератора потоков + read-only чтения глобалов однозначно решает задачу.

### 3.3. Подготовленный отдельный read-only инструмент: `tools/bawsaq-e2a-probe/`

Для фиксации перечня активных потоков в одиночной игре подготовлен отдельный C#-скрипт `tools/bawsaq-e2a-probe/BawsaqE2aThreadProbe.cs` (и workflow `.github/workflows/bawsaq-e2a-probe.yml`, сборка под SHVDNE `v1.1.0.6`, C# 5 совместимость, горячая клавиша `F10`):
- **Строго read-only:** использует только итератор потоков (`SCRIPT_THREAD_ITERATOR_*`, `GET_NAME_OF_SCRIPT_WITH_THIS_ID`, `IS_THREAD_ACTIVE`, `GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH`, `DOES_SCRIPT_WITH_NAME_HASH_EXIST`, `HAS_SCRIPT_WITH_NAME_HASH_LOADED`) и `GlobalVariable.Get(...).Read<int>()`. Никогда не меняет память, не останавливает/не запускает скрипты и не пишет глобалы.
- **Логирует в `bawsaq-e2a-thread-probe.log`:**
  - точные `tid`, точное строковое имя (`"appInternet"` vs `"appinternet"` и `"stock_controller"`) и `active` при старте, при изменениях и при нажатии `F10` (полный список всех активных потоков);
  - счётчики потоков по хэшам `0x6a172273` (`appinternet`) и `0xc9833b07` (`stock_controller`);
  - одновременный срез `*1`-глобалов (`80362, 65064, 65065, 65066, 65068, 77568`) и `*2`-глобалов (`67010, 53868, 53869, 53870, 53872, 66227`).

> **Примечание о текущем окружении:** в Linux-песочнице без установленной Windows/GTA V Enhanced `BawsaqE2aThreadProbe` подготовлен для запуска в игре на стороне пользователя (как и инструмент E1); одновременно факт исполнения пары `*1.ysc` уже подтверждён совокупностью живых логов E1 и несовместимых сигнатур глобалов (см. §2.3).

---

## 4. Задача 5 — Повторная верификация ключевых выводов E2 на подтверждённой активной паре (`*1.ysc`)

После подтверждения того, что активной парой является **`appinternet1.ysc` + `stock_controller1.ysc`**, все базовые выводы E2 (описанные в [`e2-data-flow.md`](e2-data-flow.md) и [`e2-architecture.md`](e2-architecture.md)) относятся именно к исполняемому коду игры и **подтверждены**:

| Проверка | Подтверждение на активной паре (`stock_controller1.ysc` + `appinternet1.ysc`) |
|---|---|
| **80 акций (`39 BAWSAQ + 41 LCN`)** | `stock1::func_107` (`0x60c0..0x96cf`) инициализирует `Global_57379[0..79 /*36*/]`: `41` LCN (`f_8=0`, `f_35=0..40`) и `39` BAWSAQ (`f_8=1`, `f_35=0..38` → `Global_60260[0..38 /*7*/]`). |
| **Роль `.f_9`** | Базовая котировка (`> 0.0` обязателен для попадания в `func_9475`, `func_9494`, `func_9498`, а также через `func_103` в `func_9493`); в офлайне остаётся `0.0` для `f_8=1`. |
| **Роль `.f_13` и `.f_14[0..15]`** | Заполняются только LCN (`stock1::func_29, 91`), но для обоих рынков читаются бегущей строкой (`app1::func_9475`) и High/Low на экране продажи (`app1::func_9483` → `func_9484/9485`). |
| **`stock1::func_4`, `func_5`, `func_107`** | `func_107` заполняет каталог и хэши статов; `func_4` опрашивает завершение `STAT_COMMUNITY_SYNCH_IS_PENDING()` и сбрасывает/ставит `Global_65065`; `func_5` загружает `STAT_GET_FLOAT` → `.f_9` и `STAT_COMMUNITY_GET_HISTORY(0..4)` → `.f_31..f_34` (только при `SIGNED_ONLINE && ROS`). |
| **`app1::func_9474`, `9475`, `9490`, `9493`, `9494`** | `func_9474` — гейт сайта (BAWSAQ при `G77568!=0`: `!SIGNED_ONLINE \|\| G80362 \|\| G65064 \|\| G65065 \|\| (G78678 && G78680)`) + хвост `G65066` → `func_9475`; `func_9490` — жёсткий гейт `SIGNED_ONLINE && ROS` для 6-точечного графика BAWSAQ; `func_9494` — список бумаг (`f_9 > 0 && f_8 == G77568`); `func_9493` — портфель (слоты `Global_114990.f_20573`, показ через `func_103(idx) > 0.0`). |
| **Сетевые проверки и локальность торговли** | `app1::func_53..58, 95, 97, 106, 110, 9755, 9756` не содержат сетевых проверок в SP (`!NETWORK_IS_GAME_IN_PROGRESS()`): покупка/продажа полностью локальны, если открыт UI и `.f_9 > 0.0`. |
| **3 уточнения E2 (зафиксированы в `e2-data-flow.md` и `e2-architecture.md`)** | (1) `Global_65066` — строго `ticker-refresh-needed` флаг, а не data-ready или таймер; (2) `func_9493` читает локально сохранённый портфель, но отображает строку только при `func_103(idx) > 0.0` (для BAWSAQ требуется `.f_9 > 0.0`); (3) разделено подтверждённое сохранение в `Global_114990.f_20573` и подтверждённое скрытие позиции в UI при `.f_9 == 0.0`. |
