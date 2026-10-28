# Архітектура

| Проєкт | Відповідальність |
| --- | --- |
| PTenoLauncher.Core | Моделі, правила профілів, контракт сховища |
| PTenoLauncher.Infrastructure | SQLite, файли, HTTP, DPAPI, auth, Minecraft, mods, backup |
| PTenoLauncher.Desktop | Avalonia MVVM, async commands, file pickers, композиція сервісів |
| PTenoLauncher.Tests | Ізольовані offline та headless UI тести |
| PTenoLauncher.IntegrationSmoke | Явні live тести Mojang та Vanilla installation |

Desktop → Infrastructure → Core. Core не залежить від UI, HTTP чи SQLite. Стороннього launcher executable немає.

## Offline-first

Старт виконує лише локальні читання. Каталог, auth та downloads — явні дії.
Offline блокує мережу на рівні сервісів. Прихованих connectivity probes немає.
Кешований каталог показує дату. Офлайн-гравець повинен мати раніше підтверджену Java Edition session у DPAPI.
Довільних username-only offline accounts немає.

## Дані

Стандартно: %LOCALAPPDATA%/PTenoLauncher. Portable: portable-data біля EXE за наявності portable.flag.
Аргумент --data-root ізолює тестування.

| Шлях | Дані |
| --- | --- |
| settings.db | Профілі, settings, history; без токенів |
| instances/{GUID}/game | mods, saves, resourcepacks, shaderpacks, config |
| instances/{GUID}/installation.json | Версія та перелік необхідних файлів із хешами |
| cache/metadata | Каталог і перевірені version manifests |
| cache/objects/sha1 | Спільні client/library JAR за hash |
| cache/assets | Asset indexes та об'єкти Mojang |
| cache/mods/sha256 | Копії локальних JAR |
| secrets/*.dpapi | MSAL cache та Minecraft session |
| backups | ZIP snapshots |
| logs | Обмежена історія launcher events |

Назва профілю не використовується як директорія. Перейменування не переміщує світи.
Імпортовані JAR копіюються в профіль без mutable hardlinks між профілями.
Архівування не видаляє дані. Restore створює інший GUID.

SQLite використовує schema version, WAL, FULL synchronous, транзакцію профілю та history.
JSON/DPAPI writes використовують staging, flush та атомарне переміщення.
Downloads використовують allowlist HTTPS, size/hash, .part та атомарне завершення.
Installation receipt записується після перевірки всіх файлів.
Symlink/junction у керованих шляхах відхиляються.

Сторонні plugins не завантажуються: реальної process isolation ще немає.
