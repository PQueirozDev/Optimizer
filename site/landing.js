(() => {
  "use strict";
  const menu = document.querySelector('.menu-toggle');
  const nav = document.getElementById('nav');
  const closeMenu = () => {
    nav?.classList.remove('open');
    menu?.setAttribute('aria-expanded', 'false');
    menu?.setAttribute('aria-label', 'Abrir menu');
  };
  menu?.addEventListener('click', () => {
    const open = menu.getAttribute('aria-expanded') !== 'true';
    nav?.classList.toggle('open', open);
    menu.setAttribute('aria-expanded', String(open));
    menu.setAttribute('aria-label', open ? 'Fechar menu' : 'Abrir menu');
  });
  nav?.addEventListener('click', event => { if (event.target.closest('a')) closeMenu(); });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && menu?.getAttribute('aria-expanded') === 'true') { closeMenu(); menu.focus(); }
  });
  // O destino vem apenas das releases oficiais. Sem rede, mantém a página de download do GitHub.
  const validRelease = value => {
    if (!value || !/^v?\d+\.\d+\.\d+$/.test(value.version ?? '')) return false;
    try {
      const url = new URL(value.url);
      return url.origin === 'https://github.com' && url.pathname.startsWith('/PQueirozDev/Optimizer/releases/download/') && /\/Qrztweaks-Setup-v[\d.]+\.exe$/.test(url.pathname);
    } catch { return false; }
  };
  const applyRelease = release => {
    if (!validRelease(release)) return;
    document.querySelectorAll('[data-download-link]').forEach(link => {
      link.href = release.url;
      link.setAttribute('aria-label', `Baixar instalador do Qrztweaks ${release.version} para Windows de 64 bits`);
    });
    document.querySelectorAll('[data-version]').forEach(label => { label.textContent = release.version; });
  };
  const cacheKey = 'qrztweaks-official-download';
  try {
    const cached = JSON.parse(sessionStorage.getItem(cacheKey) || 'null');
    if (cached && Date.now() - cached.at < 600000) applyRelease(cached);
  } catch { /* Armazenamento opcional. */ }
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 8000);
  fetch('https://api.github.com/repos/PQueirozDev/Optimizer/releases/latest', {
    headers: { Accept: 'application/vnd.github+json' }, signal: controller.signal
  }).then(response => response.ok ? response.json() : null).then(release => {
    if (!release || release.draft || release.prerelease) return;
    const setup = release.assets?.find(asset => /^Qrztweaks-Setup-v[\d.]+\.exe$/.test(asset.name));
    if (!setup) return;
    const info = { version: release.tag_name, url: setup.browser_download_url, at: Date.now() };
    if (!validRelease(info)) return;
    applyRelease(info);
    try { sessionStorage.setItem(cacheKey, JSON.stringify(info)); } catch { /* Cache opcional. */ }
  }).catch(() => {
    document.querySelectorAll('[data-download-link]').forEach(link => {
      if (!link.href.startsWith('https://github.com/PQueirozDev/Optimizer/releases/download/')) link.setAttribute('aria-label', 'Abrir o download oficial do Qrztweaks no GitHub');
    });
  }).finally(() => clearTimeout(timeout));
})();
