# PTenoLauncher — результати домашньої перевірки

Заповнюється користувачем після фактичних дій. PASS / FAIL / BLOCKED / NOT RUN. Початкові значення не є результатами перевірки.

Дата: 2026-10-27 (вхід); виправлення коду й автоматичні тести завершено 2026-10-28; ручний повтор очікується
Windows / масштаб екрана: __
Архів або Setup / SHA256 (якщо перевірено): __
Minecraft / Fabric / Java: __

| Перевірка | Статус | Що сталося / повідомлення |
| --- | --- | --- |
| Portable відкривається без окремої інсталяції .NET | NOT RUN | |
| Теми, мова, збереження налаштувань; AI вимкнено | NOT RUN | |
| Vanilla приховує Моди; Fabric показує | NOT RUN | |
| Каталог оновлюється й показує snapshots | NOT RUN | |
| Власна реєстрація PTenoLauncher / personal accounts / http://localhost | PASS | Підтверджено скріншотами користувача; Client ID збережено в лаунчері |
| Браузерний етап Microsoft OAuth | PASS | Користувач отримав Authentication complete; це не перевірка ліцензії Minecraft |
| Власний Client ID і повний доступ сервісів | BLOCKED | Microsoft-етап виконано; Xbox відхилив наступний запит |
| Офіційний Microsoft-вхід, ліцензія, UUID/скін | FAIL | user.auth.xboxlive.com повернув HTTP 400; акаунт у лаунчері не додано |
| Повторний вхід зі збіркою auth-fix | NOT RUN | Після виправлення JSON/header потрібна повторна перевірка користувачем |
| Vanilla: головне меню та новий тестовий світ | NOT RUN | |
| Штатне завершення / консоль / exit code | NOT RUN | |
| Дозволений офлайн-запуск і збереження світу | NOT RUN | |
| Fabric: установлення й реальна гра | NOT RUN | |
| Quilt: каталог / встановлення / запуск без модів | NOT RUN | |
| Modrinth: Reese’s Sodium Options + Sodium | NOT RUN | |
| Необов’язкові компоненти: вибір, застосування, конфлікт | NOT RUN | |
| Перейти до теки: правильна папка кожного профілю | NOT RUN | |
| Локальні JAR: множинний вибір / перетягування | NOT RUN | |
| Повторне відкриття / повторний онлайн-запуск | NOT RUN | |
| Оновлення прострочених токенів (окремо) | NOT RUN | |
| Вихід із вибраного акаунта | NOT RUN | |
| Другий власний акаунт, якщо є | NOT RUN | |
| Setup: мови, папка, ярлики, запис у Windows | NOT RUN | |
| Повторне встановлення зберігає тестові дані | NOT RUN | |
| Оновлення 0.1.0 → 0.2.0, якщо є тестова 0.1.0 | NOT RUN | |
| Звичайне видалення зберігає тестові дані | NOT RUN | |
| Явне очищення лише тестових даних | NOT RUN | |

Повідомлення / кроки відтворення: __
Скріншоти без секретів: __
Пароль, MFA-коди, токени та файли .dpapi не додавати.


## Latest observed authentication retest - 2026-10-28

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used.

Browser OAuth: PASS. Full Minecraft sign-in: FAIL/BLOCKED, downstream Minecraft Services HTTP 403. Previous Xbox HTTP 400 is retained as historical evidence. No profile nickname/UUID/skin, entitlement, refresh or gameplay PASS is recorded.
