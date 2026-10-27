// Diálogos accesibles del panel, en lugar de alert/prompt/confirm del navegador: <dialog> modal con título,
// foco dentro del diálogo, Escape para cerrar y el foco vuelve al botón que lo abrió. Textos con I18N.
(function () {
  const t = (clave) => (window.I18N ? I18N.t(clave) : clave);
  let secuencia = 0;

  function crear(texto, campo) {
    const id = 'dlg' + (++secuencia);
    const dialogo = document.createElement('dialog');
    dialogo.className = 'dialogo';
    dialogo.setAttribute('aria-labelledby', id + '-t');
    dialogo.setAttribute('aria-describedby', id + '-d');

    const titulo = document.createElement('h2');
    titulo.id = id + '-t';
    titulo.textContent = t('dlg.titulo');
    const descripcion = document.createElement('p');
    descripcion.id = id + '-d';
    descripcion.textContent = texto;
    dialogo.append(titulo, descripcion);

    let entrada = null;
    if (campo) {
      const etiqueta = document.createElement('label');
      etiqueta.htmlFor = id + '-i';
      etiqueta.textContent = campo;
      entrada = document.createElement('input');
      entrada.id = id + '-i';
      entrada.autocomplete = 'off';
      dialogo.append(etiqueta, entrada);
    }

    const fila = document.createElement('div');
    fila.className = 'fila-acciones';
    dialogo.append(fila);
    document.body.append(dialogo);
    return { dialogo, entrada, fila };
  }

  function abrir({ dialogo, entrada, fila }, botones) {
    const anterior = document.activeElement;
    return new Promise((resolver) => {
      const cerrar = (valor) => {
        dialogo.close();
        dialogo.remove();
        if (anterior && anterior.focus) anterior.focus();
        resolver(valor);
      };
      for (const b of botones) {
        const boton = document.createElement('button');
        boton.type = 'button';
        boton.textContent = t(b.texto);
        if (b.clase) boton.className = b.clase;
        boton.addEventListener('click', () => cerrar(b.valor()));
        fila.append(boton);
      }
      dialogo.addEventListener('cancel', (e) => { e.preventDefault(); cerrar(null); });
      if (entrada) entrada.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); cerrar(botones[0].valor()); } });
      dialogo.showModal();
      (entrada || fila.querySelector('button')).focus();
    });
  }

  window.Dialogo = {
    /** Mensaje con un solo botón "Aceptar". */
    aviso(texto) {
      const partes = crear(texto);
      return abrir(partes, [{ texto: 'dlg.aceptar', valor: () => true }]);
    },
    /** Confirmación sí/no; devuelve true o false. */
    confirmar(texto, textoSi = 'dlg.confirmar') {
      const partes = crear(texto);
      return abrir(partes, [{ texto: textoSi, clase: 'peligro', valor: () => true }, { texto: 'dlg.cancelar', clase: 'sec', valor: () => false }]);
    },
    /** Pide escribir un texto (por ejemplo, la palabra BORRAR); devuelve lo escrito o null si cancela. */
    pedirTexto(texto, etiquetaCampo) {
      const partes = crear(texto, etiquetaCampo);
      return abrir(partes, [
        { texto: 'dlg.confirmar', clase: 'peligro', valor: () => partes.entrada.value.trim() },
        { texto: 'dlg.cancelar', clase: 'sec', valor: () => null },
      ]);
    },
  };
})();
