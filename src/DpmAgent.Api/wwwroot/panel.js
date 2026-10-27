  const $ = (id) => document.getElementById(id);
  const t = (clave) => I18N.t(clave);
  let tab = 'citas';
  I18N.alCambiar(() => { if (!$('app').hidden) cargar(); });

  function el(tag, props = {}, ...hijos) {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(props)) {
      if (k === 'class') e.className = v;
      else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
      else if (v !== undefined && v !== null) e.setAttribute(k, v);
    }
    for (const h of hijos.flat()) e.append(h instanceof Node ? h : document.createTextNode(h ?? ''));
    return e;
  }

  async function api(ruta, opciones = {}) {
    const res = await fetch(ruta, {
      ...opciones,
      headers: opciones.body ? { 'Content-Type': 'application/json' } : undefined,
      credentials: 'same-origin',
    });
    if (res.status === 401) { mostrarLogin(); throw new Error(t('panel.sesion_vencida')); }
    const texto = await res.text();
    const datos = texto ? JSON.parse(texto) : null;
    if (!res.ok) throw new Error((datos && (datos.error || datos.detail)) || res.statusText);
    return datos;
  }

  function mostrarLogin() { $('app').hidden = true; $('login').hidden = false; }

  async function mostrarApp() {
    const yo = await api('/api/auth/yo');
    $('quien').textContent = `${yo.nombre} (${yo.usuario})`;
    $('login').hidden = true;
    $('app').hidden = false;
    await cargar();
  }

  $('formLogin').addEventListener('submit', async (e) => {
    e.preventDefault();
    $('errorLogin').textContent = '';
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ usuario: $('usuario').value.trim(), clave: $('clave').value }),
    });
    $('clave').value = '';
    if (!res.ok) { $('errorLogin').textContent = t('panel.login_error'); return; }
    await mostrarApp();
  });

  $('salir').addEventListener('click', async () => {
    await fetch('/api/auth/logout', { method: 'POST' });
    mostrarLogin();
  });

  $('tabs').addEventListener('click', async (e) => {
    const b = e.target.closest('button[data-tab]');
    if (!b) return;
    tab = b.dataset.tab;
    document.querySelectorAll('#tabs button').forEach(x => {
      x.classList.toggle('activa', x === b);
      if (x === b) x.setAttribute('aria-current', 'page'); else x.removeAttribute('aria-current');
    });
    await cargar();
  });

  async function cargar() {
    $('error').textContent = '';
    const vista = $('vista');
    try {
      const contenido = await ({ citas, escalamientos, carnets, autorizaciones, urgencias, seguimientos, llamadas, medicos, familias, pacientes, costos, bitacora })[tab]();
      vista.replaceChildren(contenido);
    } catch (err) {
      $('error').textContent = err.message;
    }
  }

  async function accion(fn) {
    $('error').textContent = '';
    try { await fn(); await cargar(); } catch (err) { $('error').textContent = err.message; }
  }

  const hoy = () => new Date(Date.now() - 4 * 3600 * 1000).toISOString().slice(0, 10);
  let fechaCitas = hoy();

  async function citas() {
    const lista = await api(`/api/panel/citas?fecha=${fechaCitas}`);
    const fecha = el('input', { type: 'date', value: fechaCitas, 'aria-label': t('citas.fecha'), onchange: async (e) => { fechaCitas = e.target.value; await cargar(); } });
    const filas = lista.map(c => el('tr', {},
      el('td', {}, c.hora),
      el('td', {}, c.paciente, el('div', { class: 'muted' }, c.telefono)),
      el('td', {}, c.ars || '—', el('div', { class: 'muted' }, c.numeroAfiliado || '')),
      el('td', {}, c.medico),
      el('td', {}, c.motivo),
      el('td', {}, el('span', { class: 'chip' }, c.estado), c.asistenciaConfirmada ? el('div', { class: 'muted' }, t('citas.asistencia')) : '',
        c.reprogramacion ? el('div', { class: 'muted' }, t('rep.estado.' + c.reprogramacion)) : '',
        c.ausencias ? el('div', { class: 'muted' }, t('fam.ausencias').replace('{n}', c.ausencias)) : ''),
      el('td', { class: 'fila-acciones' }, c.estado === 'confirmada'
        ? el('button', { class: 'sec', onclick: () => accion(() => api(`/api/panel/autorizaciones`, { method: 'POST', body: JSON.stringify({ citaId: c.id }) })) }, t('citas.borrador'))
        : '',
        c.estado === 'confirmada' && new Date(c.fechaHora) < new Date()
          ? el('button', { class: 'sec', 'aria-label': `${t('citas.no_asistio')} · ${c.paciente} ${c.hora}`,
              onclick: () => accion(() => api(`/api/panel/citas/${c.id}/no-asistio`, { method: 'POST' })) }, t('citas.no_asistio'))
          : '')));
    return el('div', {},
      el('div', { class: 'fila-acciones' }, el('label', {}, t('citas.fecha') + ' '), fecha),
      lista.length ? el('div', { class: 'tabla' }, el('table', {},
        el('thead', {}, el('tr', {}, ['citas.hora', 'citas.paciente', 'citas.ars', 'citas.medico', 'citas.motivo', 'citas.estado'].map(k => el('th', {}, t(k))), el('th', {}))),
        el('tbody', {}, filas))) : el('p', { class: 'vacio' }, t('citas.vacio')));
  }

  async function escalamientos() {
    const lista = await api('/api/panel/escalamientos');
    if (!lista.length) return el('p', { class: 'vacio' }, t('esc.vacio'));
    return el('div', {}, lista.map(e => el('div', { class: 'item' },
      el('div', { class: 'fila-acciones' },
        e.urgente ? el('span', { class: 'chip urgente' }, t('esc.urgente')) : '',
        el('strong', {}, e.id),
        e.idioma ? el('span', { class: 'chip' }, e.idioma) : '',
        el('span', { class: 'muted' }, fecha(e.creado))),
      // Resumen en el idioma del panel; el mensaje del paciente, tal como lo escribió.
      el('p', {}, I18N.idioma === 'en' ? (e.motivoEn || e.motivo) : e.motivo),
      e.mensajeOriginal ? el('div', { class: 'original', title: t('esc.original') }, e.mensajeOriginal) : '',
      el('p', { class: 'muted' }, `${t('esc.paciente')}: ${e.pacienteId || t('esc.sin_identificar')} · ${t('esc.conversacion')}: ${e.sesionId}`),
      el('button', { onclick: () => accion(() => api(`/api/panel/escalamientos/${e.id}/atender`, { method: 'POST' })) }, t('esc.atender')))));
  }

  async function carnets() {
    const lista = await api('/api/carnets');
    if (!lista.length) return el('p', { class: 'vacio' }, t('carnet.vacio'));
    return el('div', {}, lista.map(c => {
      const nombre = el('input', { value: c.datos.nombre || '' });
      const ars = el('input', { value: c.datos.ars || '' });
      const afiliado = el('input', { value: c.datos.numeroAfiliado || '' });
      return el('div', { class: 'item' },
        el('div', { class: 'fila-acciones' }, el('strong', {}, c.id),
          el('span', { class: 'muted' }, `${t('carnet.tel')} ${c.telefonoCanal} · ${c.pacienteId || t('carnet.nuevo')}`)),
        el('div', { class: 'grid' },
          el('div', {}, el('label', {}, t('carnet.nombre')), nombre),
          el('div', {}, el('label', {}, t('carnet.ars')), ars),
          el('div', {}, el('label', {}, t('carnet.afiliado')), afiliado)),
        el('div', { class: 'fila-acciones separado' },
          el('button', { onclick: () => accion(() => api(`/api/carnets/${c.id}/confirmar`, { method: 'POST',
            body: JSON.stringify({ nombre: nombre.value, ars: ars.value, numeroAfiliado: afiliado.value }) })) }, t('carnet.confirmar')),
          el('button', { class: 'sec', onclick: () => accion(() => api(`/api/carnets/${c.id}/descartar`, { method: 'POST' })) }, t('carnet.descartar'))));
    }));
  }

  async function autorizaciones() {
    const lista = await api('/api/panel/autorizaciones');
    if (!lista.length) return el('p', { class: 'vacio' }, t('aut.vacio'));
    return el('div', {}, lista.map(a => {
      const texto = el('textarea', {}, a.contenido);
      return el('div', { class: 'item' },
        el('div', { class: 'fila-acciones' }, el('strong', {}, a.id),
          el('span', { class: 'chip' }, a.estado),
          el('span', { class: 'muted' }, `${a.citaId} · ${a.paciente} · ${a.ars || t('aut.sin_ars')}`)),
        a.faltantes.length ? el('p', { class: 'error' }, t('aut.falta') + a.faltantes.join(', ')) : '',
        texto,
        el('p', { class: 'muted' }, t('aut.nota')),
        el('div', { class: 'fila-acciones' },
          el('button', { class: 'sec', onclick: () => accion(() => api(`/api/panel/autorizaciones/${a.id}`, { method: 'PUT', body: JSON.stringify({ contenido: texto.value }) })) }, t('aut.guardar')),
          el('button', { onclick: () => accion(() => api(`/api/panel/autorizaciones/${a.id}/aprobar`, { method: 'POST', body: JSON.stringify({ contenido: texto.value }) })) }, t('aut.aprobar')),
          el('button', { class: 'peligro', onclick: () => accion(() => api(`/api/panel/autorizaciones/${a.id}/descartar`, { method: 'POST' })) }, t('aut.descartar'))));
    }));
  }

  const fecha = (iso) => new Date(iso).toLocaleString(I18N.idioma === 'en' ? 'en-US' : 'es-DO');

  async function costos() {
    const c = await api('/api/panel/costos');
    const dinero = (v) => v === null || v === undefined ? '—' : `${v.toFixed(4)} ${c.moneda}`;
    const fila = (clave, valor) => el('tr', {}, el('th', {}, t(clave)), el('td', {}, valor));
    return el('div', {},
      el('p', { class: 'muted' }, t('costos.periodo')),
      el('table', {}, el('tbody', {},
        fila('costos.conversaciones', String(c.conversaciones)),
        fila('costos.citas', String(c.citasAgendadas)),
        fila('costos.llamadas', String(c.llamadas)),
        fila('costos.tokens', `${c.tokensEntrada} / ${c.tokensSalida}`),
        fila('costos.total', dinero(c.costoTotal)),
        fila('costos.por_conversacion', dinero(c.costoPorConversacion)),
        fila('costos.por_cita', dinero(c.costoPorCita)))),
      c.modelosSinPrecio.length ? el('p', { class: 'error' }, t('costos.sin_precio') + c.modelosSinPrecio.join(', ')) : '',
      el('h3', {}, t('costos.por_modelo')),
      el('table', {}, el('tbody', {}, c.porModelo.map(m =>
        el('tr', {}, el('td', {}, m.modelo), el('td', {}, String(m.llamadas)), el('td', {}, `${m.tokensEntrada} / ${m.tokensSalida}`), el('td', {}, dinero(m.costo)))))));
  }

  // Seguimientos indicados por el médico: lista por estado, creación, feriados y bloqueos de agenda.
  let filtroSeguimientos = '';
  async function seguimientos() {
    const lista = await api(`/api/panel/seguimientos${filtroSeguimientos ? '?estado=' + filtroSeguimientos : ''}`);
    const medicos = await api('/api/medicos');
    const filtro = el('select', { 'aria-label': t('seg.filtro'), onchange: (e) => { filtroSeguimientos = e.target.value; cargar(); } },
      ['', 'pendiente', 'contactando', 'agendado', 'sin_respuesta', 'cancelado'].map(v =>
        el('option', { value: v, ...(v === filtroSeguimientos ? { selected: 'selected' } : {}) }, v ? t('seg.estado.' + v) : t('seg.todos'))));

    const campo = (id, etiqueta, control) => el('div', {}, el('label', { for: id }, t(etiqueta)), control);
    const tel = el('input', { id: 'segTel', type: 'tel', placeholder: '809-555-0101' });
    const med = el('select', { id: 'segMed' }, medicos.map(m => el('option', { value: m.id }, m.nombre)));
    const cant = el('input', { id: 'segCant', type: 'number', min: '1', max: '36', value: '3' });
    const unidad = el('select', { id: 'segUni' }, el('option', { value: 'meses' }, t('seg.meses')), el('option', { value: 'semanas' }, t('seg.semanas')));
    const tol = el('input', { id: 'segTol', type: 'number', min: '0', max: '30', value: '5' });
    const rec = el('select', { id: 'segRec' }, el('option', { value: '' }, t('seg.no_repite')),
      ...[1, 3, 6, 12].map(n => el('option', { value: String(n) }, t('seg.cada').replace('{n}', n))));
    const aviso = el('p', { role: 'status' });

    const crear = el('button', { onclick: () => accion(async () => {
      const p = await api(`/api/panel/pacientes?telefono=${encodeURIComponent(tel.value.trim())}`);
      const r = await api('/api/panel/seguimientos', { method: 'POST', body: JSON.stringify({
        pacienteId: p.id, medicoId: med.value, enCantidad: Number(cant.value), enUnidad: unidad.value, toleranciaDias: Number(tol.value),
        motivo: 'control', recurrenciaCada: rec.value ? Number(rec.value) : null, recurrenciaUnidad: rec.value ? 'meses' : null }) });
      if (r.requiereConsentimiento) { telefonoPaciente = tel.value.trim(); await Dialogo.aviso(t('seg.sin_permiso')); }
    }) }, t('seg.crear'));

    const filas = lista.map(s => el('tr', {},
      el('td', {}, s.paciente),
      el('td', {}, s.medico),
      el('td', {}, `${s.fechaObjetivo} (±${s.toleranciaDias})`),
      el('td', {}, el('span', { class: 'chip' }, t('seg.estado.' + s.estado)), s.porLlamada ? el('div', { class: 'muted' }, t('seg.por_llamada')) : ''),
      el('td', {}, s.citaFechaHora ? fecha(s.citaFechaHora) : '—'),
      el('td', {}, s.recurrencia || ''),
      el('td', { class: 'fila-acciones' },
        ['sin_respuesta', 'contactando'].includes(s.estado) ? el('button', { class: 'sec', onclick: () => accion(() => api(`/api/panel/seguimientos/${s.id}/reabrir`, { method: 'POST' })) }, t('seg.reabrir')) : '',
        s.estado !== 'cancelado' ? el('button', { class: 'peligro', onclick: () => accion(() => api(`/api/panel/seguimientos/${s.id}/cancelar`, { method: 'POST' })) }, t('seg.cancelar')) : '')));

    return el('div', {},
      el('h3', {}, t('seg.nuevo')),
      el('div', { class: 'grid' }, campo('segTel', 'pac.telefono', tel), campo('segMed', 'citas.medico', med), campo('segCant', 'seg.en', cant),
        campo('segUni', 'seg.unidad', unidad), campo('segTol', 'seg.tolerancia', tol), campo('segRec', 'seg.recurrencia', rec)),
      el('div', { class: 'fila-acciones' }, crear), aviso,
      el('h3', {}, t('tab.seguimientos')),
      el('div', { class: 'fila-acciones' }, filtro),
      lista.length ? el('div', { class: 'tabla' }, el('table', {},
        el('thead', {}, el('tr', {}, ['citas.paciente', 'citas.medico', 'seg.fecha', 'citas.estado', 'seg.cita', 'seg.recurrencia'].map(k => el('th', {}, t(k))), el('th', {}))),
        el('tbody', {}, filas))) : el('p', { class: 'vacio' }, t('seg.vacio')),
      await agendaMedica(medicos));
  }

  async function agendaMedica(medicos) {
    const feriados = await api('/api/panel/feriados');
    const bloqueos = await api('/api/panel/bloqueos');
    const fFecha = el('input', { id: 'ferFecha', type: 'date' });
    const fNombre = el('input', { id: 'ferNombre' });
    const bMed = el('select', { id: 'bloMed' }, medicos.map(m => el('option', { value: m.id }, m.nombre)));
    const bDesde = el('input', { id: 'bloDesde', type: 'date' });
    const bHasta = el('input', { id: 'bloHasta', type: 'date' });
    const resultado = el('p', { role: 'status' });
    const campo = (id, etiqueta, control) => el('div', {}, el('label', { for: id }, t(etiqueta)), control);
    return el('div', {},
      el('h3', {}, t('seg.feriados')),
      el('ul', {}, feriados.map(f => el('li', {}, `${f.fecha} · ${f.nombre}`))),
      el('div', { class: 'grid' }, campo('ferFecha', 'citas.fecha', fFecha), campo('ferNombre', 'carnet.nombre', fNombre)),
      el('button', { class: 'sec', onclick: () => accion(() => api('/api/panel/feriados', { method: 'POST', body: JSON.stringify({ fecha: fFecha.value, nombre: fNombre.value }) })) }, t('seg.agregar_feriado')),
      el('h3', {}, t('seg.bloqueos')),
      el('ul', {}, bloqueos.map(b => el('li', {}, `${b.medicoId} · ${b.desde} → ${b.hasta} · ${b.motivo}`))),
      el('div', { class: 'grid' }, campo('bloMed', 'citas.medico', bMed), campo('bloDesde', 'seg.desde', bDesde), campo('bloHasta', 'seg.hasta', bHasta)),
      el('button', { class: 'sec', onclick: () => accion(async () => {
        const r = await api('/api/panel/bloqueos', { method: 'POST', body: JSON.stringify({ medicoId: bMed.value, desde: bDesde.value, hasta: bHasta.value, motivo: 'otro' }) });
        await Dialogo.aviso(t('seg.bloqueo_listo').replace('{n}', r.citasParaReprogramar));
      }) }, t('seg.bloquear')),
      resultado,
      await cambiosAgenda(medicos));
  }

  // Urgencias: solicitudes de sobrecupo (solo el personal o el médico deciden), cupos de urgencia y centros de emergencia.
  async function urgencias() {
    const solicitudes = await api('/api/panel/urgencias/sobrecupos');
    const cupos = await api('/api/panel/urgencias/cupos');
    const config = await api('/api/panel/urgencias/config');
    const medicos = await api('/api/medicos');
    const nombreMedico = (id) => (medicos.find(m => m.id === id) || {}).nombre || id;
    const campo = (id, etiqueta, control) => el('div', {}, el('label', { for: id }, t(etiqueta)), control);

    const pendientes = solicitudes.filter(s => s.estado === 'pendiente');
    const tarjetas = pendientes.map(s => {
      const hora = el('input', { type: 'time', min: '07:00', max: '19:00', 'aria-label': `${t('urg.hora')} · ${s.paciente}` });
      return el('div', { class: 'item' },
        el('div', { class: 'fila-acciones' }, el('strong', {}, s.paciente), el('span', { class: 'muted' }, s.medico),
          el('span', { class: 'chip' }, s.fecha), el('span', { class: 'muted' }, `${s.id} · ${fecha(s.creada)}`)),
        el('div', { class: 'fila-acciones' }, hora,
          el('button', { onclick: () => accion(() => api(`/api/panel/urgencias/sobrecupos/${s.id}/decidir`, { method: 'POST', body: JSON.stringify({ aprobar: true, hora: hora.value }) })) }, t('urg.aprobar')),
          el('button', { class: 'peligro', onclick: () => accion(() => api(`/api/panel/urgencias/sobrecupos/${s.id}/decidir`, { method: 'POST', body: JSON.stringify({ aprobar: false }) })) }, t('urg.rechazar'))));
    });
    const decididas = solicitudes.filter(s => s.estado !== 'pendiente').slice(-10).reverse().map(s =>
      el('li', {}, `${s.id} · ${s.paciente} · ${s.fecha} · ${t('urg.estado.' + s.estado)}${s.decididaPor ? ' · ' + s.decididaPor : ''}`));

    const cMed = el('select', { id: 'cupMed' }, medicos.map(m => el('option', { value: m.id }, m.nombre)));
    const cDia = el('select', { id: 'cupDia' }, [1, 2, 3, 4, 5].map(d => el('option', { value: String(d) }, t('urg.dia.' + d))));
    const cHora = el('input', { id: 'cupHora', type: 'time', min: '08:00', max: '16:59', value: '08:00' });
    const liberar = el('input', { id: 'urgLiberar', type: 'number', min: '8', max: '17', value: String(config.horaLiberarCupos) });
    const centros = el('textarea', { id: 'urgCentros', rows: '3', maxlength: '1000' });
    centros.value = config.centrosEmergencia || '';

    return el('div', {},
      el('p', { class: 'muted' }, t('urg.nota')),
      el('h3', {}, t('urg.pendientes')),
      pendientes.length ? el('div', {}, tarjetas) : el('p', { class: 'vacio' }, t('urg.vacio')),
      decididas.length ? el('div', {}, el('h3', {}, t('urg.decididas')), el('ul', {}, decididas)) : '',
      el('h3', {}, t('urg.cupos')),
      el('p', { class: 'muted' }, t('urg.cupos_nota').replace('{h}', config.horaLiberarCupos)),
      cupos.length ? el('ul', {}, cupos.map(c => el('li', {}, `${nombreMedico(c.medicoId)} · ${t('urg.dia.' + c.diaSemana)} · ${c.hora} `,
        el('button', { class: 'sec', 'aria-label': `${t('urg.quitar')} ${nombreMedico(c.medicoId)} ${t('urg.dia.' + c.diaSemana)} ${c.hora}`,
          onclick: () => accion(() => api(`/api/panel/urgencias/cupos/${c.id}`, { method: 'DELETE' })) }, t('urg.quitar'))))) : el('p', { class: 'vacio' }, t('urg.sin_cupos')),
      el('div', { class: 'grid' }, campo('cupMed', 'citas.medico', cMed), campo('cupDia', 'urg.dia', cDia), campo('cupHora', 'urg.hora', cHora)),
      el('button', { class: 'sec', onclick: () => accion(() => api('/api/panel/urgencias/cupos', { method: 'POST', body: JSON.stringify({ medicoId: cMed.value, diaSemana: Number(cDia.value), hora: cHora.value }) })) }, t('urg.agregar_cupo')),
      el('h3', {}, t('urg.config')),
      el('div', { class: 'grid' }, campo('urgLiberar', 'urg.liberar', liberar), campo('urgCentros', 'urg.centros', centros)),
      el('button', { class: 'sec', onclick: () => accion(() => api('/api/panel/urgencias/config', { method: 'PUT', body: JSON.stringify({ horaLiberarCupos: Number(liberar.value), centrosEmergencia: centros.value }) })) }, t('urg.guardar')));
  }

  // Cambios de agenda: estado de las citas que hay que reprogramar por un bloqueo y aviso de retraso del médico.
  let avisoRetraso = '';
  async function cambiosAgenda(medicos) {
    const lista = await api('/api/panel/reprogramaciones');
    const rMed = el('select', { id: 'retMed' }, medicos.map(m => el('option', { value: m.id }, m.nombre)));
    const rMin = el('input', { id: 'retMin', type: 'number', min: '5', max: '240', value: '30' });
    const campo = (id, etiqueta, control) => el('div', {}, el('label', { for: id }, t(etiqueta)), control);
    return el('div', {},
      el('h3', {}, t('rep.titulo')),
      lista.length ? el('ul', {}, lista.map(r => el('li', {},
        `${r.paciente} · ${r.medico} · ${fecha(r.fechaHora)} · ${t('rep.estado.' + r.estado)}${r.prioridad ? ' · ' + t('rep.prioridad') : ''}${r.nuevaCitaId ? ' → ' + r.nuevaCitaId : ''}`)))
        : el('p', { class: 'vacio' }, t('rep.vacio')),
      el('h3', {}, t('rep.retraso')),
      el('div', { class: 'grid' }, campo('retMed', 'citas.medico', rMed), campo('retMin', 'rep.minutos', rMin)),
      el('button', { class: 'sec', onclick: () => accion(async () => {
        const r = await api(`/api/panel/medicos/${rMed.value}/retraso`, { method: 'POST', body: JSON.stringify({ minutos: Number(rMin.value) }) });
        avisoRetraso = t('rep.avisados').replace('{n}', r.avisados);
      }) }, t('rep.avisar')),
      el('p', { role: 'status' }, avisoRetraso));
  }

  // Familias: varios pacientes gestionados desde un mismo teléfono, con su contador de ausencias.
  let telefonoFamilia = '';
  async function familias() {
    const tel = el('input', { id: 'famTel', type: 'tel', value: telefonoFamilia, placeholder: '809-555-0101' });
    const nombre = el('input', { id: 'famNombre' });
    const confirma = el('input', { id: 'famConfirma', type: 'checkbox' });
    const miembros = telefonoFamilia ? await api(`/api/panel/familias?telefono=${encodeURIComponent(telefonoFamilia)}`) : null;
    const campo = (id, etiqueta, control) => el('div', {}, el('label', { for: id }, t(etiqueta)), control);
    return el('div', {},
      el('p', { class: 'muted' }, t('fam.nota')),
      el('div', { class: 'grid' }, campo('famTel', 'pac.telefono', tel)),
      el('button', { class: 'sec', onclick: () => { telefonoFamilia = tel.value.trim(); cargar(); } }, t('fam.buscar')),
      miembros === null ? '' : miembros.length
        ? el('div', {}, miembros.map(m => el('div', { class: 'item fila-acciones' },
            el('strong', {}, m.nombre), el('span', { class: 'muted' }, m.id),
            el('span', { class: 'chip' }, t('fam.ausencias').replace('{n}', m.ausencias)),
            !m.activo ? el('span', { class: 'chip' }, t('fam.inactivo')) : '',
            m.noLocalizable ? el('span', { class: 'chip urgente' }, t('fam.no_localizable')) : '',
            el('button', { class: m.activo ? 'peligro' : 'sec', 'aria-label': `${t(m.activo ? 'fam.desactivar' : 'fam.reactivar')} · ${m.nombre}`,
              onclick: () => accion(() => api(`/api/panel/pacientes/${m.id}/${m.activo ? 'desactivar' : 'reactivar'}`, { method: 'POST' })) },
              t(m.activo ? 'fam.desactivar' : 'fam.reactivar')))))
        : el('p', { class: 'vacio' }, t('fam.vacio')),
      el('h3', {}, t('fam.agregar')),
      el('div', { class: 'grid' }, campo('famNombre', 'fam.nombre', nombre)),
      el('div', { class: 'fila-acciones' }, confirma, el('label', { for: 'famConfirma' }, t('fam.confirma'))),
      el('button', { onclick: () => accion(async () => {
        telefonoFamilia = tel.value.trim();
        await api('/api/panel/familias', { method: 'POST', body: JSON.stringify({ telefono: telefonoFamilia, nombre: nombre.value.trim(), confirmado: confirma.checked }) });
      }) }, t('fam.vincular')));
  }

  // Médicos: teléfono verificado para el canal del médico y permiso para su resumen diario.
  let codigoMedico = null;
  async function medicos() {
    const lista = await api('/api/panel/medicos');
    return el('div', {},
      el('p', { class: 'muted' }, t('med.nota')),
      codigoMedico ? el('p', { role: 'status', class: 'item' }, codigoMedico) : '',
      lista.map(m => {
        const tel = el('input', { type: 'tel', value: m.telefono || '', 'aria-label': `${t('pac.telefono')} · ${m.nombre}` });
        return el('div', { class: 'item' },
          el('div', { class: 'fila-acciones' }, el('strong', {}, m.nombre),
            el('span', { class: 'chip' }, m.verificado ? t('med.verificado') : t('med.sin_verificar')),
            el('span', { class: 'chip' }, m.resumen ? t('med.con_resumen') : t('med.sin_resumen'))),
          el('div', { class: 'fila-acciones' }, tel,
            el('button', { class: 'sec', onclick: () => accion(async () => {
              const r = await api(`/api/panel/medicos/${m.id}/telefono`, { method: 'POST', body: JSON.stringify({ telefono: tel.value.trim() }) });
              codigoMedico = t('med.codigo').replace('{c}', r.codigo);
            }) }, t('med.registrar')),
            el('button', { class: 'sec', onclick: () => accion(() => api(`/api/panel/medicos/${m.id}/resumen`, { method: 'POST', body: JSON.stringify({ acepta: !m.resumen }) })) },
              m.resumen ? t('med.quitar_resumen') : t('med.dar_resumen'))));
      }));
  }

  // Avisos que no se enviaron porque el paciente no dio permiso: hay que llamarle.
  async function llamadas() {
    const lista = await api('/api/panel/llamadas');
    if (!lista.length) return el('p', { class: 'vacio' }, t('llam.vacio'));
    return el('div', {},
      el('p', { class: 'muted' }, t('llam.nota')),
      lista.map(l => el('div', { class: 'item' },
        el('div', { class: 'fila-acciones' },
          el('strong', {}, l.paciente), el('span', { class: 'muted' }, l.telefono),
          el('span', { class: 'chip' }, t('llam.tipo.' + l.tipo)), el('span', { class: 'muted' }, fecha(l.creado))),
        el('p', {}, l.texto),
        el('button', { onclick: () => accion(() => api(`/api/panel/llamadas/${l.id}/hecha`, { method: 'POST' })) }, t('llam.hecha')))));
  }

  // Buscar un paciente por teléfono y registrar en persona su permiso para avisos.
  let telefonoPaciente = '';
  async function pacientes() {
    const entrada = el('input', { type: 'tel', value: telefonoPaciente, 'aria-label': t('pac.telefono'), placeholder: '809-555-0101' });
    const buscar = el('button', { onclick: () => { telefonoPaciente = entrada.value.trim(); cargar(); } }, t('pac.buscar'));
    const cabecera = el('div', { class: 'fila-acciones' }, el('label', {}, t('pac.telefono') + ' '), entrada, buscar);
    if (!telefonoPaciente) return el('div', {}, cabecera, el('p', { class: 'muted' }, t('pac.ayuda')));

    let p;
    try { p = await api(`/api/panel/pacientes?telefono=${encodeURIComponent(telefonoPaciente)}`); }
    catch (err) { return el('div', {}, cabecera, el('p', { class: 'vacio' }, t('pac.no_encontrado'))); }

    const texto = await api(`/api/panel/consentimiento/texto?idioma=${I18N.idioma}`);
    const registrar = (acepta) => accion(() => api(`/api/panel/pacientes/${p.id}/consentimientos`,
      { method: 'POST', body: JSON.stringify({ acepta, idioma: I18N.idioma }) }));
    return el('div', {}, cabecera,
      el('h3', {}, `${p.nombre} (${p.id})`),
      el('table', {}, el('tbody', {}, p.consentimientos.map(c => el('tr', {},
        el('th', {}, t('pac.cat.' + c.categoria)),
        el('td', {}, c.otorgado ? t('pac.otorgado') : t('pac.sin_permiso')),
        el('td', { class: 'muted' }, c.fechaUtc ? `${fecha(c.fechaUtc)} · ${c.canal} · v${c.versionTexto}` : ''))))),
      el('h3', {}, t('pac.texto')),
      el('blockquote', {}, texto.texto),
      el('div', { class: 'fila-acciones' },
        el('button', { onclick: () => registrar(true) }, t('pac.registrar')),
        el('button', { class: 'peligro', onclick: () => registrar(false) }, t('pac.retirar'))),
      el('h3', {}, t('priv.enlace')),
      el('div', { class: 'fila-acciones' },
        el('a', { href: `/api/panel/pacientes/${p.id}/exportar`, download: `${p.id}.json` }, t('pac.exportar')),
        el('button', { class: 'peligro', onclick: async () => {
          const respuesta = await Dialogo.pedirTexto(t('pac.confirmar_borrado'), t('dlg.escriba_borrar'));
          if (respuesta !== 'BORRAR') return;
          await accion(async () => {
            await api(`/api/panel/pacientes/${p.id}/borrar`, { method: 'POST', body: JSON.stringify({ confirmacion: respuesta }) });
            telefonoPaciente = '';
            await Dialogo.aviso(t('pac.borrado'));
          });
        } }, t('pac.borrar'))));
  }

  async function bitacora() {
    const lista = await api('/api/bitacora?limite=200');
    if (!lista.length) return el('p', { class: 'vacio' }, t('bit.vacio'));
    return el('div', { class: 'tabla' }, el('table', {},
      el('thead', {}, el('tr', {}, ['bit.fecha', 'bit.quien', 'bit.accion', 'bit.sobre', 'bit.detalle'].map(k => el('th', {}, t(k))))),
      el('tbody', {}, lista.map(b => el('tr', {},
        el('td', {}, fecha(b.fecha)),
        el('td', {}, b.actor),
        el('td', {}, b.accion),
        el('td', {}, [b.entidad, b.entidadId].filter(Boolean).join(' ')),
        el('td', {}, [b.detalle, I18N.idioma === 'en' ? (b.resumenEn || b.resumenEs) : b.resumenEs].filter(Boolean).join(' · ')))))));
  }

  fetch('/api/auth/yo', { credentials: 'same-origin' }).then(r => r.ok ? mostrarApp() : mostrarLogin()).catch(mostrarLogin);
