# Issue #21 (итерация B): инструкция профилирования кучи на Windows

Цель: найти держателей живых объектов после 30 переключений вкладок «Все»↔«Избранное»
(рост `heapAfterGcMb` без возврата при GC поколения 2). Диагностика v2 (1.0.0.36) добавляет
в лог `allocMb` и счётчики строк `rowsRealized/rowsCleared`; дамп кучи ищет конкретные типы.

---

## 1. Диагностическая сборка

Запустить сборку с флагами диагностики. Флаги включаются в `trace.json` каталога активного
профиля (Windows: `%APPDATA%\Workspace1C\profiles\<Id>\trace.json`, legacy-режим без профиля —
`%APPDATA%\Workspace1C\trace.json`). Файл перечитывается при изменении — править можно
на ходу, без перезапуска.

```json
{
  "version": 1,
  "CM_COLUMNS": false,
  "CM_MENUCLICK": false,
  "CM_MENUCLOSE": false,
  "CM_REDIRECT": false,
  "CM_TREESWITCH": true,
  "CM_TREESWITCH_FLUSH": false,
  "CM_REVEAL": false
}
```

Запасной способ включения (если флага нет в конфиге): переменная окружения
`CM_TREESWITCH=1`.

## 2. Протокол замеров (обязательно перед дампом)

1. Запустить приложение, открыть вкладку «Все» (600+ баз).
2. Сделать **30 полных переключений** «Все» → «Избранное» → «Все» (данные — в журнале
   приложения, записи `CM_TREESWITCH:`).
3. После 30-го переключения подождать 3–5 секунд (дать отработать отложенным вызовам
   и GC), НЕ кликать.
4. Снять дамп кучи (п. 4).

### 2а. Эксперимент B (гипотеза H2 — Recycling-пул)

- Прогон 1 (базовый): `CM_TREESWITCH_FLUSH: false` → измерить `heapAfterGcMb`.
- Прогон 2 (эксперимент B): `CM_TREESWITCH_FLUSH: true` (флаг активен только на Windows/WPF) →
  та же серия 30 переключений → сравнить `heapAfterGcMb`.
- Если при `CM_TREESWITCH_FLUSH=true` `heapAfterGcMb` перестаёт расти — рост был следствием
  отложенного освобождения Recycling-пула контейнеров (а не утечки подписок).
  В журнале при этом появляются записи `CM_TREESWITCH_FLUSH: Recycling-пул контейнеров сброшен`.

## 3. Что должны показать логи (критерии диагностики v2)

- `allocMb` — прирост `GC.GetTotalAllocatedBytes(false)` за смену вкладки (объём мусора,
  создаваемого одним переключением).
- `rowsRealized` / `rowsCleared` — реализованные/отсоединённые строки за смену
  (WPF-аналог счётчиков Avalonia). Устойчивый разрыв `rowsRealized` ≫ `rowsCleared`
  между сменами — признак удержания контейнеров.
- `heapMb`/`heapAfterGcMb` — замер с принудительным GC(2): постоянный рост в прогоне
  базовой ветки — реальное удержание, а не мусор.
- `collAdd/collRemove/collReset/resetMode` — без регресса «шквала» (ожидаем `reset`).

## 4. Дамп кучи (dotnet-gcdump)

Установка (один раз):

```powershell
dotnet tool install -g dotnet-gcdump
```

Проверить установку: `dotnet-gcdump --help`.

Снятие дампа (Windows, от имени пользователя, который запускал приложение):

```powershell
# PID процесса из диспетчера задач (вкладка «Подробности», Workspace1C.exe)
dotnet-gcdump collect -p <PID> -o dump_after30.gcdump
```

- Дамп **после старта и первичной загрузки** (до переключений) — `dump_base.gcdump`.
- Дамп **после 30 переключений** — `dump_after30.gcdump`.
- НЕ трогать приложение во время снятия дампа (сборка ~10–30 с).

> Примечание: приложение .NET (WPF, self-contained single-file) работает на CoreCLR и
> поддерживает EventPipe — dotnet-gcdump совместим с ним как с обычным процессом .NET.

## 5. Анализ дампа

Отчёт в HTML с топ-типами по размеру/количеству экземпляров:

```powershell
dotnet-gcdump report dump_after30.gcdump -o report_after30.html
```

Что искать (между `dump_base` и `dump_after30` должно быть различимо):

- `Workspace1C.Controls.LeveledTreeViewItem` (или базовый `System.Windows.Controls.TreeViewItem`)
  — их количество НЕ должно расти от переключений (панель виртуализирует строки).
  Рост = удержание контейнеров (Recycling-пул / подписки / кэш).
- `Workspace1C.ViewModels.GroupNodeViewModel`, `PinnedInfobaseItem`,
  `Workspace1C.Collections.ObservableRangeCollection<object>` — рост без инвалидации
  кэша означает утечку в VM-слое (гипотеза H5).
- `System.Windows.Controls.Grid` со строками шаблона (DataTemplate строк базы/группы) —
  рост без удержания DataContext указывает на отложенное освобождение (H2, ср. прогон с
  `CM_TREESWITCH_FLUSH=true`).
- Классы binding/closure: `System.Windows.Data.BindingExpression`,
  `Workspace1C.ViewModels.MainViewModel+<>c__DisplayClass…` — рост замыканий = демпферы
  Dispatcher (H4) не срабатывают.

Посмотреть, кто держит типы (в Visual Studio: «Просмотр графических объектов» → «Ссылки
на этот тип»; в PerfView/HeapView — сохранение путей).

## 6. Что прислать координатору

- Файл лога приложения (записи `CM_TREESWITCH:` за 30 переключений, оба прогона — с
  `CM_TREESWITCH_FLUSH=false` и `=true`).
- Дампы `dump_base.gcdump` и `dump_after30.gcdump` (или `report_after30.html` при большом
  размере).
- Вердикт по признакам п. 3/5: растут ли `heapAfterGcMb`, `rowsRealized-rowsCleared`,
  количество `LeveledTreeViewItem`.