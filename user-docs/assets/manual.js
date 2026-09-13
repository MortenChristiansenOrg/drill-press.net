(() => {
  const root = document.documentElement;
  const status = document.querySelector('#status');
  const themeButton = document.querySelector('#theme-toggle');
  const updateThemeLabel = () => {
    const dark = root.dataset.theme !== 'light';
    themeButton.textContent = dark ? 'Light' : 'Dark';
    themeButton.setAttribute('aria-label', dark ? 'Switch to light mode' : 'Switch to dark mode');
  };
  document.querySelectorAll('[data-enhancement]').forEach(element => element.hidden = false);
  updateThemeLabel();
  themeButton.addEventListener('click', () => {
    root.dataset.theme = root.dataset.theme === 'light' ? 'dark' : 'light';
    try { localStorage.setItem('drillpress-manual-theme', root.dataset.theme); } catch { /* Optional. */ }
    updateThemeLabel();
  });

  const menu = document.querySelector('#menu-toggle');
  const closeMenu = () => {
    document.body.classList.remove('nav-open');
    menu.setAttribute('aria-expanded', 'false');
  };
  menu.addEventListener('click', () => {
    const expanded = document.body.classList.toggle('nav-open');
    menu.setAttribute('aria-expanded', String(expanded));
  });

  const keywords = new Set('using var new return public private static class sealed async await if else is not null true false string int bool void foreach in typeof nameof where default'.split(' '));
  function highlight(code) {
    const original = code.textContent;
    const tokens = /\/\/[^\n]*|\/\*[\s\S]*?\*\/|"""[\s\S]*?"""|\$?"(?:\\.|[^"\\])*"|\b\d+\b|\b[A-Za-z_]\w*\b/g;
    const fragment = document.createDocumentFragment();
    let offset = 0;
    for (const token of original.matchAll(tokens)) {
      fragment.append(document.createTextNode(original.slice(offset, token.index)));
      const value = token[0];
      const kind = value.startsWith('//') || value.startsWith('/*') ? 'comment'
        : value.startsWith('"') || value.startsWith('$"') ? 'string'
        : /^\d+$/.test(value) ? 'number' : keywords.has(value) ? 'keyword' : null;
      if (kind) {
        const span = document.createElement('span');
        span.className = 'tok-' + kind;
        span.textContent = value;
        fragment.append(span);
      } else fragment.append(document.createTextNode(value));
      offset = token.index + value.length;
    }
    fragment.append(document.createTextNode(original.slice(offset)));
    code.replaceChildren(fragment);
  }
  document.querySelectorAll('.codeblock').forEach(block => {
    const code = block.querySelector('code');
    const original = code.textContent;
    if (block.querySelector('pre[data-check="program"], pre[data-check="library"]')) highlight(code);
    const copy = document.createElement('button');
    copy.type = 'button';
    copy.className = 'copy';
    copy.textContent = 'Copy';
    copy.setAttribute('aria-label', 'Copy ' + block.querySelector('.codehead span').textContent);
    copy.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(original);
        copy.textContent = 'Copied';
        status.textContent = 'Code copied to clipboard.';
      } catch {
        const range = document.createRange();
        range.selectNodeContents(code);
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(range);
        copy.textContent = 'Selected';
        status.textContent = 'Clipboard unavailable. Code selected; use your browser’s Copy command.';
      }
      setTimeout(() => copy.textContent = 'Copy', 2200);
    });
    block.querySelector('.codehead').append(copy);
  });

  const dialog = document.querySelector('#search-dialog');
  const input = document.querySelector('#search-input');
  const results = document.querySelector('#search-results');
  const count = document.querySelector('#search-count');
  const entries = window.DRILLPRESS_SEARCH || [];
  function search() {
    const terms = input.value.toLowerCase().trim().split(/\s+/).filter(Boolean);
    const matches = entries.filter(entry => terms.every(term => (entry.title + ' ' + entry.text).toLowerCase().includes(term)))
      .sort((a, b) => Number(terms.every(term => b.title.toLowerCase().includes(term))) - Number(terms.every(term => a.title.toLowerCase().includes(term))));
    results.replaceChildren();
    matches.slice(0, 30).forEach(entry => {
      const item = document.createElement('li');
      const link = document.createElement('a');
      link.href = entry.url;
      link.textContent = entry.title;
      const summary = document.createElement('span');
      summary.textContent = entry.text.slice(0, 140);
      link.append(summary);
      item.append(link);
      results.append(item);
    });
    count.textContent = matches.length ? (matches.length > 30 ? 'Showing 30 of ' : '') + matches.length + ' results' : 'No results. Try an API name such as CodeType, fixes, or baseline.';
  }
  function openSearch() { closeMenu(); search(); dialog.showModal(); input.focus(); }
  document.querySelector('#search-open').addEventListener('click', openSearch);
  document.querySelector('#search-close').addEventListener('click', () => dialog.close());
  input.addEventListener('input', search);
  input.addEventListener('keydown', event => {
    if (event.key === 'ArrowDown') { event.preventDefault(); results.querySelector('a')?.focus(); }
  });
  results.addEventListener('click', event => { if (event.target.closest('a')) dialog.close(); });
  document.addEventListener('keydown', event => {
    const editing = event.target instanceof Element && (event.target.matches('input, textarea, select') || event.target.isContentEditable);
    if ((event.key === '/' && !editing) || (event.key.toLowerCase() === 'k' && (event.ctrlKey || event.metaKey))) {
      event.preventDefault();
      if (!dialog.open) openSearch();
    }
    if (event.key === 'Escape') closeMenu();
  });
})();
