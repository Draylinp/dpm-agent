  const t = (clave) => I18N.t(clave);
  const chat = document.getElementById('chat');
  const trace = document.getElementById('trace');
  const form = document.getElementById('form');
  const input = document.getElementById('input');
  const send = document.getElementById('send');
  const telefono = document.getElementById('telefono');
  const idiomaPaciente = document.getElementById('idiomaPaciente');
  let sesionId = null;
  let ultimoSaliente = 0;

  function nuevaConversacion() {
    sesionId = null;
    ultimoSaliente = 0;
    telefono.disabled = false;
    idiomaPaciente.textContent = '';
    chat.replaceChildren();
    trace.replaceChildren();
  }

  document.getElementById('nueva').addEventListener('click', nuevaConversacion);

  function add(cls, text, etiqueta) {
    const div = document.createElement('div');
    div.className = 'msg ' + cls;
    if (etiqueta) {
      const small = document.createElement('small');
      small.className = 'voz';
      small.textContent = etiqueta;
      div.appendChild(small);
    }
    div.appendChild(document.createTextNode(text));
    chat.appendChild(div);
    chat.scrollTop = chat.scrollHeight;
    return div;
  }

  function addStep(p) {
    const div = document.createElement('div');
    div.className = 'step';
    const b = document.createElement('b');
    b.textContent = p.herramienta;
    const pre = document.createElement('pre');
    pre.textContent = 'args: ' + p.argumentos + '\n→ ' + p.resultado;
    div.append(b, pre);
    trace.appendChild(div);
    trace.scrollTop = trace.scrollHeight;
  }

  function mostrarRespuesta(data) {
    sesionId = data.sesionId;
    // Una sesión queda atada a su número, igual que un chat de WhatsApp.
    telefono.disabled = true;
    if (data.idioma) idiomaPaciente.textContent = `${t('chat.idioma_detectado')}: ${data.idioma}`;
    (data.pasos || []).forEach(addStep);
    add('bot', data.respuesta);
  }

  // Errores que el servidor ya explica en el idioma del paciente; si no trae texto, uno genérico amable.
  class ErrorAmable extends Error {}

  async function leerError(res) {
    try {
      const data = await res.json();
      return new ErrorAmable(data.error || t('chat.error_generico'));
    } catch {
      return new ErrorAmable(t('chat.error_generico'));
    }
  }

  function mostrarError(err) {
    add('err', err instanceof ErrorAmable ? err.message : t('chat.sin_conexion'));
  }

  // Indicador "escribiendo…" si la respuesta tarda más de medio segundo.
  let temporizadorEscribiendo = null;
  function escribiendo(activo) {
    clearTimeout(temporizadorEscribiendo);
    document.getElementById('escribiendo')?.remove();
    if (!activo) return;
    temporizadorEscribiendo = setTimeout(() => {
      const div = add('bot escribiendo', t('chat.escribiendo'));
      div.id = 'escribiendo';
    }, 500);
  }

  // Todas las llamadas del chat: con el idioma de la interfaz y el indicador mientras esperan.
  async function llamar(url, opciones) {
    escribiendo(true);
    try {
      const res = await fetch(url, { ...opciones, headers: { ...(opciones.headers || {}), 'Accept-Language': I18N.idioma } });
      if (!res.ok) throw await leerError(res);
      return await res.json();
    } finally {
      escribiendo(false);
    }
  }

  // En la demo cada número tiene una conversación fija ("demo-" + número): así la oferta automática
  // de un turno liberado aparece al escribir con el número de quien está en la lista de espera.
  function asegurarSesion() {
    if (sesionId || !demo) return;
    const digitos = telefono.value.replace(/\D/g, '');
    if (digitos.length >= 7) sesionId = 'demo-' + digitos.slice(-10);
  }

  async function enviarTexto(mensaje) {
    asegurarSesion();
    add('user', mensaje);
    send.disabled = true;
    try {
      mostrarRespuesta(await llamar('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sesionId, mensaje, telefono: telefono.value.trim() || null, idioma: I18N.idioma })
      }));
    } catch (err) {
      mostrarError(err);
    } finally {
      send.disabled = false;
      input.focus();
    }
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const mensaje = input.value.trim();
    if (!mensaje) return;
    input.value = '';
    await enviarTexto(mensaje);
  });

  // Mensajes que el sistema envía solo (ofertas de turnos liberados, recordatorios).
  async function revisarSalientes() {
    if (!sesionId) return;
    try {
      const res = await fetch(`/api/chat/${encodeURIComponent(sesionId)}/salientes?despuesDe=${ultimoSaliente}`);
      if (!res.ok) return;
      for (const m of await res.json()) {
        ultimoSaliente = Math.max(ultimoSaliente, m.id);
        add('bot', m.texto, m.tipo === 'recordatorio' ? t('chat.recordatorio') : t('chat.automatico'));
      }
    } catch {
      // Sin conexión: se intenta en la siguiente vuelta.
    }
  }
  setInterval(revisarSalientes, 5000);

  // Foto del carnet de la ARS: se lee, queda pendiente y el agente pide confirmación.
  const foto = document.getElementById('foto');
  const archivoFoto = document.getElementById('archivoFoto');
  foto.addEventListener('click', () => archivoFoto.click());
  archivoFoto.addEventListener('change', async () => {
    const archivo = archivoFoto.files[0];
    archivoFoto.value = '';
    if (!archivo) return;
    add('user', archivo.name, t('chat.foto_carnet'));
    send.disabled = foto.disabled = true;
    try {
      mostrarRespuesta(await llamar('/api/pacientes/carnet', { method: 'POST', body: formulario('imagen', archivo) }));
    } catch (err) {
      mostrarError(err);
    } finally {
      send.disabled = foto.disabled = false;
    }
  });

  function formulario(campo, archivo) {
    asegurarSesion();
    const datos = new FormData();
    datos.append(campo, archivo, archivo.name || 'archivo');
    if (sesionId) datos.append('sesionId', sesionId);
    if (telefono.value.trim()) datos.append('telefono', telefono.value.trim());
    datos.append('idioma', I18N.idioma);
    return datos;
  }

  // Notas de voz: se graban en el navegador y se envían a /api/chat/audio.
  const grabar = document.getElementById('grabar');
  const MAX_SEGUNDOS = 120;
  let grabadora = null;
  let corte = null;

  function formatoGrabacion() {
    const opciones = ['audio/ogg;codecs=opus', 'audio/webm;codecs=opus', 'audio/webm', 'audio/mp4'];
    return opciones.find(x => window.MediaRecorder && MediaRecorder.isTypeSupported(x)) || '';
  }

  if (!navigator.mediaDevices || !window.MediaRecorder) {
    grabar.disabled = true;
    grabar.dataset.i18nTitle = 'chat.sin_microfono';
  }

  grabar.addEventListener('click', async () => {
    if (grabadora) {
      grabadora.stop();
      return;
    }
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
      add('err', t('chat.error_microfono') + err.message);
      return;
    }
    const partes = [];
    const tipo = formatoGrabacion();
    grabadora = new MediaRecorder(stream, tipo ? { mimeType: tipo } : undefined);
    grabadora.ondataavailable = (e) => { if (e.data.size) partes.push(e.data); };
    grabadora.onstop = async () => {
      clearTimeout(corte);
      stream.getTracks().forEach(x => x.stop());
      const blob = new Blob(partes, { type: grabadora.mimeType });
      grabadora = null;
      grabar.classList.remove('activo');
      grabar.textContent = '🎤';
      await enviarAudio(blob);
    };
    grabadora.start();
    grabar.classList.add('activo');
    grabar.textContent = '■';
    corte = setTimeout(() => grabadora && grabadora.stop(), MAX_SEGUNDOS * 1000);
  });

  async function enviarAudio(blob) {
    if (!blob.size) return;
    const pendiente = add('user', '…', t('chat.nota_voz'));
    send.disabled = grabar.disabled = true;
    try {
      const data = await llamar('/api/chat/audio', { method: 'POST', body: formulario('audio', blob) });
      pendiente.lastChild.textContent = data.transcripcion;
      mostrarRespuesta(data);
    } catch (err) {
      pendiente.lastChild.textContent = t('chat.no_procesado');
      mostrarError(err);
    } finally {
      send.disabled = grabar.disabled = false;
    }
  }

  // Modo demo: banner, reinicio y ejemplos para probar en 30 segundos.
  let demo = null;

  let notaActual = null;

  function pintarNota() {
    const p = document.getElementById('notaDemo');
    const texto = notaActual && (notaActual[I18N.idioma] || notaActual.en);
    p.hidden = !texto;
    p.textContent = texto || '';
  }

  function pintarEjemplos() {
    pintarNota();
    const caja = document.getElementById('ejemplos');
    caja.querySelectorAll('button').forEach(b => b.remove());
    if (!demo) return;
    for (const ej of demo.ejemplos) {
      const b = document.createElement('button');
      b.type = 'button';
      b.textContent = ej.etiqueta[I18N.idioma] || ej.etiqueta.en;
      b.addEventListener('click', async () => {
        if (ej.telefono !== telefono.value.trim() || telefono.disabled) {
          nuevaConversacion();
          telefono.value = ej.telefono;
        }
        notaActual = ej.nota || null;
        pintarNota();
        await enviarTexto(ej.mensaje[I18N.idioma] || ej.mensaje.en);
      });
      caja.insertBefore(b, document.getElementById('carnetDemo'));
    }
  }

  async function cargarDemo() {
    try {
      const res = await fetch('/api/demo');
      if (!res.ok) return;
      demo = await res.json();
      if (!demo.habilitado) { demo = null; return; }
      document.getElementById('banner').hidden = false;
      document.getElementById('ejemplos').hidden = false;
      if (demo.usuarioPanel) {
        document.getElementById('panelDemo').hidden = false;
        document.getElementById('credencialesDemo').textContent = `${demo.usuarioPanel} / ${demo.clavePanel}`;
      }
      pintarEjemplos();
    } catch {
      demo = null;
    }
  }

  document.getElementById('reiniciar').addEventListener('click', async () => {
    const res = await fetch('/api/demo/reiniciar', { method: 'POST' });
    nuevaConversacion();
    notaActual = null;
    pintarNota();
    add(res.ok ? 'bot' : 'err', res.ok ? t('demo.reiniciada') : (await leerError(res)).message);
  });

  I18N.alCambiar(pintarEjemplos);
  cargarDemo();
