# Microsoft Authentication — налаштування власником PTenoLauncher

## AppID review status - 2026-10-28

The owner reports submitting the official review form (https://aka.ms/mce-reviewappid) for PTenoLauncher. Status: SUBMITTED, APPROVAL PENDING; no approval message has been observed. Browser OAuth previously completed, followed by HTTP 403 from api.minecraftservices.com. The cause is not confirmed. After the review decision, retest the existing registered public client, entitlement/profile and licensed gameplay without sharing tokens or browser callback URLs.

## Quilt mod metadata checkpoint - 2026-10-28

User retest of the auth-fix payload completed browser OAuth, then failed with HTTP 403 from api.minecraftservices.com. The screenshot identifies the host/status but not the exact endpoint or safe server reason. An application-service access/registration issue is possible, not proven. Full entitlement/profile/refresh and online/offline gameplay remain unverified. No new application, borrowed Client ID or authentication bypass was used. The owner reports submitting the AppID review form on 2026-10-28; approval remains pending. Continue independent development while this remains external.

Повний Microsoft/Xbox/Minecraft login ще не пройдено. Код не використовує чужий Client ID, пароль або username-only offline identity.

Звичайний гравець натискає «Виконати вхід» у шапці лаунчера; зареєстрований публічний Client ID PTenoLauncher задано за замовчуванням. Власний ID у налаштуваннях — лише додаткова конфігурація застосунку, не вимога для кожного гравця. Схвалення Xbox/Minecraft API ще потребує перевірки; реєстрація не надає ліцензію на гру.

Стан перевірки 2026-10-27/28: власник уже зареєстрував PTenoLauncher для personal accounts, налаштував http://localhost та завершив браузерний Microsoft-етап. Наступний Xbox-запит повернув HTTP 400. У коді виправлено регістр полів JSON і додано документований Xbox contract header; повторний повний вхід ще не перевірено. Деталі та окрема тестова збірка: docs/AUTHENTICATION_FIX.md. Наведені нижче кроки реєстрації залишені для налаштування нового середовища; повторно реєструвати вже створений застосунок не потрібно.

1. Відкрийте офіційний Microsoft Entra admin center: https://entra.microsoft.com/
2. У своєму tenant відкрийте Entra ID → App registrations → New registration.
3. Назва: PTenoLauncher. Supported account types: Personal Microsoft accounts або організаційні та Personal Microsoft accounts.
4. Зареєструйте застосунок і скопіюйте Application (client) ID. Це публічний ідентифікатор desktop public client; client secret не створюйте.
5. Authentication → Add a platform → Mobile and desktop applications → redirect URI http://localhost для system browser.
6. Якщо Microsoft вимагає додаткових public-client налаштувань для вашого сценарію, застосуйте їх за актуальною офіційною документацією.
7. У PTenoLauncher → Налаштування → Акаунт Microsoft вставте власний Client ID та збережіть.
8. Натисніть «Увійти через Microsoft» і самостійно пройдіть офіційний браузерний вхід/MFA. Лаунчер не має форми пароля.
9. Перевірте нікнейм, UUID, skin cache і право на Java Edition. Вибраний акаунт зберігається у DPAPI; інші підтверджені акаунти можна перемикати.
10. Установіть Vanilla, скористайтеся автоматичною Java, перевірте Offline Readiness і виконайте ручний онлайн/офлайн game test.

Реєстрація в Entra сама по собі не доводить доступ до Minecraft Services. Якщо Xbox/XSTS/Minecraft API відхиляє ваш застосунок, потрібні актуальні дозволи/схвалення сервісу. Зверніться до офіційної підтримки Minecraft: https://help.minecraft.net/. Відомий код не обходить таку відмову. Неперевірену форму запиту доступу або вигадану процедуру схвалення тут не наведено.

Офіційні джерела:
- https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app
- https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-desktop-app-registration
- https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/overview

Ручний сценарій: увійти → перевірити entitlement/profile → запустити Vanilla → зберегти тестовий світ → штатно вийти → відключити мережу → повторити дозволений offline launch → перевірити logout вибраного акаунта.
Не передавайте пароль, MFA-код або токени розробнику/AI. До фактичного проходження цей game e2e test має статус NOT RUN.

Актуальна офіційна інструкція MSAL для системного браузера й loopback redirect: https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers . Домашній покроковий чекліст: docs/HOME_TEST_CHECKLIST.md; результат заповнюється в docs/HOME_TEST_RESULTS.md. Усі ручні тести залишаються NOT RUN до фактичного виконання користувачем.
