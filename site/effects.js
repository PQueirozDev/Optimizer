// Efeitos e interações da página: progresso de rolagem, menu, abas de planos, contadores,
// palavra rotativa, brilho nos cards, inclinação dos planos, FAQ suave e versão atual.
// Tudo respeita prefers-reduced-motion e a página continua completa sem JavaScript.
(() => {
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const finePointer = window.matchMedia("(pointer: fine)").matches;

  // --- Rolagem: barra de progresso, topo compacto, voltar ao topo e link ativo ---------
  const root = document.documentElement;
  const topbar = document.querySelector(".topbar");
  const toTop = document.querySelector(".to-top");
  const navLinks = [...document.querySelectorAll(".nav a[href^='#']")];
  const sections = navLinks.map((a) => document.querySelector(a.getAttribute("href"))).filter(Boolean);
  let ticking = false;

  const onScroll = () => {
    ticking = false;
    const max = root.scrollHeight - root.clientHeight;
    root.style.setProperty("--progress", max > 0 ? (root.scrollTop / max).toFixed(4) : "0");
    topbar?.classList.toggle("scrolled", root.scrollTop > 8);
    toTop?.classList.toggle("show", root.scrollTop > 900);

    const probe = root.scrollTop + window.innerHeight * 0.35;
    let current = null;
    for (const section of sections) if (section.offsetTop <= probe) current = section;
    navLinks.forEach((a) => a.classList.toggle("active", current !== null && a.getAttribute("href") === `#${current.id}`));
  };
  window.addEventListener("scroll", () => {
    if (!ticking) { ticking = true; requestAnimationFrame(onScroll); }
  }, { passive: true });
  onScroll();
  toTop?.addEventListener("click", () => window.scrollTo({ top: 0, behavior: reduce ? "auto" : "smooth" }));

  // --- Menu no celular ---------------------------------------------------------------------
  const nav = document.getElementById("nav");
  const menuToggle = document.querySelector(".menu-toggle");
  const setMenu = (open) => {
    nav.classList.toggle("open", open);
    menuToggle.setAttribute("aria-expanded", String(open));
    menuToggle.setAttribute("aria-label", open ? "Fechar menu" : "Abrir menu");
  };
  menuToggle?.addEventListener("click", () => setMenu(!nav.classList.contains("open")));
  nav?.addEventListener("click", (e) => { if (e.target.closest("a")) setMenu(false); });
  document.addEventListener("keydown", (e) => { if (e.key === "Escape" && nav?.classList.contains("open")) setMenu(false); });

  // --- Abas de produtos (Optimizer / Windows) ----------------------------------------------
  const tabs = [...document.querySelectorAll(".tab")];
  const indicator = document.querySelector(".tab-indicator");
  const moveIndicator = (tab) => {
    if (!indicator || !tab) return;
    indicator.style.width = `${tab.offsetWidth}px`;
    indicator.style.transform = `translateX(${tab.offsetLeft}px)`;
  };
  const selectTab = (tab, focus = false) => {
    for (const other of tabs) {
      const selected = other === tab;
      other.classList.toggle("is-active", selected);
      other.setAttribute("aria-selected", String(selected));
      other.tabIndex = selected ? 0 : -1;
      const panel = document.getElementById(other.getAttribute("aria-controls"));
      panel.hidden = !selected;
      panel.classList.toggle("is-active", selected);
      if (selected && !reduce) {
        panel.classList.remove("is-entering");
        void panel.offsetWidth; // reinicia a animação de entrada
        panel.classList.add("is-entering");
      }
    }
    moveIndicator(tab);
    if (focus) tab.focus();
  };
  tabs.forEach((tab, i) => {
    tab.addEventListener("click", () => selectTab(tab));
    tab.addEventListener("keydown", (e) => {
      if (e.key !== "ArrowRight" && e.key !== "ArrowLeft") return;
      e.preventDefault();
      selectTab(tabs[(i + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length], true);
    });
  });
  const placeIndicator = () => moveIndicator(tabs.find((t) => t.classList.contains("is-active")));
  if (document.fonts?.ready) document.fonts.ready.then(placeIndicator);
  placeIndicator();
  window.addEventListener("resize", placeIndicator);

  // --- Contadores dos números -----------------------------------------------------------
  const counters = document.querySelectorAll("[data-count]");
  const countUp = (el) => {
    const target = Number(el.dataset.count);
    const start = performance.now();
    const duration = 1400;
    const step = (now) => {
      const t = Math.min(1, (now - start) / duration);
      el.textContent = String(Math.round(target * (1 - Math.pow(1 - t, 3))));
      if (t < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  };
  if (!reduce && "IntersectionObserver" in window) {
    const seen = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        countUp(entry.target);
        seen.unobserve(entry.target);
      }
    }, { threshold: 0.6 });
    counters.forEach((el) => { el.textContent = "0"; seen.observe(el); });
  }

  // --- Palavra rotativa do título (efeito de digitação) -----------------------------------
  const rotator = document.querySelector(".rotator");
  if (rotator && !reduce) {
    const words = rotator.dataset.words.split("|");
    let index = 0;
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
    // Leitores de tela ouvem o título completo uma vez, não cada letra digitada
    const title = rotator.closest("h1");
    title.setAttribute("aria-label", title.textContent.replace(/\s+/g, " ").trim());
    rotator.setAttribute("aria-hidden", "true");
    (async () => {
      await sleep(2600);
      rotator.classList.add("typing");
      for (;;) {
        const word = words[index];
        for (let i = word.length; i >= 0; i--) { rotator.textContent = word.slice(0, i) || "​"; await sleep(38); }
        index = (index + 1) % words.length;
        const next = words[index];
        for (let i = 1; i <= next.length; i++) { rotator.textContent = next.slice(0, i); await sleep(70); }
        await sleep(2400);
      }
    })();
  }

  // --- Brilho que segue o mouse nos cards e inclinação dos planos ------------------------
  if (finePointer && !reduce) {
    document.querySelectorAll(".spotlight, .plan").forEach((el) => {
      el.addEventListener("pointermove", (e) => {
        const r = el.getBoundingClientRect();
        el.style.setProperty("--mx", `${e.clientX - r.left}px`);
        el.style.setProperty("--my", `${e.clientY - r.top}px`);
      });
    });
    document.querySelectorAll(".tilt").forEach((el) => {
      el.addEventListener("pointermove", (e) => {
        const r = el.getBoundingClientRect();
        const x = (e.clientX - r.left) / r.width - 0.5;
        const y = (e.clientY - r.top) / r.height - 0.5;
        el.style.setProperty("--ry", `${(x * 5).toFixed(2)}deg`);
        el.style.setProperty("--rx", `${(-y * 5).toFixed(2)}deg`);
      });
      el.addEventListener("pointerleave", () => {
        el.style.setProperty("--rx", "0deg");
        el.style.setProperty("--ry", "0deg");
      });
    });
  }

  // --- FAQ com abertura e fechamento suaves -----------------------------------------------
  if (!reduce && "animate" in Element.prototype) {
    document.querySelectorAll(".faq details").forEach((details) => {
      const summary = details.querySelector("summary");
      const body = details.querySelector(".faq-body");
      if (!body) return;
      let animation = null;
      summary.addEventListener("click", (e) => {
        e.preventDefault();
        animation?.cancel();
        if (details.open) {
          animation = body.animate([{ height: `${body.offsetHeight}px`, opacity: 1 }, { height: "0px", opacity: 0 }], { duration: 260, easing: "ease" });
          animation.onfinish = () => { details.open = false; animation = null; };
        } else {
          details.open = true;
          animation = body.animate([{ height: "0px", opacity: 0 }, { height: `${body.offsetHeight}px`, opacity: 1 }], { duration: 320, easing: "cubic-bezier(0.22, 1, 0.36, 1)" });
          animation.onfinish = () => { animation = null; };
        }
      });
    });
  }

  // --- Versão atual (da release mais recente no GitHub, com cache na sessão) ------------
  const versionTargets = document.querySelectorAll("[data-version]");
  const showVersion = (tag) => { if (/^v?\d+\.\d+\.\d+$/.test(tag)) versionTargets.forEach((el) => { el.textContent = tag.startsWith("v") ? tag : `v${tag}`; }); };
  try {
    const cached = sessionStorage.getItem("pq-version");
    if (cached) showVersion(cached);
    else fetch("https://api.github.com/repos/PQueirozDev/Optimizer/releases/latest", { headers: { Accept: "application/vnd.github+json" } })
      .then((r) => (r.ok ? r.json() : null))
      .then((release) => {
        if (!release?.tag_name) return;
        showVersion(release.tag_name);
        try { sessionStorage.setItem("pq-version", release.tag_name); } catch { /* armazenamento indisponível */ }
      })
      .catch(() => { /* sem rede: fica a versão escrita na página */ });
  } catch { /* sessionStorage bloqueado: fica a versão escrita na página */ }
})();
