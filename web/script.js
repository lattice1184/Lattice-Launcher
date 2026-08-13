/* Lattice Launcher 落地页 —— 一次性方向性动画触发器
   原则：进入视口即播、播完即止（unobserve / disconnect），不循环。 */
(function () {
  'use strict';

  // 通用一次性 reveal：Hero 流程线 / 自修复 emblem / 截图帧 / 下载卡
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      e.target.classList.add('is-played', 'revealed');
      io.unobserve(e.target);
    }
  }, { threshold: 0.25 });

  document.querySelectorAll('.launch-rail, .repair-emblem, .shot, [data-reveal]').forEach((el) => io.observe(el));

  // 自修复 emblem：扫描线播完后加 .repaired（描边红→青，一次性终态）
  const emblem = document.querySelector('.repair-emblem');
  if (emblem) {
    emblem.addEventListener('animationend', (e) => {
      if (e.target.classList.contains('scan')) emblem.classList.add('repaired');
    });
  }

  // 画廊滚动进度线：进入视口后随滚动向下拉长，到达底部解绑（一次性，不跟随回滚）
  const sec = document.querySelector('.gallery-sec');
  if (sec) {
    const line = sec.querySelector('.gallery-progress-line');
    const io2 = new IntersectionObserver(([e]) => {
      if (!e.isIntersecting) return;
      line.classList.add('active');
      io2.disconnect();
      let ticking = false;
      const upd = () => {
        const r = sec.getBoundingClientRect();
        const p = Math.min(Math.max((innerHeight - r.top) / r.height, 0), 1);
        line.style.setProperty('--p', p.toFixed(3));
        if (p >= 1) window.removeEventListener('scroll', upd, { passive: true });
      };
      window.addEventListener('scroll', upd, { passive: true });
      upd();
    }, { threshold: 0 });
    io2.observe(sec);
  }

  // 导航滚动后加深 + 返回顶部按钮
  const nav = document.getElementById('nav');
  const backTop = document.getElementById('backTop');
  const onScroll = () => {
    const y = window.scrollY;
    if (nav) nav.classList.toggle('scrolled', y > 40);
    if (backTop) backTop.classList.toggle('show', y > 600);
  };
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
  if (backTop) backTop.addEventListener('click', () => window.scrollTo({ top: 0, behavior: 'smooth' }));
})();
