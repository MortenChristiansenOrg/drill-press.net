// Apply the saved preference before the first paint; dark is the default.
document.documentElement.classList.add('js');
try {
  const theme = localStorage.getItem('drillpress-manual-theme');
  if (theme === 'light' || theme === 'dark') document.documentElement.dataset.theme = theme;
} catch { /* Storage is optional, including when reading local files. */ }
