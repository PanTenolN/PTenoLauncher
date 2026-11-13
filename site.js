(() => {
  "use strict";
  const release = window.PTENO_RELEASE;
  const packs = window.PTENO_TRANSLATIONS;
  if (!release || !packs) {
    console.error("PTenoLauncher: release.js or translations.js could not be loaded");
    return; // Fail closed: download links have the hidden attribute in the HTML.
  }

  const languages = ["uk", "en", "pl", "de", "es", "fr", "pt-BR", "ru"];
  const localeNames = {uk:"uk-UA",en:"en-GB",pl:"pl-PL",de:"de-DE",es:"es-ES",fr:"fr-FR","pt-BR":"pt-BR",ru:"ru-RU"};
  const storageKey = "ptenolauncher.website.language";
  const languageSelect = document.getElementById("language");
  const getStoredLanguage = () => {
    try { return localStorage.getItem(storageKey); } catch { return null; }
  };
  const storeLanguage = (value) => {
    try { localStorage.setItem(storageKey, value); } catch { /* Private browsing can disable storage. */ }
  };
  const supportedLanguage = (value) => languages.includes(value) || value === "auto";
  const matchLanguage = (candidate) => {
    if (typeof candidate !== "string") return null;
    const normal = candidate.toLowerCase().replaceAll("_", "-");
    const base = normal.split("-")[0];
    if (base === "pt") return "pt-BR";
    return languages.find(lang => lang.toLowerCase() === normal || lang.toLowerCase() === base) || null;
  };
  const detectBrowserLanguage = () => {
    const candidates = Array.isArray(navigator.languages) && navigator.languages.length
      ? navigator.languages : [navigator.language];
    for (const candidate of candidates) {
      const matched = matchLanguage(candidate);
      if (matched) return matched;
    }
    return "en";
  };
  const selectedPreference = getStoredLanguage();
  let preference = supportedLanguage(selectedPreference) ? selectedPreference : "auto";
  languageSelect.value = preference;

  const byPath = (root, path) => path.split(".").reduce((parent, key) => parent?.[key], root);
  const setText = (id, text) => {
    const el = document.getElementById(id);
    if (el) el.textContent = String(text);
  };
  const node = (tag, className, content) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (content !== undefined) element.textContent = content;
    return element;
  };
  const appendParagraph = (parent, value) => parent.append(node("p", "", value));
  const validHash = value => typeof value === "string" && /^[a-f0-9]{64}$/i.test(value);
  const validMetadata = () => {
    const a = release.packages?.setup;
    const b = release.packages?.portable;
    if (!a || !b || release.assetsVerified !== true) return false;
    if (!/^\d+\.\d+\.\d+$/.test(release.version) || !/^v[\w.\-]+$/.test(release.tag)) return false;
    if (release.repository !== "PanTenolN/PTenoLauncher") return false;
    if (!release.tag.startsWith(`v${release.version}-preview.`)) return false;
    if (a.file !== `PTenoLauncher-Setup-${release.version}-win-x64.exe`) return false;
    if (b.file !== `PTenoLauncher-Portable-${release.version}-win-x64.zip`) return false;
    if (![a.sizeMiB, b.sizeMiB].every(n => Number.isFinite(n) && n > 0)) return false;
    return validHash(a.sha256) && validHash(b.sha256) && a.sha256.toLowerCase() !== b.sha256.toLowerCase();
  };
  const downloadAvailable = validMetadata();
  const releaseBase = `https://github.com/${release.repository}/releases`;
  const releasePage = `${releaseBase}/tag/${encodeURIComponent(release.tag)}`;
  const releaseAsset = file => `${releaseBase}/download/${encodeURIComponent(release.tag)}/${encodeURIComponent(file)}`;
  document.getElementById("release-link").href = releasePage;
  document.getElementById("sums-link").href = downloadAvailable ? releaseAsset("SHA256SUMS.txt") : releasePage;

  // No unverified asset URLs are exposed as download buttons.
  for (const kind of ["setup", "portable"]) {
    const asset = release.packages?.[kind];
    const link = document.getElementById(`${kind}-download`);
    const fallback = document.getElementById(`${kind}-unavailable`);
    const hash = document.getElementById(`${kind}-hash`);
    if (asset) {
      setText(`${kind}-file`, asset.file);
      if (validHash(asset.sha256) && downloadAvailable) hash.textContent = asset.sha256.toLowerCase();
    }
    link.hidden = !downloadAvailable;
    if (downloadAvailable) {
      link.href = releaseAsset(asset.file);
      link.setAttribute("download", asset.file);
    } else link.removeAttribute("href");
    fallback.hidden = downloadAvailable;
  }

  const featureIcons = ["▦", "◈", ">_", "⌘", "▧", "◉"];
  const featureStates = ["available", "available", "available", "available", "unreleased", "available"];
  const statusStates = ["verified", "verified", "caution", "unreleased", "unreleased", "deferred", "caution"];
  const fillFeatures = t => {
    const target = document.getElementById("feature-grid");
    target.replaceChildren();
    t.featureCards.forEach((entry, i) => {
      const card = node("article", "feature-card");
      const icon = node("div", "feature-icon", featureIcons[i]); icon.setAttribute("aria-hidden", "true");
      card.append(icon, node("h3", "", entry.title));
      appendParagraph(card, entry.body);
      const state = featureStates[i];
      card.append(node("span", `feature-tag ${state === "unreleased" ? "later" : ""}`, t.labels[state]));
      target.append(card);
    });
  };
  const fillStatus = t => {
    const target = document.getElementById("status-grid");
    target.replaceChildren();
    t.statusItems.forEach((entry, i) => {
      const kind = statusStates[i];
      const card = node("article", "status-item");
      const head = node("div", "status-item-head");
      head.append(node("h3", "", entry.title), node("span", `state-tag ${kind}`, t.labels[kind]));
      card.append(head);
      appendParagraph(card, entry.body);
      target.append(card);
    });
  };
  const fillSteps = (id, entries) => {
    const list = document.getElementById(id);
    list.replaceChildren(...entries.map(text => node("li", "", text)));
  };
  const fillFaq = t => {
    const target = document.getElementById("faq-list");
    // Preserve which questions were expanded while changing language.
    const openIndices = [...target.children].flatMap((el, i) => el.open ? [i] : []);
    target.replaceChildren();
    t.faq.items.forEach((entry, i) => {
      const item = node("details");
      item.open = openIndices.includes(i);
      item.append(node("summary", "", entry.question));
      appendParagraph(item, entry.answer);
      target.append(item);
    });
  };
  const formatDate = lang => {
    const parsed = new Date(`${release.date}T12:00:00Z`);
    if (Number.isNaN(parsed.getTime())) return release.date;
    try { return new Intl.DateTimeFormat(localeNames[lang], {day:"numeric",month:"long",year:"numeric",timeZone:"UTC"}).format(parsed); }
    catch { return release.date; }
  };
  const formatMiB = (value, lang) => {
    try { return new Intl.NumberFormat(localeNames[lang], {minimumFractionDigits:1,maximumFractionDigits:1}).format(value) + " MiB"; }
    catch { return String(value) + " MiB"; }
  };
  const render = () => {
    const lang = preference === "auto" ? detectBrowserLanguage() : preference;
    const t = packs[lang] || packs.en;
    document.documentElement.lang = lang;
    document.title = `PTenoLauncher — Minecraft Java Edition · ${release.version}`;
    const description = document.querySelector('meta[name="description"]');
    if (description) description.content = t.hero.description;
    for (const el of document.querySelectorAll("[data-i18n]")) {
      const translated = byPath(t, el.dataset.i18n);
      if (typeof translated === "string") el.textContent = translated;
    }
    for (const el of document.querySelectorAll("[data-i18n-aria]")) {
      const translated = byPath(t, el.dataset.i18nAria);
      if (typeof translated === "string") el.setAttribute("aria-label", translated);
    }
    languageSelect.options[0].textContent = t.general.auto;
    setText("release-version", `${release.version} ${release.channel}`);
    setText("release-date", formatDate(lang));
    document.getElementById("release-date").dateTime = release.date;
    for (const kind of ["setup", "portable"]) {
      const asset = release.packages?.[kind];
      if (asset && Number.isFinite(asset.sizeMiB)) setText(`${kind}-size`, formatMiB(asset.sizeMiB, lang));
    }
    setText("download-verification", downloadAvailable ? t.downloads.confirmed : t.downloads.notConfirmed);
    fillFeatures(t);
    fillStatus(t);
    fillSteps("guide-setup", t.guide.setupSteps);
    fillSteps("guide-portable", t.guide.portableSteps);
    fillFaq(t);
    setText("year", new Date().getFullYear());
  };
  languageSelect.addEventListener("change", () => {
    const candidate = languageSelect.value;
    if (!supportedLanguage(candidate)) return;
    preference = candidate;
    storeLanguage(preference);
    render();
  });
  render();
})();
