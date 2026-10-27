  function mostrarIdioma() {
    document.querySelectorAll('article[data-idioma]').forEach(a => { a.hidden = a.dataset.idioma !== I18N.idioma; });
  }
  I18N.alCambiar(mostrarIdioma);
  document.addEventListener('DOMContentLoaded', mostrarIdioma);
