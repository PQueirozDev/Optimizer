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

// Copiar a chave Pix.
document.querySelectorAll("[data-copy]").forEach((button) => {
  const label = button.querySelector("span");
  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(button.dataset.copy);
      label.textContent = "Copiado!";
    } catch {
      label.textContent = "Selecione e copie";
    }
    setTimeout(() => (label.textContent = "Copiar"), 1800);
  });
});

// Demonstração do app: percorre as etapas em loop enquanto está visível.
const steps = [...document.querySelectorAll(".steps-list li")];
const bar = document.querySelector(".progress span");
const barBox = document.querySelector(".progress");
const barLabel = document.querySelector(".progress-label");
const title = document.querySelector(".app-title");

function showStep(index) {
  steps.forEach((li, i) => {
    li.classList.toggle("done", i < index);
    li.classList.toggle("active", i === index);
  });
  const percent = Math.min(100, Math.round((index / steps.length) * 100));
  bar.style.width = `${percent}%`;
  barBox.setAttribute("aria-valuenow", String(percent));
  barLabel.textContent = `${percent}%`;
  title.firstChild.textContent = index >= steps.length ? "Concluído" : "Otimizando";
  title.querySelector(".dots").hidden = index >= steps.length;
}

if (steps.length) {
  if (reduceMotion) {
    showStep(3);
  } else {
    let current = 0;
    let timer = null;
    const tick = () => {
      showStep(current);
      current = current > steps.length ? 0 : current + 1;
    };
    const demo = document.querySelector(".app-window");
    new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting && !timer) {
        tick();
        timer = setInterval(tick, 1300);
      } else if (!entry.isIntersecting && timer) {
        clearInterval(timer);
        timer = null;
      }
    }).observe(demo);
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
