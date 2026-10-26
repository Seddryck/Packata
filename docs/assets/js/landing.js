// Enhance the linked examples into tabs; without JavaScript every example is readable.
(() => {
  const steps = document.querySelector('.workflow-steps');
  if (!steps) return;
  const tabs = [...steps.querySelectorAll('a')];
  const panels = tabs.map(tab => document.querySelector(tab.getAttribute('href')));
  if (panels.some(panel => !panel)) return;

  steps.setAttribute('role', 'tablist');
  const narrowScreen = window.matchMedia('(max-width: 580px)');
  const setOrientation = () => steps.setAttribute('aria-orientation', narrowScreen.matches ? 'vertical' : 'horizontal');
  setOrientation();
  narrowScreen.addEventListener('change', setOrientation);
  const select = (index, focus = false) => {
    tabs.forEach((tab, position) => {
      const selected = position === index;
      tab.setAttribute('aria-selected', String(selected));
      tab.tabIndex = selected ? 0 : -1;
      panels[position].hidden = !selected;
    });
    if (focus) tabs[index].focus();
  };

  tabs.forEach((tab, index) => {
    tab.setAttribute('role', 'tab');
    tab.setAttribute('aria-controls', panels[index].id);
    panels[index].setAttribute('role', 'tabpanel');
    tab.addEventListener('click', event => {
      event.preventDefault();
      select(index);
    });
    tab.addEventListener('keydown', event => {
      let next;
      switch (event.key) {
        case 'ArrowRight': if (narrowScreen.matches) return; next = (index + 1) % tabs.length; break;
        case 'ArrowLeft': if (narrowScreen.matches) return; next = (index + tabs.length - 1) % tabs.length; break;
        case 'ArrowDown': if (!narrowScreen.matches) return; next = (index + 1) % tabs.length; break;
        case 'ArrowUp': if (!narrowScreen.matches) return; next = (index + tabs.length - 1) % tabs.length; break;
        case 'Home': next = 0; break;
        case 'End': next = tabs.length - 1; break;
        case ' ': event.preventDefault(); select(index); return;
        default: return;
      }
      event.preventDefault();
      select(next, true);
    });
  });
  // Honor a direct link to a stage; otherwise start with the approved preview state.
  const hashIndex = tabs.findIndex(tab => tab.hash === window.location.hash);
  select(hashIndex >= 0 ? hashIndex : 2);
  window.addEventListener('hashchange', () => {
    const index = tabs.findIndex(tab => tab.hash === window.location.hash);
    if (index >= 0) select(index);
  });
})();
