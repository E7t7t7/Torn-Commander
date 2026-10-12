// Torn Commander example helper.
// It does not click anything or make network requests.
// Enable it from Settings if you want a small visual badge on Torn pages.
(() => {
  if (!location.hostname.endsWith('torn.com')) return;
  if (document.getElementById('tc-example-badge')) return;
  const badge = document.createElement('div');
  badge.id = 'tc-example-badge';
  badge.textContent = 'TC';
  Object.assign(badge.style, {
    position: 'fixed',
    right: '8px',
    bottom: '8px',
    zIndex: '2147483647',
    padding: '4px 7px',
    borderRadius: '6px',
    background: 'rgba(30,34,42,.78)',
    color: '#ddd',
    font: '11px Segoe UI, sans-serif',
    pointerEvents: 'none'
  });
  document.documentElement.appendChild(badge);
})();
