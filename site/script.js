document.getElementById("year").textContent = new Date().getFullYear();

const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

// Entrada suave dos blocos ao aparecerem na tela.
const reveals = document.querySelectorAll(".reveal");
if (reduceMotion || !("IntersectionObserver" in window)) {
  reveals.forEach((el) => el.classList.add("in"));
} else {
  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        entry.target.classList.add("in");
        observer.unobserve(entry.target);
      }
    },
    { threshold: 0.12, rootMargin: "0px 0px -40px 0px" }
  );
  reveals.forEach((el) => observer.observe(el));
}

// Vídeo da demonstração: só carrega perto da tela, pausa fora dela e, para quem prefere
// menos movimento, mostra a capa e espera o play.
const demoVideo = document.querySelector(".demo-video");
if (demoVideo && demoVideo.tagName === "VIDEO") {
  const demo = demoVideo.closest(".demo");
  const toggle = demo.querySelector(".demo-toggle");
  const source = demoVideo.querySelector("source");
  let userPaused = reduceMotion;
  let visible = false;
  let waiting = reduceMotion;

  demoVideo.muted = true;
  if (reduceMotion) {
    demoVideo.autoplay = false;
    demoVideo.removeAttribute("autoplay");
  }

  const showPoster = () => {
    if (!demoVideo.getAttribute("poster")) demoVideo.setAttribute("poster", demoVideo.dataset.poster);
  };
  const load = () => {
    showPoster();
    if (source.getAttribute("src")) return;
    source.setAttribute("src", source.dataset.src);
    demoVideo.load();
  };
  const sync = () => {
    const paused = demoVideo.paused;
    demo.classList.toggle("is-paused", paused);
    demo.classList.toggle("is-idle", paused && waiting && demoVideo.currentTime === 0);
    toggle.setAttribute("aria-label", paused ? "Reproduzir demonstração" : "Pausar demonstração");
  };
  const play = () => {
    load();
    const attempt = demoVideo.play();
    // Economia de energia ou bloqueio do navegador: fica a capa com o botão de play
    if (attempt) attempt.catch(() => {
      waiting = true;
      sync();
    });
  };

  toggle.hidden = false;
  toggle.addEventListener("click", () => {
    if (demoVideo.paused) {
      userPaused = false;
      play();
    } else {
      userPaused = true;
      demoVideo.pause();
    }
  });
  demoVideo.addEventListener("play", sync);
  demoVideo.addEventListener("pause", sync);
  sync();

  if ("IntersectionObserver" in window) {
    // A capa e o vídeo começam a baixar pouco antes da seção aparecer
    const near = new IntersectionObserver(([entry]) => {
      if (!entry.isIntersecting) return;
      if (reduceMotion) showPoster();
      else load();
      near.disconnect();
    }, { rootMargin: "300px 0px" });
    const onScreen = new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      if (visible && !userPaused) play();
      else if (!visible && !demoVideo.paused) demoVideo.pause();
    }, { threshold: 0.35 });
    // Nada do vídeo concorre com o carregamento inicial da página
    const observe = () => {
      near.observe(demo);
      onScreen.observe(demo);
    };
    if (document.readyState === "complete") observe();
    else window.addEventListener("load", observe, { once: true });
  } else if (reduceMotion) {
    showPoster();
  } else {
    play();
  }
}

// Partículas do hero (canvas leve, pausa fora da tela).
const canvas = document.querySelector(".particles");
if (canvas && canvas.getContext) {
  const ctx = canvas.getContext("2d");
  const hero = canvas.parentElement;
  let dots = [];
  let width = 0;
  let height = 0;
  let running = false;

  function resize() {
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    width = hero.clientWidth;
    height = hero.clientHeight;
    canvas.width = width * ratio;
    canvas.height = height * ratio;
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    const count = Math.round(Math.min(140, (width * height) / 9000));
    dots = Array.from({ length: count }, () => ({
      x: Math.random() * width,
      y: Math.random() * height,
      r: Math.random() * 1.4 + 0.4,
      vx: (Math.random() - 0.5) * 0.12,
      vy: -Math.random() * 0.18 - 0.03,
      a: Math.random() * 0.6 + 0.2,
      blue: Math.random() < 0.28,
    }));
  }

  function draw() {
    ctx.clearRect(0, 0, width, height);
    for (const d of dots) {
      ctx.beginPath();
      ctx.arc(d.x, d.y, d.r, 0, Math.PI * 2);
      ctx.fillStyle = d.blue ? `rgba(92, 155, 255, ${d.a})` : `rgba(255, 255, 255, ${d.a * 0.7})`;
      ctx.fill();
    }
  }

  function frame() {
    if (!running) return;
    for (const d of dots) {
      d.x += d.vx;
      d.y += d.vy;
      if (d.y < -4) {
        d.y = height + 4;
        d.x = Math.random() * width;
      }
      if (d.x < -4) d.x = width + 4;
      if (d.x > width + 4) d.x = -4;
    }
    draw();
    requestAnimationFrame(frame);
  }

  resize();
  draw();
  window.addEventListener("resize", () => {
    resize();
    draw();
  });

  if (!reduceMotion) {
    new IntersectionObserver(([entry]) => {
      const wasRunning = running;
      running = entry.isIntersecting;
      if (running && !wasRunning) requestAnimationFrame(frame);
    }).observe(hero);
  }
}
