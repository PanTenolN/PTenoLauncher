# Постійні правила продовження розробки PTenoLauncher

Дата: 2026-10-30. Джерело: пряме уточнення власника після накопичення тестових кешів. Додавати блок нижче до кожного наступного промпту продовження та перевірки, що запускає тести/збірки/E2E. Це документаційне правило; його запис не виконує очищення чи зміну продуктового коду.

## Обов'язковий блок для кожного промпту

**Тестові кеші й тимчасові дані:** перед роботою прочитай docs/CONTINUATION_RULES.md. Створюй тести/E2E лише в окремих позначених roots поточної операції, веди точний список створених шляхів і контролюй їхній розмір. Після кожного завершеного етапу, помилки, скасування та наприкінці сесії звільни handles/процеси й прибери саме свої disposable test caches, synthetic payloads, staging, дублікати build/package/source trees. Спочатку збережи компактні логи, TRX/JSON, hashes і потрібний мінімальний reproducer. Перед видаленням перевір абсолютний шлях, належність операції та відсутність links/reparse points; не видаляй загальні батьківські каталоги. Користувацькі profiles/worlds/accounts/translations/backups, продуктовий cache/Java, .tools/dotnet, .local/nuget, поточні Desktop/helper binaries, PDF/документи та докази тестів збережи. Невідомі/зайняті roots не видаляй і захист не обходь: зафіксуй точний залишок, розмір і причину. Наприкінці звітуй created/removed/retained sizes та підтверди збереження даних. Якщо залишок невідомий — не називай cleanup успішним. Не запускай широке historical cleanup автоматично лише через цей блок.

## Як реалізовувати правило

1. Відокремлюй три категорії: product/user data; компактні докази; disposable generated test payload. Призначення, а не слово cache або test у назві, визначає можливість видалення.
2. Кожен тест/E2E має власний root/operation ID і marker/manifest із підтвердженою належністю. Перед новою великою копією вимірюй потрібний обсяг і вільне місце; не копіюй рекурсивно весь проєкт разом із .local/artifacts/dependencies/old E2E.
3. Використовуй finally/Dispose та runner-level cleanup для success/failure/cancellation; для аварійного завершення залиш компактний owned-root manifest і безпечне явне recovery/cleanup. Резервний механізм потрібен, бо Dispose не виконується після довільного process crash.
4. Не приховуй cleanup exception. Вона має бути окремо видимою поряд із первинною test failure, без її підміни. Блокований lock не виправляти масовим kill dotnet/Java; звільняти лише власні підтверджені test process identities/handles. Користувацьку гру/лаунчер не завершувати.
5. Зберігай оригінальні failed/successful результати без переписування, source/build/hash/scope, необхідні status records, компактні screenshots і sanitized diagnostics. Якщо runnable payload видалено, додавай marker/примітку: evidence retained, payload removed. Старий PASS не стає новим запуском і не означає доступність видаленого executable.
6. Під час циклу діагностики залишай лише потрібний поточний reproducer і останні робочі outputs. Повні дублікати чотирьох test builds/ZIP/rollback copies не повинні накопичуватися після кожного E2E. Не руйнуй незавершену операцію, яка ще використовує ці файли.
7. Для filesystem operations використовуй один shell end-to-end, на Windows — native PowerShell -LiteralPath. Перед recursive delete перевір resolved absolute target, дозволений owned root, батьківські links і user-data markers; computed path поза точно визначеним root має зупинити cleanup. Не передавай список з одного shell в інший для видалення.
8. scripts/Clean-ProjectStorage.ps1 — окремий maintenance tool із власними guards і попереднім review. Його broad historical targets не є per-test lifecycle cleanup; не запускати -Execute за замовчуванням після кожного тесту й не розширювати його exclusions мовчки.
9. SDK/dependencies/current launcher залишаються: після вже виконаного maintenance старі test bin/obj/snapshots можуть бути відсутні. Потрібні outputs відновлювати підтримуваним restore/build із retained dependencies, не видаляти SDK/nuget для «очищення» й не вимикати committed audit/security settings.
10. В acceptance додати змістовні lifecycle checks success/failure/cancel/repeated run, occupied files/unknown root/link refusal та evidence/data preservation. Це інженерне уточнення існуючих FOUNDATION-04, SECURITY-04/06, DISTRIBUTION-11 і тестового workflow; число 189 requirements та 44 module rows не змінюється самим записом правила.

## Формат підсумку

- Створені test roots і приблизний пік/сукупний розмір.
- Видалені disposable roots і виміряний обсяг (або явно зазначена оцінка).
- Збережені evidence та мінімальний reproducer.
- Будь-які retained roots: точний шлях, розмір, причина і безпечний наступний крок.
- Підтвердження збереження user data, SDK/dependencies/current app та outputs користувача.

Нуль залишків не оголошувати без перевірки. Цей план не дає дозволу видаляти користувацькі кеші/дані чи довільні старі roots.
