// Tamaño de letra ajustable (muchos pacientes son personas mayores). Un botón #tamanoLetra recorre
// 100 %, 115 % y 130 %; todo el CSS usa rem, así que la página entera escala. Se recuerda en el navegador.
(function () {
  const CLAVE = 'dpm.letra';
  const ESCALAS = [1, 1.15, 1.3];
  let indice = 0;
  try { indice = Math.max(0, ESCALAS.indexOf(parseFloat(localStorage.getItem(CLAVE)))); } catch { /* sin almacenamiento */ }

  function aplicar() {
    document.documentElement.style.fontSize = (16 * ESCALAS[indice]) + 'px';
    const boton = document.getElementById('tamanoLetra');
    if (boton) {
      const etiqueta = (window.I18N ? I18N.t('a11y.letra') : 'Tamaño de letra') + ': ' + Math.round(ESCALAS[indice] * 100) + ' %';
      boton.setAttribute('aria-label', etiqueta);
      boton.title = etiqueta;
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    const boton = document.getElementById('tamanoLetra');
    if (boton) {
      boton.addEventListener('click', () => {
        indice = (indice + 1) % ESCALAS.length;
        try { localStorage.setItem(CLAVE, String(ESCALAS[indice])); } catch { /* navegación privada */ }
        aplicar();
      });
    }
    if (window.I18N) I18N.alCambiar(aplicar);
    aplicar();
  });
  aplicar();
})();
