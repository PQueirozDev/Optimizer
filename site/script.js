const menu = document.querySelector('.menu-toggle');
const nav = document.querySelector('.nav');
menu?.addEventListener('click', () => {
  const open = nav.classList.toggle('open');
  menu.setAttribute('aria-expanded', String(open));
});
nav?.querySelectorAll('a').forEach(link => link.addEventListener('click', () => nav.classList.remove('open')));

const revealItems = document.querySelectorAll('.section-wrap > *, .feature, .plan, .visual-card, .patch-list article, .support-card, .guidelines-list');
revealItems.forEach(item => item.classList.add('reveal'));
const observer = new IntersectionObserver(entries => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('is-visible');
      observer.unobserve(entry.target);
    }
  });
}, { threshold: 0.12 });
revealItems.forEach(item => observer.observe(item));

document.querySelectorAll('.feature, .plan, .visual-card').forEach(card => {
  card.addEventListener('pointermove', event => {
    const box = card.getBoundingClientRect();
    card.style.setProperty('--pointer-x', `${((event.clientX - box.left) / box.width) * 100}%`);
    card.style.setProperty('--pointer-y', `${((event.clientY - box.top) / box.height) * 100}%`);
  });
});
