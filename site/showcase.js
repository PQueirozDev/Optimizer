// v1.8: fundo animado, brilho e rastro que seguem o mouse, app do topo em 3D e galeria com lightbox.
(() => {
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const finePointer = window.matchMedia("(pointer: fine)").matches;

  // ---------- Camadas de fundo (aurora, grade e brilho do cursor) ----------
  const aurora = document.createElement("div");
  aurora.className = "fx-aurora";
  aurora.setAttribute("aria-hidden", "true");
  aurora.innerHTML = "<i></i><i></i><i></i>";
  const grid = document.createElement("div");
  grid.className = "fx-grid";
  grid.setAttribute("aria-hidden", "true");
  document.body.prepend(grid);
  document.body.prepend(aurora);

  if (!reduce && finePointer) {
    const spot = document.createElement("div");
    spot.className = "fx-spot";
    spot.setAttribute("aria-hidden", "true");
    document.body.prepend(spot);

    // ---------- Rastro do mouse: partículas que brilham e se apagam ----------
    const canvas = document.createElement("canvas");
    canvas.className = "fx-trail";
    canvas.setAttribute("aria-hidden", "true");
    document.body.appendChild(canvas);
    const ctx = canvas.getContext("2d");
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const resize = () => {
      canvas.width = innerWidth * dpr;
      canvas.height = innerHeight * dpr;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    resize();
    window.addEventListener("resize", resize);

    const accent = getComputedStyle(document.documentElement).getPropertyValue("--accent-bright").trim() || "#5c9bff";
    const points = [];
    const sparks = [];
    let mouse = null;
    let last = null;

    window.addEventListener("pointermove", (e) => {
      mouse = { x: e.clientX, y: e.clientY };
      spot.style.setProperty("--mx", e.clientX + "px");
      spot.style.setProperty("--my", e.clientY + "px");
      // Faíscas proporcionais à velocidade do movimento
      if (last) {
        const speed = Math.hypot(e.clientX - last.x, e.clientY - last.y);
        const count = Math.min(3, Math.floor(speed / 18));
        for (let i = 0; i < count; i++) {
          sparks.push({ x: e.clientX, y: e.clientY, vx: (Math.random() - 0.5) * 2.2, vy: (Math.random() - 0.5) * 2.2 - 0.6, life: 1, size: 1 + Math.random() * 1.8 });
        }
      }
      last = mouse;
    }, { passive: true });
    document.addEventListener("pointerleave", () => { mouse = null; last = null; });

    // Clique: uma onda de luz
    const rings = [];
    window.addEventListener("pointerdown", (e) => rings.push({ x: e.clientX, y: e.clientY, r: 4, life: 1 }));

    const frame = () => {
      ctx.clearRect(0, 0, innerWidth, innerHeight);
      if (mouse) points.push({ x: mouse.x, y: mouse.y, life: 1 });
      for (const p of points) p.life -= 0.045;
      while (points.length && points[0].life <= 0) points.shift();

      // Fita luminosa que afina e some no fim
      ctx.lineCap = "round";
      ctx.lineJoin = "round";
      for (let i = 1; i < points.length; i++) {
        const a = points[i - 1], b = points[i];
        ctx.strokeStyle = accent;
        ctx.globalAlpha = b.life * 0.55;
        ctx.lineWidth = b.life * 6;
        ctx.shadowColor = accent;
        ctx.shadowBlur = 16;
        ctx.beginPath();
        ctx.moveTo(a.x, a.y);
        ctx.lineTo(b.x, b.y);
        ctx.stroke();
      }

      ctx.shadowBlur = 10;
      for (let i = sparks.length - 1; i >= 0; i--) {
        const s = sparks[i];
        s.x += s.vx; s.y += s.vy; s.vy += 0.03; s.life -= 0.022;
        if (s.life <= 0) { sparks.splice(i, 1); continue; }
        ctx.globalAlpha = s.life;
        ctx.fillStyle = "#ffffff";
        ctx.beginPath();
        ctx.arc(s.x, s.y, s.size * s.life, 0, Math.PI * 2);
        ctx.fill();
      }

      for (let i = rings.length - 1; i >= 0; i--) {
        const r = rings[i];
        r.r += 3.2; r.life -= 0.035;
        if (r.life <= 0) { rings.splice(i, 1); continue; }
        ctx.globalAlpha = r.life * 0.7;
        ctx.strokeStyle = accent;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(r.x, r.y, r.r, 0, Math.PI * 2);
        ctx.stroke();
      }
      ctx.globalAlpha = 1;
      ctx.shadowBlur = 0;
      requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);

    // ---------- App do topo inclina na direção do mouse ----------
    const stage = document.querySelector(".hero-stage");
    const hero = document.querySelector(".hero");
    if (stage && hero) {
      hero.addEventListener("pointermove", (e) => {
        const rect = hero.getBoundingClientRect();
        const x = (e.clientX - rect.left) / rect.width - 0.5;
        const y = (e.clientY - rect.top) / rect.height - 0.5;
        stage.style.setProperty("--ry", (x * 12).toFixed(2) + "deg");
        stage.style.setProperty("--rx", (-y * 8).toFixed(2) + "deg");
      });
      hero.addEventListener("pointerleave", () => {
        stage.style.setProperty("--ry", "0deg");
        stage.style.setProperty("--rx", "0deg");
      });
    }

    // Aurora reage levemente à rolagem (profundidade)
    window.addEventListener("scroll", () => {
      aurora.style.transform = `translate3d(0, ${(-scrollY * 0.08).toFixed(1)}px, 0)`;
    }, { passive: true });
  }

  // ---------- Galeria com lightbox ----------
  const shots = [...document.querySelectorAll(".shot")];
  const box = document.querySelector(".lightbox");
  if (!box || shots.length === 0) return;
  const img = box.querySelector("img");
  const caption = box.querySelector("figcaption");
  let index = 0;
  let opener = null;

  const show = (i) => {
    index = (i + shots.length) % shots.length;
    const shot = shots[index];
    img.src = shot.dataset.full;
    img.alt = shot.dataset.caption;
    caption.textContent = `${shot.dataset.caption} · ${index + 1}/${shots.length}`;
    // Reinicia a animação de zoom
    img.style.animation = "none";
    void img.offsetWidth;
    img.style.animation = "";
  };
  const open = (i, trigger) => {
    opener = trigger;
    show(i);
    box.hidden = false;
    document.body.style.overflow = "hidden";
    box.querySelector(".lb-close").focus();
  };
  const close = () => {
    box.hidden = true;
    document.body.style.overflow = "";
    opener?.focus();
  };

  shots.forEach((shot, i) => shot.addEventListener("click", () => open(i, shot)));
  box.querySelector(".lb-close").addEventListener("click", close);
  box.querySelector(".lb-prev").addEventListener("click", () => show(index - 1));
  box.querySelector(".lb-next").addEventListener("click", () => show(index + 1));
  box.addEventListener("click", (e) => { if (e.target === box) close(); });
  document.addEventListener("keydown", (e) => {
    if (box.hidden) return;
    if (e.key === "Escape") close();
    else if (e.key === "ArrowLeft") show(index - 1);
    else if (e.key === "ArrowRight") show(index + 1);
  });
})();
