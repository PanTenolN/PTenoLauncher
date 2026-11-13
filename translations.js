/* Complete site localization: eight languages, no network dependencies. */
window.PTENO_TRANSLATIONS = Object.freeze({
  "uk": {
    "general": {
      "skip": "Перейти до вмісту",
      "navigation": "Основна навігація",
      "language": "Мова сайту",
      "auto": "Автоматично"
    },
    "nav": {
      "downloads": "Завантаження",
      "features": "Можливості",
      "status": "Стан",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Незалежний лаунчер Minecraft Java Edition",
      "description": "Керуй профілями та модами Minecraft в одному місці. Перед встановленням перевір можливості й обмеження саме цієї збірки.",
      "preview": "Development Preview",
      "action": "Обрати пакет",
      "secondary": "Перевірити стан випуску",
      "note": "Це попередня версія, а не гарантія роботи всіх функцій."
    },
    "downloads": {
      "overline": "ПОЧНИ ТУТ",
      "title": "Завантаж PTenoLauncher",
      "intro": "Одна збірка, два формати. Обери зручний спосіб запуску.",
      "versionLabel": "Версія",
      "dateLabel": "Випущено",
      "setupTitle": "Інсталятор",
      "setupSummary": "Майстер встановлення для звичайного використання на одному комп’ютері.",
      "portableTitle": "Portable",
      "portableSummary": "Запуск із розпакованої теки без окремого встановлення.",
      "launchLabel": "Як запустити",
      "setupLaunch": "Запусти Setup та пройди кроки встановлення.",
      "portableLaunch": "Розпакуй весь ZIP і відкрий PTenoLauncher.exe у теці.",
      "dataLabel": "Дані",
      "portableData": "поруч із .exe",
      "fileLabel": "Файл",
      "sizeLabel": "Розмір",
      "downloadSetup": "Завантажити Setup",
      "downloadPortable": "Завантажити Portable",
      "unavailable": "Пряме завантаження недоступне. Переглянь випуски на GitHub.",
      "notConfirmed": "Контрольні суми обох пакетів не підтверджено в конфігурації. Прямі кнопки приховано.",
      "confirmed": "Обидва пакети й SHA‑256 підтверджені для цього релізу.",
      "notes": "Нотатки релізу ↗",
      "all": "Усі випуски ↗",
      "shaTitle": "SHA‑256 та перевірка файлів",
      "shaHelp": "Порівняй SHA‑256 завантаженого файлу з опублікованим SHA256SUMS.txt. Не запускай файл, якщо походження не підтверджено.",
      "sumsLocation": "(на сторінці випуску)"
    },
    "features": {
      "overline": "ДЛЯ ТВОЇХ ЗБІРОК",
      "title": "Що вміє лаунчер",
      "intro": "Опис стосується публічної версії 0.2.2. Нові локальні функції не позначені як опубліковані."
    },
    "status": {
      "overline": "ПЕРЕД ВСТАНОВЛЕННЯМ",
      "title": "Стан і обмеження",
      "intro": "Development Preview призначений для тестування. Нижче зазначено, що вже перевірено й які обмеження діють.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Автоматичні тести перевіряють запуск Portable після повного розпакування. Власник проєкту вручну перевірив вхід Microsoft із ліцензією та запуск Minecraft офлайн."
    },
    "guide": {
      "overline": "ПОКРОКОВО",
      "title": "Перший запуск",
      "intro": "Для Portable потрібно розпакувати всі файли разом; не переносити лише .exe.",
      "setupTitle": "З інсталятором",
      "portableTitle": "Без встановлення",
      "setupSteps": [
        "Завантаж Setup із випуску GitHub і перевір його контрольну суму.",
        "Запусти інсталятор та виконай кроки майстра.",
        "Відкрий PTenoLauncher; обери мову, а потім переходь до профілів."
      ],
      "portableSteps": [
        "Завантаж ZIP із випуску GitHub і перевір SHA‑256.",
        "Повністю розпакуй ZIP у вибрану теку.",
        "Запусти PTenoLauncher.exe звідти; дані будуть поруч у portable-data."
      ]
    },
    "faq": {
      "overline": "Є ЗАПИТАННЯ?",
      "title": "FAQ",
      "items": [
        {
          "question": "Яка Windows потрібна?",
          "answer": "Цей Preview призначений для Windows 11 x64. Інші системи не заявлені як підтримувані."
        },
        {
          "question": "Чи потрібна куплена Minecraft Java Edition?",
          "answer": "Для гри потрібен акаунт Microsoft із правом на Minecraft Java Edition."
        },
        {
          "question": "Що робити, якщо Windows показує попередження?",
          "answer": "Файли попередньої збірки непідписані. Перш ніж запускати, звір випуск на GitHub і SHA‑256. Якщо походження не підтверджено, не запускай."
        },
        {
          "question": "Куди повідомити про помилку?",
          "answer": "Створи GitHub Issue: вкажи версію 0.2.2, кроки відтворення, Windows та очищений від секретів лог. Не публікуй токени або паролі."
        }
      ]
    },
    "footer": {
      "disclaimer": "Незалежний проєкт. Не є офіційним продуктом Minecraft і не пов’язаний із Mojang або Microsoft.",
      "ai": "Створюється за допомогою ШІ (OpenAI Codex); напрям розробки визначає автор.",
      "code": "Вихідний код ↗",
      "releases": "Випуски ↗",
      "issues": "Повідомити про проблему ↗",
      "top": "До початку ↑"
    },
    "labels": {
      "available": "У випуску",
      "verified": "Перевірено вручну",
      "unreleased": "Ще не опубліковано",
      "deferred": "Відкладено",
      "caution": "Обмеження"
    },
    "featureCards": [
      {
        "title": "Профілі Minecraft",
        "body": "Окремі теки й налаштування збірок зменшують ризик змішування модів, конфігурацій і світів."
      },
      {
        "title": "Моди",
        "body": "Каталог Modrinth, кошик встановлення та імпорт локальних .jar з переглядом перед додаванням."
      },
      {
        "title": "Консоль гри",
        "body": "Логи Minecraft і Java допомагають зрозуміти помилки запуску та модів."
      },
      {
        "title": "Керування Java",
        "body": "Підготовка потрібної Java та повторне використання перевірених локальних файлів."
      },
      {
        "title": "Ресурспаки й шейдери",
        "body": "Керування в окремих профілях розробляється; повний набір дій у 0.2.2 не підтверджений."
      },
      {
        "title": "Локальні сервери",
        "body": "Локальний сервер Minecraft 1.21.1 із Forge 52.1.0 запускається на Java 21 x64 після перевірки підготовлених файлів."
      }
    ],
    "statusItems": [
      {
        "title": "Microsoft і ліцензія",
        "body": "Власник проєкту успішно ввійшов через Microsoft і підтвердив право на Minecraft Java Edition."
      },
      {
        "title": "Офлайн-гра",
        "body": "Власник проєкту успішно запустив Minecraft без інтернету з раніше авторизованим ліцензованим акаунтом."
      },
      {
        "title": "Завантажувачі",
        "body": "У коді описані Vanilla, Fabric і Quilt. Для сервера підтверджено запуск Minecraft 1.21.1 з Forge 52.1.0. Підтримка інших ядер, NeoForge та клієнтський запуск Forge потребують окремої перевірки."
      },
      {
        "title": "CurseForge й автооновлення",
        "body": "Каталог CurseForge, розширений імпорт збірок і автоматичне оновлення лаунчера не підтверджені для цього публічного випуску."
      },
      {
        "title": "ШІ-асистент і переклад модів",
        "body": "У розробці; наявність експериментального коду не означає готовність у випуску."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Відкладено; сумісність між різними завантажувачами не гарантується."
      },
      {
        "title": "Попередження Windows",
        "body": "Preview-пакети непідписані. Перевір джерело та SHA‑256, а не обходь попередження для неперевірених файлів."
      }
    ]
  },
  "en": {
    "general": {
      "skip": "Skip to content",
      "navigation": "Main navigation",
      "language": "Website language",
      "auto": "Automatic"
    },
    "nav": {
      "downloads": "Downloads",
      "features": "Features",
      "status": "Status",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Independent Minecraft Java Edition launcher",
      "description": "Manage Minecraft profiles and mods in one place. Before installing, check the features and limitations of this exact build.",
      "preview": "Development Preview",
      "action": "Choose a package",
      "secondary": "View release status",
      "note": "This is a preview, not a guarantee that every feature works."
    },
    "downloads": {
      "overline": "GET STARTED",
      "title": "Download PTenoLauncher",
      "intro": "One build, two packages. Choose how you want to run it.",
      "versionLabel": "Version",
      "dateLabel": "Released",
      "setupTitle": "Installer",
      "setupSummary": "Guided installation for regular use on one computer.",
      "portableTitle": "Portable",
      "portableSummary": "Run from an extracted folder without a separate installation.",
      "launchLabel": "How to start",
      "setupLaunch": "Run Setup and follow the installation wizard.",
      "portableLaunch": "Extract the entire ZIP and run PTenoLauncher.exe from that folder.",
      "dataLabel": "Data",
      "portableData": "beside the .exe",
      "fileLabel": "File",
      "sizeLabel": "Size",
      "downloadSetup": "Download Setup",
      "downloadPortable": "Download Portable",
      "unavailable": "Direct downloads are unavailable. See releases on GitHub.",
      "notConfirmed": "The checksums for both packages are not confirmed in this site configuration. Direct buttons are hidden.",
      "confirmed": "Both packages and SHA‑256 hashes are confirmed for this release.",
      "notes": "Release notes ↗",
      "all": "All releases ↗",
      "shaTitle": "SHA‑256 and file verification",
      "shaHelp": "Compare the downloaded file’s SHA‑256 against the published SHA256SUMS.txt. Do not run files from an unverified source.",
      "sumsLocation": "(on release page)"
    },
    "features": {
      "overline": "FOR YOUR BUILDS",
      "title": "Launcher capabilities",
      "intro": "Descriptions refer to the public 0.2.2 build. New local-only features are not presented as released."
    },
    "status": {
      "overline": "BEFORE INSTALLING",
      "title": "Status and limitations",
      "intro": "Development Preview is intended for testing. See which features have been tested and what limitations apply.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Automated tests check Portable startup after full extraction. The project owner manually tested licensed Microsoft sign-in and offline Minecraft startup."
    },
    "guide": {
      "overline": "STEP BY STEP",
      "title": "First launch",
      "intro": "For Portable, extract all files together; do not move just the .exe.",
      "setupTitle": "With installer",
      "portableTitle": "Without installation",
      "setupSteps": [
        "Download Setup from the GitHub release and verify its checksum.",
        "Run the installer and follow the wizard.",
        "Open PTenoLauncher, choose a language and continue to profiles."
      ],
      "portableSteps": [
        "Download the ZIP from GitHub releases and verify SHA‑256.",
        "Extract the whole ZIP into a folder of your choice.",
        "Run PTenoLauncher.exe there; data stays next to it in portable-data."
      ]
    },
    "faq": {
      "overline": "QUESTIONS?",
      "title": "FAQ",
      "items": [
        {
          "question": "Which Windows version is required?",
          "answer": "This preview targets Windows 11 x64. Other operating systems are not claimed as supported."
        },
        {
          "question": "Do I need to own Minecraft Java Edition?",
          "answer": "To play, you need a Microsoft account with access to Minecraft Java Edition."
        },
        {
          "question": "What if Windows shows a warning?",
          "answer": "Preview files are unsigned. Check the GitHub release and SHA‑256 before running. Do not run files from an unverified source."
        },
        {
          "question": "Where can I report a bug?",
          "answer": "Create a GitHub Issue with version 0.2.2, reproduction steps, Windows details and a log scrubbed of secrets. Never post tokens or passwords."
        }
      ]
    },
    "footer": {
      "disclaimer": "An independent project. Not an official Minecraft product and not affiliated with Mojang or Microsoft.",
      "ai": "Developed with AI assistance (OpenAI Codex); the owner sets the development direction.",
      "code": "Source code ↗",
      "releases": "Releases ↗",
      "issues": "Report an issue ↗",
      "top": "Back to top ↑"
    },
    "labels": {
      "available": "In release",
      "verified": "Manually tested",
      "unreleased": "Not yet published",
      "deferred": "Deferred",
      "caution": "Limitation"
    },
    "featureCards": [
      {
        "title": "Minecraft profiles",
        "body": "Separate folders and settings help keep mods, configurations and worlds from mixing across builds."
      },
      {
        "title": "Mods",
        "body": "Modrinth catalog, installation cart and local .jar imports with a review before installation."
      },
      {
        "title": "Game console",
        "body": "Minecraft and Java logs help diagnose launch and mod failures."
      },
      {
        "title": "Java management",
        "body": "Prepare the required Java version and reuse verified local files."
      },
      {
        "title": "Resource packs and shaders",
        "body": "Per-profile management is in development; the exact actions in 0.2.2 are not fully confirmed."
      },
      {
        "title": "Local servers",
        "body": "The local Minecraft 1.21.1 server with Forge 52.1.0 starts on 64-bit Java 21 after the launcher verifies its prepared files."
      }
    ],
    "statusItems": [
      {
        "title": "Microsoft sign-in and license",
        "body": "The project owner signed in successfully with Microsoft and verified their Minecraft Java Edition entitlement."
      },
      {
        "title": "Offline play",
        "body": "The project owner successfully launched Minecraft without an internet connection using a previously signed-in licensed account."
      },
      {
        "title": "Loaders",
        "body": "Vanilla, Fabric and Quilt are documented in the source. The verified server combination is Minecraft 1.21.1 with Forge 52.1.0. Other server cores, NeoForge and Forge client gameplay need separate testing."
      },
      {
        "title": "CurseForge and updates",
        "body": "The CurseForge catalog, extended modpack imports and launcher auto-update are not confirmed for this public build."
      },
      {
        "title": "AI assistant and mod translation",
        "body": "In development; experimental code does not imply a ready-to-use release feature."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Deferred; compatibility across loaders is not guaranteed."
      },
      {
        "title": "Windows warnings",
        "body": "Preview packages are unsigned. Verify source and SHA‑256 instead of bypassing warnings for unverified files."
      }
    ]
  },
  "pl": {
    "general": {
      "skip": "Przejdź do treści",
      "navigation": "Nawigacja główna",
      "language": "Język strony",
      "auto": "Automatycznie"
    },
    "nav": {
      "downloads": "Pobieranie",
      "features": "Funkcje",
      "status": "Stan",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Niezależny launcher Minecraft Java Edition",
      "description": "Zarządzaj profilami i modami Minecraft w jednym miejscu. Przed instalacją sprawdź możliwości i ograniczenia tej konkretnej wersji.",
      "preview": "Development Preview",
      "action": "Wybierz pakiet",
      "secondary": "Sprawdź stan wydania",
      "note": "To wersja testowa — nie gwarantuje działania wszystkich funkcji."
    },
    "downloads": {
      "overline": "NA POCZĄTEK",
      "title": "Pobierz PTenoLauncher",
      "intro": "Jedna wersja, dwa pakiety. Wybierz sposób uruchamiania.",
      "versionLabel": "Wersja",
      "dateLabel": "Data wydania",
      "setupTitle": "Instalator",
      "setupSummary": "Instalacja z kreatorem do codziennego użytkowania na jednym komputerze.",
      "portableTitle": "Portable",
      "portableSummary": "Uruchamianie z rozpakowanego folderu bez osobnej instalacji.",
      "launchLabel": "Uruchamianie",
      "setupLaunch": "Uruchom Setup i wykonaj kroki instalatora.",
      "portableLaunch": "Rozpakuj całe ZIP i uruchom PTenoLauncher.exe w tym folderze.",
      "dataLabel": "Dane",
      "portableData": "obok pliku .exe",
      "fileLabel": "Plik",
      "sizeLabel": "Rozmiar",
      "downloadSetup": "Pobierz Setup",
      "downloadPortable": "Pobierz Portable",
      "unavailable": "Bezpośrednie pobieranie niedostępne. Zobacz wydania na GitHubie.",
      "notConfirmed": "Sumy kontrolne obu pakietów nie zostały potwierdzone w konfiguracji strony. Przyciski ukryto.",
      "confirmed": "Oba pakiety i SHA‑256 zostały potwierdzone dla tego wydania.",
      "notes": "Opis wydania ↗",
      "all": "Wszystkie wydania ↗",
      "shaTitle": "SHA‑256 i sprawdzanie plików",
      "shaHelp": "Porównaj SHA‑256 pobranego pliku z opublikowanym SHA256SUMS.txt. Nie uruchamiaj plików z niezweryfikowanego źródła.",
      "sumsLocation": "(na stronie wydania)"
    },
    "features": {
      "overline": "DLA TWOICH PACZEK",
      "title": "Możliwości launchera",
      "intro": "Opis dotyczy publicznej wersji 0.2.2, a nie nowych funkcji dostępnych tylko lokalnie."
    },
    "status": {
      "overline": "PRZED INSTALACJĄ",
      "title": "Stan i ograniczenia",
      "intro": "Development Preview służy do testów. Poniżej znajdziesz wyniki testów i aktualne ograniczenia.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Testy automatyczne sprawdzają uruchomienie wersji Portable po pełnym rozpakowaniu. Właściciel projektu ręcznie sprawdził logowanie Microsoft z weryfikacją licencji i uruchomienie Minecrafta offline."
    },
    "guide": {
      "overline": "KROK PO KROKU",
      "title": "Pierwsze uruchomienie",
      "intro": "W wersji Portable rozpakuj wszystkie pliki razem; nie przenoś samego .exe.",
      "setupTitle": "Z instalatorem",
      "portableTitle": "Bez instalacji",
      "setupSteps": [
        "Pobierz Setup z GitHuba i sprawdź sumę kontrolną.",
        "Uruchom instalator i wykonaj kroki kreatora.",
        "Otwórz PTenoLauncher, wybierz język i przejdź do profili."
      ],
      "portableSteps": [
        "Pobierz ZIP z GitHuba i sprawdź SHA‑256.",
        "Rozpakuj całe ZIP do wybranego folderu.",
        "Uruchom tam PTenoLauncher.exe; dane znajdziesz w portable-data."
      ]
    },
    "faq": {
      "overline": "MASZ PYTANIA?",
      "title": "FAQ",
      "items": [
        {
          "question": "Jaki Windows jest wymagany?",
          "answer": "Wersja testowa jest przeznaczona dla Windows 11 x64. Inne systemy nie są oficjalnie wspierane."
        },
        {
          "question": "Czy potrzebuję Minecraft Java Edition?",
          "answer": "Do gry potrzebujesz konta Microsoft z dostępem do Minecraft Java Edition."
        },
        {
          "question": "Co zrobić z ostrzeżeniem Windows?",
          "answer": "Pliki testowe nie są podpisane. Przed uruchomieniem sprawdź wydanie GitHub i SHA‑256. Nie uruchamiaj plików z niepewnego źródła."
        },
        {
          "question": "Gdzie zgłosić błąd?",
          "answer": "Utwórz GitHub Issue: podaj 0.2.2, kroki odtworzenia, wersję Windows i log bez sekretów. Nie publikuj tokenów ani haseł."
        }
      ]
    },
    "footer": {
      "disclaimer": "Niezależny projekt. Nie jest oficjalnym produktem Minecraft i nie jest powiązany z Mojang ani Microsoft.",
      "ai": "Rozwijany przy pomocy AI (OpenAI Codex); kierunek rozwoju określa autor.",
      "code": "Kod źródłowy ↗",
      "releases": "Wydania ↗",
      "issues": "Zgłoś problem ↗",
      "top": "Na górę ↑"
    },
    "labels": {
      "available": "W wydaniu",
      "verified": "Sprawdzono ręcznie",
      "unreleased": "Jeszcze nieopublikowane",
      "deferred": "Odłożone",
      "caution": "Ograniczenie"
    },
    "featureCards": [
      {
        "title": "Profile Minecraft",
        "body": "Oddzielne foldery i ustawienia ograniczają mieszanie modów, konfiguracji i światów."
      },
      {
        "title": "Mody",
        "body": "Katalog Modrinth, koszyk instalacji i lokalne pliki .jar z podglądem przed dodaniem."
      },
      {
        "title": "Konsola gry",
        "body": "Logi Minecraft i Java pomagają znaleźć przyczyny błędów uruchamiania i modów."
      },
      {
        "title": "Zarządzanie Javą",
        "body": "Przygotowanie odpowiedniej Javy i ponowne użycie sprawdzonych plików lokalnych."
      },
      {
        "title": "Paczki zasobów i shadery",
        "body": "Zarządzanie dla profilów jest rozwijane; pełny zakres działań w 0.2.2 nie jest potwierdzony."
      },
      {
        "title": "Serwery lokalne",
        "body": "Lokalny serwer Minecraft 1.21.1 z Forge 52.1.0 uruchamia się na 64-bitowej Javie 21 po sprawdzeniu przygotowanych plików."
      }
    ],
    "statusItems": [
      {
        "title": "Logowanie Microsoft i licencja",
        "body": "Właściciel projektu pomyślnie zalogował się przez Microsoft i potwierdził uprawnienie do Minecraft Java Edition."
      },
      {
        "title": "Gra offline",
        "body": "Właściciel projektu pomyślnie uruchomił Minecrafta bez internetu na koncie z licencją, na które wcześniej się zalogował."
      },
      {
        "title": "Loadery",
        "body": "Kod opisuje Vanilla, Fabric i Quilt. Potwierdzono uruchomienie serwera Minecraft 1.21.1 z Forge 52.1.0. Inne silniki serwerów, NeoForge i uruchomienie klienta Forge wymagają osobnych testów."
      },
      {
        "title": "CurseForge i aktualizacje",
        "body": "Katalog CurseForge, rozszerzony import paczek i automatyczne aktualizacje nie są potwierdzone dla tej wersji."
      },
      {
        "title": "Asystent AI i tłumaczenie modów",
        "body": "W rozwoju; kod eksperymentalny nie oznacza gotowej funkcji w wydaniu."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Odłożone; zgodność między loaderami nie jest gwarantowana."
      },
      {
        "title": "Ostrzeżenia Windows",
        "body": "Pakiety testowe nie są podpisane. Sprawdź źródło i SHA‑256; nie omijaj ostrzeżeń dla niezweryfikowanych plików."
      }
    ]
  },
  "de": {
    "general": {
      "skip": "Zum Inhalt springen",
      "navigation": "Hauptnavigation",
      "language": "Sprache der Website",
      "auto": "Automatisch"
    },
    "nav": {
      "downloads": "Downloads",
      "features": "Funktionen",
      "status": "Status",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Unabhängiger Launcher für Minecraft Java Edition",
      "description": "Verwalte Minecraft-Profile und Mods an einem Ort. Prüfe vor der Installation die Funktionen und Einschränkungen genau dieser Version.",
      "preview": "Development Preview",
      "action": "Paket wählen",
      "secondary": "Versionsstatus ansehen",
      "note": "Dies ist eine Vorabversion. Nicht alle Funktionen sind garantiert nutzbar."
    },
    "downloads": {
      "overline": "ERSTE SCHRITTE",
      "title": "PTenoLauncher herunterladen",
      "intro": "Eine Version, zwei Pakete. Wähle die passende Startmethode.",
      "versionLabel": "Version",
      "dateLabel": "Veröffentlicht",
      "setupTitle": "Installer",
      "setupSummary": "Geführte Installation für die normale Nutzung auf einem PC.",
      "portableTitle": "Portable",
      "portableSummary": "Aus einem entpackten Ordner ohne separate Installation starten.",
      "launchLabel": "Start",
      "setupLaunch": "Setup starten und dem Installationsassistenten folgen.",
      "portableLaunch": "Das gesamte ZIP entpacken und PTenoLauncher.exe im Ordner starten.",
      "dataLabel": "Daten",
      "portableData": "neben der .exe",
      "fileLabel": "Datei",
      "sizeLabel": "Größe",
      "downloadSetup": "Setup herunterladen",
      "downloadPortable": "Portable herunterladen",
      "unavailable": "Direkte Downloads sind nicht verfügbar. Sieh dir die Releases auf GitHub an.",
      "notConfirmed": "Die Prüfsummen beider Pakete sind in der Website-Konfiguration nicht bestätigt. Direkte Schaltflächen sind ausgeblendet.",
      "confirmed": "Beide Pakete und SHA‑256 sind für dieses Release bestätigt.",
      "notes": "Versionshinweise ↗",
      "all": "Alle Releases ↗",
      "shaTitle": "SHA‑256 und Dateiprüfung",
      "shaHelp": "Vergleiche die SHA‑256 der Datei mit der veröffentlichten SHA256SUMS.txt. Starte keine Dateien aus ungeprüften Quellen.",
      "sumsLocation": "(auf der Release-Seite)"
    },
    "features": {
      "overline": "FÜR DEINE MODPACKS",
      "title": "Funktionen des Launchers",
      "intro": "Die Angaben beziehen sich auf die öffentliche Version 0.2.2, nicht auf nur lokal vorhandene Funktionen."
    },
    "status": {
      "overline": "VOR DER INSTALLATION",
      "title": "Status und Einschränkungen",
      "intro": "Development Preview dient zum Testen. Hier findest du die Prüfergebnisse und die geltenden Einschränkungen.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Automatisierte Tests prüfen den Start von Portable nach dem vollständigen Entpacken. Der Projektinhaber hat die Microsoft-Anmeldung mit Lizenzprüfung und den Offline-Start von Minecraft manuell getestet."
    },
    "guide": {
      "overline": "SCHRITT FÜR SCHRITT",
      "title": "Erster Start",
      "intro": "Bei Portable alle Dateien zusammen entpacken, nicht nur die .exe verschieben.",
      "setupTitle": "Mit Installer",
      "portableTitle": "Ohne Installation",
      "setupSteps": [
        "Setup vom GitHub-Release laden und Prüfsumme kontrollieren.",
        "Installer starten und dem Assistenten folgen.",
        "PTenoLauncher öffnen, Sprache wählen und die Profile aufrufen."
      ],
      "portableSteps": [
        "ZIP von GitHub laden und SHA‑256 kontrollieren.",
        "Das gesamte ZIP in einen Ordner entpacken.",
        "Dort PTenoLauncher.exe starten; Daten liegen daneben in portable-data."
      ]
    },
    "faq": {
      "overline": "FRAGEN?",
      "title": "FAQ",
      "items": [
        {
          "question": "Welche Windows-Version wird benötigt?",
          "answer": "Dieses Preview ist für Windows 11 x64 vorgesehen. Andere Betriebssysteme gelten nicht als unterstützt."
        },
        {
          "question": "Muss ich Minecraft Java Edition besitzen?",
          "answer": "Zum Spielen brauchst du ein Microsoft-Konto mit Zugriff auf Minecraft Java Edition."
        },
        {
          "question": "Was tun bei einer Windows-Warnung?",
          "answer": "Die Vorabdateien sind nicht signiert. Vor dem Start GitHub-Release und SHA‑256 prüfen. Ungeprüfte Dateien nicht starten."
        },
        {
          "question": "Wo kann ich Fehler melden?",
          "answer": "Erstelle ein GitHub Issue mit Version 0.2.2, Schritten, Windows-Angaben und einem von Geheimnissen bereinigten Log. Niemals Tokens oder Passwörter posten."
        }
      ]
    },
    "footer": {
      "disclaimer": "Unabhängiges Projekt. Kein offizielles Minecraft-Produkt und nicht mit Mojang oder Microsoft verbunden.",
      "ai": "Entwickelt mit KI-Unterstützung (OpenAI Codex); die Entwicklungsrichtung bestimmt der Eigentümer.",
      "code": "Quellcode ↗",
      "releases": "Releases ↗",
      "issues": "Problem melden ↗",
      "top": "Nach oben ↑"
    },
    "labels": {
      "available": "Im Release",
      "verified": "Manuell getestet",
      "unreleased": "Noch nicht veröffentlicht",
      "deferred": "Zurückgestellt",
      "caution": "Einschränkung"
    },
    "featureCards": [
      {
        "title": "Minecraft-Profile",
        "body": "Getrennte Ordner und Einstellungen verhindern das Vermischen von Mods, Konfigurationen und Welten."
      },
      {
        "title": "Mods",
        "body": "Modrinth-Katalog, Installationskorb und lokale .jar-Dateien mit Vorschau vor dem Installieren."
      },
      {
        "title": "Spielkonsole",
        "body": "Minecraft- und Java-Protokolle helfen bei Start- und Mod-Fehlern."
      },
      {
        "title": "Java-Verwaltung",
        "body": "Passende Java-Version vorbereiten und geprüfte lokale Dateien wiederverwenden."
      },
      {
        "title": "Ressourcenpakete und Shader",
        "body": "Profilbezogene Verwaltung ist in Arbeit; der volle Funktionsumfang in 0.2.2 ist nicht bestätigt."
      },
      {
        "title": "Lokale Server",
        "body": "Der lokale Minecraft-1.21.1-Server mit Forge 52.1.0 startet mit 64-Bit-Java 21, nachdem der Launcher die vorbereiteten Dateien geprüft hat."
      }
    ],
    "statusItems": [
      {
        "title": "Microsoft-Anmeldung und Lizenz",
        "body": "Der Projektinhaber hat sich erfolgreich mit Microsoft angemeldet und die Berechtigung für Minecraft Java Edition geprüft."
      },
      {
        "title": "Offline-Spiel",
        "body": "Der Projektinhaber hat Minecraft ohne Internetverbindung erfolgreich mit einem zuvor angemeldeten Konto mit gültiger Lizenz gestartet."
      },
      {
        "title": "Loader",
        "body": "Vanilla, Fabric und Quilt sind im Quellcode dokumentiert. Geprüft ist der Serverstart für Minecraft 1.21.1 mit Forge 52.1.0. Andere Server-Software, NeoForge und der Forge-Client benötigen separate Tests."
      },
      {
        "title": "CurseForge und Updates",
        "body": "CurseForge-Katalog, erweiterter Modpack-Import und automatische Updates sind für dieses Release nicht bestätigt."
      },
      {
        "title": "KI-Assistent und Mod-Übersetzung",
        "body": "In Entwicklung; experimenteller Code bedeutet keine veröffentlichte Funktion."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Zurückgestellt; Kompatibilität zwischen Loadern ist nicht garantiert."
      },
      {
        "title": "Windows-Warnungen",
        "body": "Vorabpakete sind unsigniert. Quelle und SHA‑256 prüfen und Warnungen bei ungeprüften Dateien nicht umgehen."
      }
    ]
  },
  "es": {
    "general": {
      "skip": "Saltar al contenido",
      "navigation": "Navegación principal",
      "language": "Idioma del sitio",
      "auto": "Automático"
    },
    "nav": {
      "downloads": "Descargas",
      "features": "Funciones",
      "status": "Estado",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Lanzador independiente de Minecraft Java Edition",
      "description": "Gestiona perfiles y mods de Minecraft en un solo lugar. Antes de instalar, consulta las funciones y limitaciones de esta versión concreta.",
      "preview": "Development Preview",
      "action": "Elegir paquete",
      "secondary": "Ver estado de la versión",
      "note": "Es una versión preliminar: no se garantiza que todo funcione."
    },
    "downloads": {
      "overline": "EMPIEZA AQUÍ",
      "title": "Descargar PTenoLauncher",
      "intro": "Una versión, dos paquetes. Elige cómo ejecutarlo.",
      "versionLabel": "Versión",
      "dateLabel": "Publicado",
      "setupTitle": "Instalador",
      "setupSummary": "Instalación guiada para uso habitual en un ordenador.",
      "portableTitle": "Portable",
      "portableSummary": "Ejecutar desde una carpeta extraída sin instalación independiente.",
      "launchLabel": "Cómo iniciar",
      "setupLaunch": "Ejecuta Setup y sigue el asistente de instalación.",
      "portableLaunch": "Extrae todo el ZIP y ejecuta PTenoLauncher.exe desde esa carpeta.",
      "dataLabel": "Datos",
      "portableData": "junto al .exe",
      "fileLabel": "Archivo",
      "sizeLabel": "Tamaño",
      "downloadSetup": "Descargar Setup",
      "downloadPortable": "Descargar Portable",
      "unavailable": "Las descargas directas no están disponibles. Consulta las versiones en GitHub.",
      "notConfirmed": "Las sumas de ambos paquetes no están confirmadas en la configuración del sitio. Los botones directos están ocultos.",
      "confirmed": "Los paquetes y SHA‑256 están confirmados para esta versión.",
      "notes": "Notas de la versión ↗",
      "all": "Todas las versiones ↗",
      "shaTitle": "SHA‑256 y verificación de archivos",
      "shaHelp": "Compara el SHA‑256 del archivo descargado con el SHA256SUMS.txt publicado. No ejecutes archivos de fuentes no verificadas.",
      "sumsLocation": "(en la página de la versión)"
    },
    "features": {
      "overline": "PARA TUS MODPACKS",
      "title": "Funciones del lanzador",
      "intro": "Las descripciones se refieren a la versión pública 0.2.2, no a funciones solo disponibles localmente."
    },
    "status": {
      "overline": "ANTES DE INSTALAR",
      "title": "Estado y limitaciones",
      "intro": "Development Preview está destinada a pruebas. Aquí puedes consultar qué se ha probado y qué limitaciones se aplican.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Las pruebas automatizadas comprueban el inicio de Portable después de extraerlo por completo. El propietario del proyecto probó manualmente el inicio de sesión con Microsoft y la verificación de licencia, así como el arranque de Minecraft sin conexión."
    },
    "guide": {
      "overline": "PASO A PASO",
      "title": "Primer inicio",
      "intro": "En Portable extrae todos los archivos juntos; no muevas solamente el .exe.",
      "setupTitle": "Con instalador",
      "portableTitle": "Sin instalación",
      "setupSteps": [
        "Descarga Setup desde GitHub y comprueba su suma.",
        "Ejecuta el instalador y sigue el asistente.",
        "Abre PTenoLauncher, elige un idioma y ve a los perfiles."
      ],
      "portableSteps": [
        "Descarga el ZIP desde GitHub y verifica SHA‑256.",
        "Extrae todo el ZIP en la carpeta que prefieras.",
        "Ejecuta PTenoLauncher.exe allí; los datos quedan en portable-data."
      ]
    },
    "faq": {
      "overline": "¿PREGUNTAS?",
      "title": "FAQ",
      "items": [
        {
          "question": "¿Qué versión de Windows necesito?",
          "answer": "Esta vista previa está dirigida a Windows 11 x64. No se afirma compatibilidad con otros sistemas."
        },
        {
          "question": "¿Necesito comprar Minecraft Java Edition?",
          "answer": "Para jugar necesitas una cuenta de Microsoft con acceso a Minecraft Java Edition."
        },
        {
          "question": "¿Y si Windows muestra una advertencia?",
          "answer": "Los archivos preliminares no están firmados. Comprueba el lanzamiento en GitHub y SHA‑256 antes de ejecutar. No abras archivos de origen dudoso."
        },
        {
          "question": "¿Dónde informo de un error?",
          "answer": "Abre un GitHub Issue con la versión 0.2.2, pasos, detalles de Windows y un registro sin secretos. Nunca publiques tokens ni contraseñas."
        }
      ]
    },
    "footer": {
      "disclaimer": "Proyecto independiente. No es un producto oficial de Minecraft ni está afiliado a Mojang o Microsoft.",
      "ai": "Desarrollado con ayuda de IA (OpenAI Codex); el autor define la dirección del proyecto.",
      "code": "Código fuente ↗",
      "releases": "Versiones ↗",
      "issues": "Informar de un error ↗",
      "top": "Volver arriba ↑"
    },
    "labels": {
      "available": "En la versión",
      "verified": "Probado manualmente",
      "unreleased": "Aún no publicado",
      "deferred": "Aplazado",
      "caution": "Limitación"
    },
    "featureCards": [
      {
        "title": "Perfiles de Minecraft",
        "body": "Carpetas y ajustes separados evitan mezclar mods, configuraciones y mundos entre instalaciones."
      },
      {
        "title": "Mods",
        "body": "Catálogo Modrinth, carrito de instalación e importación de .jar locales con revisión previa."
      },
      {
        "title": "Consola del juego",
        "body": "Los registros de Minecraft y Java ayudan a diagnosticar errores de inicio y de mods."
      },
      {
        "title": "Gestión de Java",
        "body": "Preparación de Java y reutilización de archivos locales verificados."
      },
      {
        "title": "Paquetes de recursos y shaders",
        "body": "La gestión por perfil está en desarrollo; no se confirma el alcance exacto de 0.2.2."
      },
      {
        "title": "Servidores locales",
        "body": "El servidor local de Minecraft 1.21.1 con Forge 52.1.0 se inicia con Java 21 de 64 bits tras comprobar los archivos preparados."
      }
    ],
    "statusItems": [
      {
        "title": "Inicio de sesión y licencia Microsoft",
        "body": "El propietario del proyecto inició sesión correctamente con Microsoft y verificó el acceso a Minecraft Java Edition."
      },
      {
        "title": "Juego sin conexión",
        "body": "El propietario del proyecto inició Minecraft sin conexión a Internet con una cuenta con licencia en la que había iniciado sesión previamente."
      },
      {
        "title": "Cargadores",
        "body": "Vanilla, Fabric y Quilt están documentados en el código. Se ha comprobado el inicio del servidor Minecraft 1.21.1 con Forge 52.1.0. Otros núcleos de servidor, NeoForge y el cliente Forge requieren pruebas por separado."
      },
      {
        "title": "CurseForge y actualizaciones",
        "body": "No se confirman el catálogo CurseForge, la importación ampliada de modpacks ni la actualización automática."
      },
      {
        "title": "Asistente de IA y traducción de mods",
        "body": "En desarrollo; el código experimental no implica una función pública lista."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Aplazado; no se garantiza la compatibilidad entre cargadores."
      },
      {
        "title": "Avisos de Windows",
        "body": "Los paquetes preliminares no están firmados. Verifica el origen y SHA‑256, sin omitir avisos para archivos no verificados."
      }
    ]
  },
  "fr": {
    "general": {
      "skip": "Aller au contenu",
      "navigation": "Navigation principale",
      "language": "Langue du site",
      "auto": "Automatique"
    },
    "nav": {
      "downloads": "Téléchargement",
      "features": "Fonctionnalités",
      "status": "État",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Lanceur indépendant pour Minecraft Java Edition",
      "description": "Gérez vos profils et mods Minecraft au même endroit. Avant l’installation, vérifiez les fonctionnalités et limites de cette version précise.",
      "preview": "Development Preview",
      "action": "Choisir un paquet",
      "secondary": "Voir l’état de la version",
      "note": "Il s’agit d’une préversion : toutes les fonctions ne sont pas garanties."
    },
    "downloads": {
      "overline": "POUR COMMENCER",
      "title": "Télécharger PTenoLauncher",
      "intro": "Une version, deux formats. Choisissez votre mode de lancement.",
      "versionLabel": "Version",
      "dateLabel": "Publié le",
      "setupTitle": "Installateur",
      "setupSummary": "Installation guidée pour un usage régulier sur un ordinateur.",
      "portableTitle": "Portable",
      "portableSummary": "Exécution depuis un dossier extrait, sans installation séparée.",
      "launchLabel": "Démarrage",
      "setupLaunch": "Lancez Setup et suivez l’assistant d’installation.",
      "portableLaunch": "Extrayez tout le ZIP et lancez PTenoLauncher.exe dans le dossier.",
      "dataLabel": "Données",
      "portableData": "à côté du .exe",
      "fileLabel": "Fichier",
      "sizeLabel": "Taille",
      "downloadSetup": "Télécharger Setup",
      "downloadPortable": "Télécharger Portable",
      "unavailable": "Téléchargements directs indisponibles. Consultez les versions sur GitHub.",
      "notConfirmed": "Les sommes de contrôle des deux paquets ne sont pas confirmées dans la configuration du site. Les boutons directs sont masqués.",
      "confirmed": "Les deux paquets et leurs SHA‑256 sont confirmés pour cette version.",
      "notes": "Notes de version ↗",
      "all": "Toutes les versions ↗",
      "shaTitle": "SHA‑256 et vérification",
      "shaHelp": "Comparez le SHA‑256 du fichier avec le SHA256SUMS.txt publié. Ne lancez pas de fichiers de source non vérifiée.",
      "sumsLocation": "(sur la page de version)"
    },
    "features": {
      "overline": "POUR VOS MODPACKS",
      "title": "Fonctionnalités du lanceur",
      "intro": "Ces informations concernent la version publique 0.2.2, pas les nouveautés disponibles uniquement en local."
    },
    "status": {
      "overline": "AVANT L’INSTALLATION",
      "title": "État et limites",
      "intro": "La préversion Development Preview est destinée aux essais. Consultez les résultats des tests et les limites de cette version.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Les tests automatisés vérifient le démarrage de Portable après extraction complète. Le propriétaire du projet a testé manuellement la connexion Microsoft avec vérification de la licence et le démarrage de Minecraft hors ligne."
    },
    "guide": {
      "overline": "ÉTAPE PAR ÉTAPE",
      "title": "Premier démarrage",
      "intro": "Pour Portable, extrayez tous les fichiers ensemble; ne déplacez pas seulement le .exe.",
      "setupTitle": "Avec installateur",
      "portableTitle": "Sans installation",
      "setupSteps": [
        "Téléchargez Setup depuis GitHub et vérifiez sa somme.",
        "Lancez l’installateur et suivez les étapes.",
        "Ouvrez PTenoLauncher, choisissez la langue puis les profils."
      ],
      "portableSteps": [
        "Téléchargez le ZIP depuis GitHub et vérifiez SHA‑256.",
        "Extrayez le ZIP entier dans un dossier.",
        "Lancez PTenoLauncher.exe ; les données seront dans portable-data à côté."
      ]
    },
    "faq": {
      "overline": "DES QUESTIONS ?",
      "title": "FAQ",
      "items": [
        {
          "question": "Quelle version de Windows faut-il ?",
          "answer": "Cette préversion cible Windows 11 x64. Aucun autre système n’est annoncé comme compatible."
        },
        {
          "question": "Faut-il posséder Minecraft Java Edition ?",
          "answer": "Pour jouer, il faut un compte Microsoft disposant d’un accès à Minecraft Java Edition."
        },
        {
          "question": "Que faire face à une alerte Windows ?",
          "answer": "Les fichiers de préversion ne sont pas signés. Vérifiez la page GitHub et SHA‑256 avant lancement. N’exécutez pas un fichier de provenance douteuse."
        },
        {
          "question": "Où signaler un bug ?",
          "answer": "Créez une GitHub Issue avec 0.2.2, les étapes, Windows et un journal sans secrets. Ne publiez jamais de jetons ni de mots de passe."
        }
      ]
    },
    "footer": {
      "disclaimer": "Projet indépendant. Ce n’est pas un produit officiel Minecraft et il n’est pas affilié à Mojang ou Microsoft.",
      "ai": "Développé avec l’aide de l’IA (OpenAI Codex) ; son auteur définit les orientations.",
      "code": "Code source ↗",
      "releases": "Versions ↗",
      "issues": "Signaler un problème ↗",
      "top": "Retour en haut ↑"
    },
    "labels": {
      "available": "Dans la version",
      "verified": "Testé manuellement",
      "unreleased": "Pas encore publié",
      "deferred": "Reporté",
      "caution": "Limitation"
    },
    "featureCards": [
      {
        "title": "Profils Minecraft",
        "body": "Des dossiers et paramètres séparés évitent de mélanger mods, configurations et mondes."
      },
      {
        "title": "Mods",
        "body": "Catalogue Modrinth, panier d’installation et import de .jar locaux avec aperçu avant installation."
      },
      {
        "title": "Console du jeu",
        "body": "Les journaux Minecraft et Java facilitent le diagnostic des erreurs de démarrage et de mods."
      },
      {
        "title": "Gestion de Java",
        "body": "Préparation de Java et réutilisation de fichiers locaux vérifiés."
      },
      {
        "title": "Packs de ressources et shaders",
        "body": "La gestion par profil est en cours; les actions exactes de 0.2.2 ne sont pas toutes confirmées."
      },
      {
        "title": "Serveurs locaux",
        "body": "Le serveur local Minecraft 1.21.1 avec Forge 52.1.0 démarre avec Java 21 en 64 bits après la vérification des fichiers préparés."
      }
    ],
    "statusItems": [
      {
        "title": "Connexion Microsoft et licence",
        "body": "Le propriétaire du projet s’est connecté avec succès via Microsoft et a vérifié son accès à Minecraft Java Edition."
      },
      {
        "title": "Jeu hors ligne",
        "body": "Le propriétaire du projet a lancé Minecraft sans connexion Internet avec un compte disposant d’une licence et déjà authentifié."
      },
      {
        "title": "Chargeurs",
        "body": "Vanilla, Fabric et Quilt sont documentés dans le code source. Le démarrage du serveur Minecraft 1.21.1 avec Forge 52.1.0 est vérifié. Les autres logiciels serveur, NeoForge et le client Forge nécessitent des tests séparés."
      },
      {
        "title": "CurseForge et mises à jour",
        "body": "Le catalogue CurseForge, l’import avancé de modpacks et la mise à jour automatique ne sont pas confirmés dans cette version."
      },
      {
        "title": "Assistant IA et traduction de mods",
        "body": "En développement; du code expérimental ne signifie pas une fonction publiée."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Reporté; la compatibilité entre chargeurs n’est pas garantie."
      },
      {
        "title": "Avertissements Windows",
        "body": "Les paquets de préversion ne sont pas signés. Vérifiez la source et le SHA‑256; ne contournez pas les alertes de fichiers non vérifiés."
      }
    ]
  },
  "pt-BR": {
    "general": {
      "skip": "Pular para o conteúdo",
      "navigation": "Navegação principal",
      "language": "Idioma do site",
      "auto": "Automático"
    },
    "nav": {
      "downloads": "Downloads",
      "features": "Recursos",
      "status": "Status",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Launcher independente do Minecraft Java Edition",
      "description": "Gerencie perfis e mods do Minecraft em um só lugar. Antes de instalar, confira os recursos e limites desta versão específica.",
      "preview": "Development Preview",
      "action": "Escolher pacote",
      "secondary": "Ver status da versão",
      "note": "Esta é uma prévia; nem todos os recursos têm funcionamento garantido."
    },
    "downloads": {
      "overline": "COMECE AQUI",
      "title": "Baixar PTenoLauncher",
      "intro": "Uma versão, dois pacotes. Escolha como executar.",
      "versionLabel": "Versão",
      "dateLabel": "Lançado em",
      "setupTitle": "Instalador",
      "setupSummary": "Instalação guiada para uso comum em um computador.",
      "portableTitle": "Portable",
      "portableSummary": "Execução de uma pasta extraída, sem instalação separada.",
      "launchLabel": "Como iniciar",
      "setupLaunch": "Execute o Setup e siga o assistente de instalação.",
      "portableLaunch": "Extraia todo o ZIP e execute PTenoLauncher.exe nessa pasta.",
      "dataLabel": "Dados",
      "portableData": "ao lado do .exe",
      "fileLabel": "Arquivo",
      "sizeLabel": "Tamanho",
      "downloadSetup": "Baixar Setup",
      "downloadPortable": "Baixar Portable",
      "unavailable": "Downloads diretos indisponíveis. Consulte os lançamentos no GitHub.",
      "notConfirmed": "Os hashes dos dois pacotes não foram confirmados na configuração do site. Botões diretos ocultos.",
      "confirmed": "Os dois pacotes e seus SHA‑256 foram confirmados para esta versão.",
      "notes": "Notas da versão ↗",
      "all": "Todos os lançamentos ↗",
      "shaTitle": "SHA‑256 e verificação de arquivos",
      "shaHelp": "Compare o SHA‑256 do arquivo baixado com o SHA256SUMS.txt publicado. Não execute arquivos de origem não verificada.",
      "sumsLocation": "(na página da versão)"
    },
    "features": {
      "overline": "PARA SEUS MODPACKS",
      "title": "Recursos do launcher",
      "intro": "As descrições valem para a versão pública 0.2.2, não para recursos novos disponíveis só localmente."
    },
    "status": {
      "overline": "ANTES DE INSTALAR",
      "title": "Status e limitações",
      "intro": "A Development Preview é voltada para testes. Veja os resultados das verificações e as limitações desta versão.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Os testes automatizados verificam a inicialização do Portable após a extração completa. O responsável pelo projeto testou manualmente o login da Microsoft com verificação da licença e a inicialização do Minecraft offline."
    },
    "guide": {
      "overline": "PASSO A PASSO",
      "title": "Primeira execução",
      "intro": "No Portable, extraia todos os arquivos juntos; não mova apenas o .exe.",
      "setupTitle": "Com instalador",
      "portableTitle": "Sem instalação",
      "setupSteps": [
        "Baixe o Setup no GitHub e confira o hash.",
        "Execute o instalador e siga o assistente.",
        "Abra o PTenoLauncher, escolha o idioma e vá para os perfis."
      ],
      "portableSteps": [
        "Baixe o ZIP no GitHub e confira o SHA‑256.",
        "Extraia todo o ZIP para uma pasta de sua escolha.",
        "Execute PTenoLauncher.exe ali; os dados ficam em portable-data."
      ]
    },
    "faq": {
      "overline": "DÚVIDAS?",
      "title": "FAQ",
      "items": [
        {
          "question": "Qual Windows é necessário?",
          "answer": "Esta prévia foi feita para Windows 11 x64. Outros sistemas não são declarados compatíveis."
        },
        {
          "question": "Preciso ter Minecraft Java Edition?",
          "answer": "Para jogar, você precisa de uma conta Microsoft com acesso ao Minecraft Java Edition."
        },
        {
          "question": "E se o Windows mostrar um aviso?",
          "answer": "Os arquivos da prévia não são assinados. Confira o lançamento no GitHub e SHA‑256 antes de executar. Não abra arquivos de origem não verificada."
        },
        {
          "question": "Onde posso relatar um erro?",
          "answer": "Crie uma GitHub Issue com a versão 0.2.2, passos, Windows e logs sem segredos. Nunca publique tokens ou senhas."
        }
      ]
    },
    "footer": {
      "disclaimer": "Projeto independente. Não é um produto oficial do Minecraft nem afiliado à Mojang ou à Microsoft.",
      "ai": "Desenvolvido com auxílio de IA (OpenAI Codex); o autor define os rumos do projeto.",
      "code": "Código-fonte ↗",
      "releases": "Lançamentos ↗",
      "issues": "Reportar problema ↗",
      "top": "Voltar ao topo ↑"
    },
    "labels": {
      "available": "Na versão",
      "verified": "Testado manualmente",
      "unreleased": "Ainda não publicado",
      "deferred": "Adiado",
      "caution": "Limitação"
    },
    "featureCards": [
      {
        "title": "Perfis do Minecraft",
        "body": "Pastas e configurações separadas evitam misturar mods, ajustes e mundos entre versões."
      },
      {
        "title": "Mods",
        "body": "Catálogo Modrinth, carrinho de instalação e importação de .jar locais com revisão antes de instalar."
      },
      {
        "title": "Console do jogo",
        "body": "Logs do Minecraft e do Java ajudam a diagnosticar falhas de inicialização e mods."
      },
      {
        "title": "Gerenciamento de Java",
        "body": "Preparação do Java necessário e reutilização de arquivos locais verificados."
      },
      {
        "title": "Pacotes de recursos e shaders",
        "body": "O gerenciamento por perfil está em desenvolvimento; as ações exatas de 0.2.2 não estão confirmadas."
      },
      {
        "title": "Servidores locais",
        "body": "O servidor local de Minecraft 1.21.1 com Forge 52.1.0 inicia no Java 21 de 64 bits após a verificação dos arquivos preparados."
      }
    ],
    "statusItems": [
      {
        "title": "Login Microsoft e licença",
        "body": "O responsável pelo projeto fez login com a Microsoft e confirmou o acesso ao Minecraft Java Edition."
      },
      {
        "title": "Jogo offline",
        "body": "O responsável pelo projeto conseguiu iniciar o Minecraft sem internet com uma conta licenciada que já estava autenticada."
      },
      {
        "title": "Loaders",
        "body": "Vanilla, Fabric e Quilt estão documentados no código. A inicialização do servidor Minecraft 1.21.1 com Forge 52.1.0 foi verificada. Outros núcleos de servidor, NeoForge e o cliente Forge precisam de testes separados."
      },
      {
        "title": "CurseForge e atualizações",
        "body": "Catálogo CurseForge, importação ampliada de modpacks e atualização automática não foram confirmados nesta versão pública."
      },
      {
        "title": "Assistente de IA e tradução de mods",
        "body": "Em desenvolvimento; código experimental não significa recurso publicado e pronto."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Adiado; compatibilidade entre loaders não é garantida."
      },
      {
        "title": "Alertas do Windows",
        "body": "Os pacotes de prévia não são assinados. Verifique origem e SHA‑256 em vez de ignorar alertas para arquivos não verificados."
      }
    ]
  },
  "ru": {
    "general": {
      "skip": "Перейти к содержимому",
      "navigation": "Основная навигация",
      "language": "Язык сайта",
      "auto": "Автоматически"
    },
    "nav": {
      "downloads": "Загрузки",
      "features": "Возможности",
      "status": "Статус",
      "faq": "FAQ"
    },
    "hero": {
      "eyebrow": "Независимый лаунчер Minecraft Java Edition",
      "description": "Управляйте профилями и модами Minecraft в одном месте. Перед установкой проверьте возможности и ограничения именно этой сборки.",
      "preview": "Development Preview",
      "action": "Выбрать пакет",
      "secondary": "Проверить статус выпуска",
      "note": "Это предварительная версия: работа всех функций не гарантируется."
    },
    "downloads": {
      "overline": "НАЧНИТЕ ЗДЕСЬ",
      "title": "Скачать PTenoLauncher",
      "intro": "Одна сборка, два формата. Выберите удобный способ запуска.",
      "versionLabel": "Версия",
      "dateLabel": "Дата выпуска",
      "setupTitle": "Установщик",
      "setupSummary": "Мастер установки для обычного использования на одном компьютере.",
      "portableTitle": "Portable",
      "portableSummary": "Запуск из распакованной папки без отдельной установки.",
      "launchLabel": "Как запустить",
      "setupLaunch": "Запустите Setup и выполните шаги мастера.",
      "portableLaunch": "Распакуйте весь ZIP и откройте PTenoLauncher.exe из этой папки.",
      "dataLabel": "Данные",
      "portableData": "рядом с .exe",
      "fileLabel": "Файл",
      "sizeLabel": "Размер",
      "downloadSetup": "Скачать Setup",
      "downloadPortable": "Скачать Portable",
      "unavailable": "Прямые загрузки недоступны. Смотрите выпуски на GitHub.",
      "notConfirmed": "Контрольные суммы обоих пакетов не подтверждены в конфигурации сайта. Прямые кнопки скрыты.",
      "confirmed": "Оба пакета и SHA‑256 подтверждены для этого выпуска.",
      "notes": "Примечания к выпуску ↗",
      "all": "Все выпуски ↗",
      "shaTitle": "SHA‑256 и проверка файлов",
      "shaHelp": "Сравните SHA‑256 скачанного файла с опубликованным SHA256SUMS.txt. Не запускайте файлы из непроверенных источников.",
      "sumsLocation": "(на странице выпуска)"
    },
    "features": {
      "overline": "ДЛЯ ВАШИХ СБОРОК",
      "title": "Возможности лаунчера",
      "intro": "Описание относится к публичной версии 0.2.2, а не к новым локальным функциям."
    },
    "status": {
      "overline": "ПЕРЕД УСТАНОВКОЙ",
      "title": "Статус и ограничения",
      "intro": "Development Preview предназначен для тестирования. Ниже указано, что проверено и какие ограничения действуют.",
      "bannerTitle": "Development Preview · 0.2.2",
      "bannerText": "Автоматические тесты проверяют запуск Portable после полной распаковки. Владелец проекта вручную проверил вход через Microsoft с проверкой лицензии и запуск Minecraft офлайн."
    },
    "guide": {
      "overline": "ПОШАГОВО",
      "title": "Первый запуск",
      "intro": "Для Portable распакуйте все файлы вместе; не переносите только .exe.",
      "setupTitle": "С установщиком",
      "portableTitle": "Без установки",
      "setupSteps": [
        "Загрузите Setup с GitHub и проверьте контрольную сумму.",
        "Запустите установщик и следуйте мастеру.",
        "Откройте PTenoLauncher, выберите язык и перейдите к профилям."
      ],
      "portableSteps": [
        "Загрузите ZIP с GitHub и проверьте SHA‑256.",
        "Полностью распакуйте ZIP в выбранную папку.",
        "Запустите PTenoLauncher.exe оттуда; данные будут в portable-data."
      ]
    },
    "faq": {
      "overline": "ЕСТЬ ВОПРОСЫ?",
      "title": "FAQ",
      "items": [
        {
          "question": "Какая Windows требуется?",
          "answer": "Эта предварительная версия предназначена для Windows 11 x64. Поддержка других систем не заявлена."
        },
        {
          "question": "Нужно ли покупать Minecraft Java Edition?",
          "answer": "Для игры нужна учётная запись Microsoft с доступом к Minecraft Java Edition."
        },
        {
          "question": "Что делать при предупреждении Windows?",
          "answer": "Файлы Preview не подписаны. Перед запуском проверьте выпуск на GitHub и SHA‑256. Не запускайте файлы с неподтверждённым происхождением."
        },
        {
          "question": "Куда сообщать об ошибках?",
          "answer": "Создайте GitHub Issue: укажите 0.2.2, шаги, Windows и очищенный от секретов лог. Не публикуйте токены и пароли."
        }
      ]
    },
    "footer": {
      "disclaimer": "Независимый проект. Не является официальным продуктом Minecraft и не связан с Mojang или Microsoft.",
      "ai": "Разрабатывается с помощью ИИ (OpenAI Codex); направление работы определяет автор.",
      "code": "Исходный код ↗",
      "releases": "Выпуски ↗",
      "issues": "Сообщить о проблеме ↗",
      "top": "Наверх ↑"
    },
    "labels": {
      "available": "В выпуске",
      "verified": "Проверено вручную",
      "unreleased": "Ещё не опубликовано",
      "deferred": "Отложено",
      "caution": "Ограничение"
    },
    "featureCards": [
      {
        "title": "Профили Minecraft",
        "body": "Раздельные папки и настройки уменьшают риск смешивания модов, конфигураций и миров."
      },
      {
        "title": "Моды",
        "body": "Каталог Modrinth, корзина установки и локальные .jar с просмотром перед установкой."
      },
      {
        "title": "Игровая консоль",
        "body": "Логи Minecraft и Java помогают разобраться с ошибками запуска и модов."
      },
      {
        "title": "Управление Java",
        "body": "Подготовка нужной Java и повторное использование проверенных локальных файлов."
      },
      {
        "title": "Ресурспаки и шейдеры",
        "body": "Управление для отдельных профилей разрабатывается; точные действия в 0.2.2 не подтверждены."
      },
      {
        "title": "Локальные серверы",
        "body": "Локальный сервер Minecraft 1.21.1 с Forge 52.1.0 запускается на 64-битной Java 21 после проверки подготовленных файлов."
      }
    ],
    "statusItems": [
      {
        "title": "Вход Microsoft и лицензия",
        "body": "Владелец проекта успешно вошёл через Microsoft и подтвердил право на Minecraft Java Edition."
      },
      {
        "title": "Офлайн-игра",
        "body": "Владелец проекта успешно запустил Minecraft без интернета с ранее авторизованной учётной записью с лицензией."
      },
      {
        "title": "Загрузчики",
        "body": "Vanilla, Fabric и Quilt описаны в исходном коде. Подтверждён запуск сервера Minecraft 1.21.1 с Forge 52.1.0. Другие серверные ядра, NeoForge и клиент Forge требуют отдельных проверок."
      },
      {
        "title": "CurseForge и обновления",
        "body": "Каталог CurseForge, расширенный импорт сборок и автообновление не подтверждены для этого выпуска."
      },
      {
        "title": "ИИ-ассистент и перевод модов",
        "body": "В разработке; экспериментальный код не означает готовую опубликованную функцию."
      },
      {
        "title": "Sinytra, Kilt, Cross-Loader",
        "body": "Отложено; совместимость между загрузчиками не гарантируется."
      },
      {
        "title": "Предупреждения Windows",
        "body": "Пакеты Preview не имеют цифровой подписи. Проверяйте источник и SHA‑256; не игнорируйте предупреждения для непроверенных файлов."
      }
    ]
  }
});
