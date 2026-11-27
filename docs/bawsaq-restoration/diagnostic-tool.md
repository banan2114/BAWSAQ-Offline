# BawsaqE1Diagnostics — диагностический SHVDN-плагин (этап E1)

Read-only плагин для **ScriptHookVDotNetEnhanced (SHVDNE)**, API v3, .NET Framework 4.8.
Логирует сетевые нативы и ключевые скриптовые глобалы BAWSAQ; **ничего не пишет** и не изменяет.

Источники: `tools/bawsaq-diagnostics/` (исходники), `.github/workflows/bawsaq-e1-diagnostics.yml` (CI-сборка), протокол — [`experiment-e1.md`](experiment-e1.md).

---

## 1. Результаты проверки API и совместимости (задача 1)

| Вопрос | Результат | Источник (проверено в сеансе) |
|---|---|---|
| Какая реализация SHVDN стоит у пользователя | **SHVDNE = `Chiheb-Bacha/ScriptHookVDotNetEnhanced`**, релиз **v1.1.0.6** (15.07.2026, asset `ScriptHookVDotNetEnhanced-v1.1.0.6.zip`). Лог пользователя `ScriptHookV .Net Enhanced 3.9.0.6 (1.1.0.6)` совпадает: `1.1.0.6` — версия релиза, `3.9.0.x` — версия сборки `ScriptHookVDotNet3.dll` | github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced (releases, README) |
| Доступен ли API v3 | **Да.** `AssemblyName=ScriptHookVDotNet3`, `TargetFramework=net48`, namespace `GTA` / `GTA.Native`; пользовательские скрипты наследуют `GTA.Script` напрямую и имеют public-конструктор без параметров | `source/scripting_v3/ScriptHookVDotNet_APIv3.csproj`, `GTA/Shvdn/Script/Script.cs` |
| `GTA.Native.GlobalVariable.Get()` | **Подтверждён.** `public struct GlobalVariable` в `namespace GTA.Native`; `public static GlobalVariable Get(int index)` → `SHVDN.NativeMemory.GetGlobalPtr(index)`; при несуществующем индексе — `IndexOutOfRangeException` | `source/scripting_v3/GTA.Native/Native.cs` (регион `Global Variables`) |
| `Read<T>()` | **Подтверждён.** `public unsafe T Read<T>()` (string — через UTF-8, примитивы — из адреса глобала). Существует и `Write<T>()` — **в проекте не используется** (запрет записи) | там же |
| Вызов нужных нативов | **Подтверждён.** `Function.Call<bool>(Hash...)`, `public enum Hash : ulong`. В SHVDNE присутствуют: `NETWORK_IS_SIGNED_ONLINE`, `NETWORK_HAS_VALID_ROS_CREDENTIALS`, `NETWORK_IS_SIGNED_IN`, `NETWORK_HAS_SOCIAL_CLUB_ACCOUNT` | `GTA.Native/Native.cs`, `GTA.Native/NativeHashes.cs` |
| ⚠️ Сырые хэши: SHVDNE vs YSC 1158 | Значения enum в SHVDNE: `NETWORK_IS_SIGNED_ONLINE = 0x1077788E268557C2`, `NETWORK_HAS_VALID_ROS_CREDENTIALS = 0x85443FF4C328F53B`. В натив-таблице YSC сборки 1158 эти же нативы: `0x261e3728ee56b3ac`, `0x1595d1b690089487`. **В плагине используются только именованные `Hash.*`; сырые legacy-константы не хардкодятся** (маппинг между изданиями — внутренняя ответственность SHVDNE) | `NativeHashes.cs` vs `game-scripts/appinternet1.ysc` (натив-таблица, `tools/ysc_disasm.py`) |
| Совместимость с GTA V Enhanced **1.0.1158.16** | **Подтверждена фактом работы:** SHVDNE v1.1.0.6 загружена в игре пользователя (его лог). Статически: README SHVDNE — «Legacy + Enhanced»; **`GameVersion`/`ExeVersionConsts` не содержат записи `1.0.1158.16`** (Enhanced в enum = `v2_0_811_8…v2_0_1013_33`, т.е. exe-версии 2.0.811.8–2.0.1013.33; в legacy-списке 1158 пропущен: 1103→1180). Плагин поэтому логирует `Game.FileVersion` (реальная exe-версия), `Game.Version` (enum/сырое число) и `Game.isEnhanced` — точное соответствие номеров зафиксирует сам эксперимент | `GTA/Game.cs` (строки FileVersion/Version/isEnhanced), `GTA/ExeVersionConsts.cs`, `GTA/GameVersion.cs` |
| Горячая клавиша | **Подтверждена.** Событие `Script.KeyDown` (`WinForms.KeyEventArgs`, `Keys.F9`); альтернатива `Game.IsKeyPressed(Keys)`. Подписка в конструкторе безопасна: `ScriptDomain.LookupScript` во время ctor возвращает `_executingScript` | `GTA/Shvdn/Script/Script.cs`, `source/core/ScriptDomain.cs` (`InstantiateScriptFast`, `LookupScript`) |
| Неподтверждённые смещения памяти | **Не используются.** Обращение к глобалам — только через публичный `GlobalVariable.Get` (внутри — официальный `GetGlobalPtr` SHVDN); к нативам — только через `Function.Call`. Никаких собственных паттернов/офсетов в проекте нет | исходники проекта (§2) |

## 2. Что делает плагин (и что категорически нет)

**Делает (по построению — только чтение):**
- `NETWORK_IS_SIGNED_ONLINE()`, `NETWORK_HAS_VALID_ROS_CREDENTIALS()` (+ два смежных для контекста: `NETWORK_IS_SIGNED_IN`, `NETWORK_HAS_SOCIAL_CLUB_ACCOUNT`);
- чтение глобалов `Global_80362, Global_65064, Global_65065, Global_65068, Global_77568` через `GlobalVariable.Get(i).Read<int>()`;
- **дополнительный канал улик** `Global_77542` (в `func_9474` в него кладётся `-1` на раннем error-пути `Global_65068`) — read-only, помечен как дополнительный;
- заголовок сессии: версия плагина, полное имя/версия `ScriptHookVDotNet3.dll`, CLR/ОС, `Game.FileVersion`, `Game.Version`, `Game.isEnhanced`, путь к игре, путь лога;
- запись **только при изменении** значений (`CHANGED`), всегда — ручной снимок (`F9` → `MANUAL`), плюс редкий `HEARTBEAT` (300 с) для подтверждения живости.

**Не делает (программно исключено):**
- не вызывает `GlobalVariable.Write` / не пишет в глобалы;
- не вызывает `STAT_SET_*` и любых других мутирующих нативов (только четыре `bool`-чтения);
- не перехватывает/не подменяет функции, не патчит память процесса;
- не изменяет YSC, не трогает архивы RPF, не меняет торговлю/портфель/сейвы;
- не детектит строку `WWW_BAWSAQ_COM_S_ERROR` автоматически (это визуальная фиксация в протоколе — см. ограничения).

## 3. Точные зависимости

| Компонент | Версия | Назначение |
|---|---|---|
| ScriptHookVDotNetEnhanced | **v1.1.0.6** (релиз) | рантайм в игре; референс-сборка для компиляции: `ScriptHookVDotNet3.dll` (версия сборки 3.9.0.x, в логе пользователя `3.9.0.6`) |
| Script Hook V (C++, Alexander Blade) | установлен у пользователя | загрузчик ASI (SHVDNE от него зависит) |
| .NET Framework | **4.8** (в системе уже есть — его требует сам SHVDNE) | рантайм плагина; для build.cmd — компилятор `csc.exe v4.0.30319` (C# 5) |
| Язык исходников | **C# 5** (`LangVersion=5`) | одновременно собирается dotnet SDK (CI) и системным csc (build.cmd) |
| Пакеты NuGet | **нет** | ни одной зависимости вне .NET Framework и SHVDNE |

## 4. Сборка

### Вариант A — GitHub Actions (основной, «готовый DLL»)
Workflow `.github/workflows/bawsaq-e1-diagnostics.yml` (срабатывает на push в `tools/bawsaq-diagnostics/**` или вручную):
1. качает `ScriptHookVDotNetEnhanced-v1.1.0.6.zip` (референс, в `lib/` — **не коммитится**, в `.gitignore`);
2. `dotnet build BawsaqE1Diagnostics.csproj -c Release` (windows-latest, targeting pack net48);
3. кладёт результат в **`tools/bawsaq-diagnostics/dist/`** + `SHA256SUMS.txt`, коммитит в ветку (`[skip ci]`), дублирует в Actions artifact `BawsaqE1Diagnostics`;
4. в логе сборки — `REF_SHA256=` (референс) и `OUT_SHA256=` (наш DLL).

### Вариант B — на машине пользователя (без SDK и интернета)
```bat
cd tools\bawsaq-diagnostics
build.cmd "C:\Path\To\GTA V\ScriptHookVDotNet3.dll"
```
Использует системный `csc.exe` (.NET Framework), референс — **установленный в игре** DLL той же версии SHVDNE. Результат: `dist\BawsaqE1Diagnostics.dll`.

## 5. Установка в игру

1. Убедиться, что SHVDNE **v1.1.0.6** загружена (строка в логе SHVDN).
2. Скопировать `BawsaqE1Diagnostics.dll` в **`<GTA V>\scripts\`** (папка `scripts` рядом с `GTA5.exe`; SHVDNE сам сканирует её на .dll с типом-наследником `GTA.Script`).
3. Ничего больше не менять: `.ini` SHVDN, YSC, архивы — не трогаем.

## 6. Использование

- Лог: **`<GTA V>\scripts\bawsaq-e1-diagnostics.log`** (если папка недоступна на запись — `%TEMP%\bawsaq-e1-diagnostics.log`; путь всегда указан в заголовке).
- **F9** — принудительный снимок (`MANUAL` пишется всегда, даже если значения не изменились; повторное нажатие <1 с игнорируется).
- Автозапись: `INITIAL` (первый снимок), `CHANGED` (любое изменение любого из значений), `HEARTBEAT` (каждые 5 минут — подтверждение, что плагин жив).
- Ошибки чтения попадают в строку как `G80362=ERR:...` / `signed_online=ERR:...` и не дублируются, пока не изменятся.

## 7. Пример ожидаемого лога

> Значения ниже — **иллюстрация формата и офлайн-ожиданий** (см. отчёт §5), а не результаты измерений. Фактические значения фиксирует эксперимент.

```
[2026-11-28 14:02:11.318] ==================== bawsaq-e1-diagnostics v1.0.0 ====================
[2026-11-28 14:02:11.320] mode=READ-ONLY (no global writes, no STAT_SET_*, no hooks, no memory patches, no YSC changes)
[2026-11-28 14:02:11.320] hotkeys: F9 = manual snapshot (values are always written, even if unchanged)
[2026-11-28 14:02:11.331] plugin=BawsaqE1Diagnostics, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
[2026-11-28 14:02:11.345] shvdn=ScriptHookVDotNet3, Version=3.9.0.6, Culture=neutral, PublicKeyToken=null
[2026-11-28 14:02:11.347] shvdn_location=C:\Games\GTA V\ScriptHookVDotNet3.dll
[2026-11-28 14:02:11.350] shvdn_file_version=3.9.0.6
[2026-11-28 14:02:11.350] shvdn_api=v3 (GTA.Script, GTA.Native.Function, GTA.Native.GlobalVariable)
[2026-11-28 14:02:11.352] runtime_clr=4.0.30319.42000 os=Microsoft Windows NT 10.0.26100.0 64bit=True utc=2026-11-28 12:02:11
[2026-11-28 14:02:11.380] game_file_version=1.0.1158.16
[2026-11-28 14:02:11.381] game_enum_version=v2_0_1013_33 raw=1009
[2026-11-28 14:02:11.382] game_is_enhanced=True
[2026-11-28 14:02:11.382] game_path=C:\Games\GTA V\
[2026-11-28 14:02:11.383] globals=G80362,G65064,G65065,G65068,G77568,G77542
[2026-11-28 14:02:11.383] natives=NETWORK_IS_SIGNED_ONLINE, NETWORK_HAS_VALID_ROS_CREDENTIALS, NETWORK_IS_SIGNED_IN, NETWORK_HAS_SOCIAL_CLUB_ACCOUNT (GTA.Native.Hash names)
[2026-11-28 14:02:11.383] log_path=C:\Games\GTA V\scripts\bawsaq-e1-diagnostics.log
[2026-11-28 14:02:11.383] ====================================================================
[2026-11-28 14:02:11.890] INITIAL signed_online=False ros=False signed_in=False sc_account=False G80362=0 G65064=0 G65065=1 G65068=0 G77568=0 G77542=0
[2026-11-28 14:03:02.114] CHANGED signed_online=False ros=False signed_in=False sc_account=False G80362=0 G65064=1 G65065=1 G65068=0 G77568=1 G77542=0
[2026-11-28 14:03:41.557] MANUAL signed_online=False ros=False signed_in=False sc_account=False G80362=0 G65064=1 G65065=1 G65068=0 G77568=1 G77542=0
[2026-11-28 14:08:41.601] HEARTBEAT ticks=603 last_snapshot=signed_online=False ros=False signed_in=False sc_account=False G80362=0 G65064=1 G65065=1 G65068=0 G77568=1 G77542=0
```

## 8. Ограничения и неподтверждённые возможности

1. **Автоматического детекта `WWW_BAWSAQ_COM_S_ERROR` нет** — строка рисуется scaleform'ом; плагин её не видит. Фиксация — визуально + `F9` в момент ошибки (протокол E1).
2. **Соответствие индексов глобалов рантайму проверяется впервые.** Индексы (80362, 65064, …) взяты из инструкций YSC 1158.16. Если `Get` вернёт `ERR:IndexOutOfRange` — это результат эксперимента (зафиксировать, не «чинить» подбором).
3. **Наблюдения `G77568/G65064/G77542` интерпретируются по статическому анализу** (`docs/bawsaq-restoration/report.md` §4–§5): ожидания заданы, но не измерены до E1.
4. **Разрыв версийных номеров не закрыт:** SHVDNE не знает сборку `1.0.1158.16` (enum Enhanced = 2.0.811.8–2.0.1013.33). Загрузка подтверждена фактом работы SHVDNE; числовое соответствие зафиксирует `game_file_version` в логе.
5. **Сырые хэши нативов SHVDNE ≠ хэши в YSC 1158** (таблица в §1). Плагин намеренно вызывает нативы по именам enum — корректность трансляции на данном издании игры обеспечивает SHVDNE; независимая перепроверка не выполнялась.
6. Плагин не пишет в глобалы/статистику/память **по построению** — но и не предоставляет функций управления: это осознанный предел этапа E1 (только диагностика).
7. Сценарий B требует ROS-авторизации в **одиночном режиме** (Social Club/Лаунчер); **GTA Online не требуется и не используется**.
8. Лог может не создаться, если папка `scripts` недоступна на запись (защищённая папка) — тогда `%TEMP%`; если и это недоступно, плагин молча продолжает работу без лога (не ломает игру).
9. Референс-сборка `ScriptHookVDotNet3.dll` не распространяется репозиторием (`.gitignore`); её SHA256 фиксируется в `dist/SHA256SUMS.txt` и в логе CI.
10. Сборка в песочнице исследования без Windows невозможна — «готовый DLL» производит CI (Вариант A) либо `build.cmd` на машине пользователя (Вариант B); оба варианта собирают одни и те же исходники.

## 9. Воспроизводимость

```bash
# Исходники и проект
ls tools/bawsaq-diagnostics/
# CI-сборка (после push):
gh run list --workflow bawsaq-e1-diagnostics.yml
gh run view <id> --log | grep -E 'REF_SHA256|OUT_SHA256'
git pull   # dist/BawsaqE1Diagnostics.dll + dist/SHA256SUMS.txt
# Локальная сборка (Windows):
tools\bawsaq-diagnostics\build.cmd "C:\Path\To\GTA V\ScriptHookVDotNet3.dll"
```
