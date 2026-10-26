// Textos de la interfaz en español e inglés. Los elementos con data-i18n="clave" se traducen solos;
// data-i18n-placeholder y data-i18n-title traducen esos atributos.
(function () {
  const TEXTOS = {
    es: {
      'idioma': 'Idioma',
      'chat.titulo': 'DPM Agent: recepción del consultorio',
      'chat.subtitulo': 'Prototipo con datos ficticios. Solo tareas administrativas, sin orientación médica.',
      'chat.telefono': 'Número de WhatsApp (simulado):',
      'chat.nueva': 'Nueva conversación',
      'chat.placeholder': 'Ej.: Quiero una cita con la doctora para mañana en la mañana',
      'chat.enviar': 'Enviar',
      'chat.foto': 'Enviar foto del carnet de la ARS',
      'chat.grabar': 'Grabar nota de voz',
      'chat.sin_microfono': 'Este navegador no permite grabar audio',
      'chat.error_microfono': 'No se pudo usar el micrófono: ',
      'chat.nota_voz': '🎤 Nota de voz',
      'chat.foto_carnet': '📷 Foto del carnet',
      'chat.no_procesado': '(no se pudo procesar)',
      'chat.automatico': '🔔 Mensaje automático',
      'chat.recordatorio': '🔔 Recordatorio',
      'chat.error': 'Error: ',
      'chat.idioma_detectado': 'Idioma del paciente',
      'traza.titulo': 'Pasos del agente',
      'traza.subtitulo': 'Herramientas que el modelo decidió usar en cada turno.',
      'demo.banner': 'Demo con datos ficticios. No escriba datos reales.',
      'demo.reiniciar': 'Reiniciar demo',
      'demo.reiniciada': 'La demo volvió a su estado inicial.',
      'demo.probar': 'Pruebe en 30 segundos:',
      'demo.panel': 'Panel del personal',
      'panel.titulo': 'Panel del consultorio',
      'panel.solo_personal': 'Solo para el personal. Datos ficticios.',
      'panel.usuario': 'Usuario',
      'panel.clave': 'Clave',
      'panel.entrar': 'Entrar',
      'panel.login_error': 'Usuario o clave incorrectos.',
      'panel.salir': 'Cerrar sesión',
      'panel.sesion_vencida': 'La sesión terminó. Entre de nuevo.',
      'tab.citas': 'Citas del día',
      'tab.escalamientos': 'Escalamientos',
      'tab.carnets': 'Carnets por confirmar',
      'tab.autorizaciones': 'Autorizaciones ARS',
      'tab.costos': 'Costos',
      'tab.bitacora': 'Bitácora',
      'citas.fecha': 'Fecha',
      'citas.vacio': 'No hay citas para este día.',
      'citas.hora': 'Hora',
      'citas.paciente': 'Paciente',
      'citas.ars': 'ARS',
      'citas.medico': 'Médico',
      'citas.motivo': 'Motivo',
      'citas.estado': 'Estado',
      'citas.asistencia': 'asistencia confirmada',
      'citas.borrador': 'Borrador ARS',
      'esc.vacio': 'No hay escalamientos pendientes.',
      'esc.urgente': 'URGENTE',
      'esc.atender': 'Marcar como atendido',
      'esc.paciente': 'Paciente',
      'esc.sin_identificar': 'sin identificar',
      'esc.conversacion': 'Conversación',
      'esc.original': 'Mensaje original',
      'carnet.vacio': 'No hay carnets por confirmar.',
      'carnet.tel': 'Tel.',
      'carnet.nuevo': 'paciente nuevo',
      'carnet.nombre': 'Nombre',
      'carnet.ars': 'ARS',
      'carnet.afiliado': 'Número de afiliado',
      'carnet.confirmar': 'Confirmar',
      'carnet.descartar': 'Descartar',
      'aut.vacio': 'No hay borradores pendientes. Créelos desde "Citas del día".',
      'aut.sin_ars': 'sin ARS',
      'aut.falta': 'Falta: ',
      'aut.nota': 'Revise y edite el texto. Aprobar no envía nada: solo lo marca como listo para que usted lo tramite.',
      'aut.guardar': 'Guardar cambios',
      'aut.aprobar': 'Aprobar',
      'aut.descartar': 'Descartar',
      'costos.periodo': 'Últimos 30 días',
      'costos.conversaciones': 'Conversaciones',
      'costos.citas': 'Citas agendadas por el agente',
      'costos.llamadas': 'Llamadas al modelo',
      'costos.tokens': 'Tokens (entrada / salida)',
      'costos.total': 'Costo total',
      'costos.por_conversacion': 'Costo promedio por conversación',
      'costos.por_cita': 'Costo promedio por cita agendada',
      'costos.por_modelo': 'Por modelo',
      'costos.sin_precio': 'Sin precio configurado para: ',
      'bit.vacio': 'Sin registros todavía.',
      'bit.fecha': 'Fecha',
      'bit.quien': 'Quién',
      'bit.accion': 'Acción',
      'bit.sobre': 'Sobre',
      'bit.detalle': 'Detalle',
    },
    en: {
      'idioma': 'Language',
      'chat.titulo': 'DPM Agent: clinic front desk',
      'chat.subtitulo': 'Prototype with fictional data. Administrative tasks only, never medical advice.',
      'chat.telefono': 'WhatsApp number (simulated):',
      'chat.nueva': 'New conversation',
      'chat.placeholder': 'E.g.: I would like an appointment with the doctor tomorrow morning',
      'chat.enviar': 'Send',
      'chat.foto': 'Send a photo of the health insurance card',
      'chat.grabar': 'Record a voice note',
      'chat.sin_microfono': 'This browser cannot record audio',
      'chat.error_microfono': 'Could not use the microphone: ',
      'chat.nota_voz': '🎤 Voice note',
      'chat.foto_carnet': '📷 Insurance card photo',
      'chat.no_procesado': '(could not be processed)',
      'chat.automatico': '🔔 Automatic message',
      'chat.recordatorio': '🔔 Reminder',
      'chat.error': 'Error: ',
      'chat.idioma_detectado': 'Patient language',
      'traza.titulo': 'Agent steps',
      'traza.subtitulo': 'Tools the model decided to use on each turn.',
      'demo.banner': 'Demo with fictional data. Do not enter real information.',
      'demo.reiniciar': 'Reset demo',
      'demo.reiniciada': 'The demo is back to its initial state.',
      'demo.probar': 'Try it in 30 seconds:',
      'demo.panel': 'Staff dashboard',
      'panel.titulo': 'Clinic dashboard',
      'panel.solo_personal': 'Staff only. Fictional data.',
      'panel.usuario': 'Username',
      'panel.clave': 'Password',
      'panel.entrar': 'Sign in',
      'panel.login_error': 'Wrong username or password.',
      'panel.salir': 'Sign out',
      'panel.sesion_vencida': 'Your session ended. Please sign in again.',
      'tab.citas': "Today's appointments",
      'tab.escalamientos': 'Escalations',
      'tab.carnets': 'Cards to confirm',
      'tab.autorizaciones': 'Insurance authorizations',
      'tab.costos': 'Costs',
      'tab.bitacora': 'Audit log',
      'citas.fecha': 'Date',
      'citas.vacio': 'No appointments on this day.',
      'citas.hora': 'Time',
      'citas.paciente': 'Patient',
      'citas.ars': 'Insurer',
      'citas.medico': 'Doctor',
      'citas.motivo': 'Reason',
      'citas.estado': 'Status',
      'citas.asistencia': 'attendance confirmed',
      'citas.borrador': 'Insurance draft',
      'esc.vacio': 'No pending escalations.',
      'esc.urgente': 'URGENT',
      'esc.atender': 'Mark as handled',
      'esc.paciente': 'Patient',
      'esc.sin_identificar': 'not identified',
      'esc.conversacion': 'Conversation',
      'esc.original': 'Original message',
      'carnet.vacio': 'No insurance cards to confirm.',
      'carnet.tel': 'Phone',
      'carnet.nuevo': 'new patient',
      'carnet.nombre': 'Name',
      'carnet.ars': 'Insurer',
      'carnet.afiliado': 'Member number',
      'carnet.confirmar': 'Confirm',
      'carnet.descartar': 'Discard',
      'aut.vacio': 'No pending drafts. Create them from "Today\'s appointments".',
      'aut.sin_ars': 'no insurer',
      'aut.falta': 'Missing: ',
      'aut.nota': 'Review and edit the text. Approving sends nothing: it only marks it as ready for you to submit.',
      'aut.guardar': 'Save changes',
      'aut.aprobar': 'Approve',
      'aut.descartar': 'Discard',
      'costos.periodo': 'Last 30 days',
      'costos.conversaciones': 'Conversations',
      'costos.citas': 'Appointments booked by the agent',
      'costos.llamadas': 'Model calls',
      'costos.tokens': 'Tokens (input / output)',
      'costos.total': 'Total cost',
      'costos.por_conversacion': 'Average cost per conversation',
      'costos.por_cita': 'Average cost per booked appointment',
      'costos.por_modelo': 'By model',
      'costos.sin_precio': 'No price configured for: ',
      'bit.vacio': 'No entries yet.',
      'bit.fecha': 'Date',
      'bit.quien': 'Who',
      'bit.accion': 'Action',
      'bit.sobre': 'About',
      'bit.detalle': 'Detail',
    },
  };

  const CLAVE = 'dpm.idioma';

  function leerGuardado() {
    try { return localStorage.getItem(CLAVE); } catch { return null; }
  }

  function guardar(idioma) {
    try { localStorage.setItem(CLAVE, idioma); } catch { /* navegación privada: no se recuerda */ }
  }

  function inicial() {
    const guardado = leerGuardado();
    if (guardado && TEXTOS[guardado]) return guardado;
    const navegador = (navigator.language || 'en').slice(0, 2).toLowerCase();
    return navegador === 'es' ? 'es' : 'en';
  }

  let actual = inicial();
  const oyentes = [];

  function t(clave) {
    return (TEXTOS[actual] && TEXTOS[actual][clave]) || TEXTOS.en[clave] || clave;
  }

  function aplicar(raiz = document) {
    document.documentElement.lang = actual;
    raiz.querySelectorAll('[data-i18n]').forEach(e => { e.textContent = t(e.dataset.i18n); });
    raiz.querySelectorAll('[data-i18n-placeholder]').forEach(e => { e.placeholder = t(e.dataset.i18nPlaceholder); });
    raiz.querySelectorAll('[data-i18n-title]').forEach(e => { e.title = t(e.dataset.i18nTitle); });
    raiz.querySelectorAll('select[data-selector-idioma]').forEach(s => { s.value = actual; });
  }

  function cambiar(idioma) {
    if (!TEXTOS[idioma]) return;
    actual = idioma;
    guardar(idioma);
    aplicar();
    oyentes.forEach(f => f(idioma));
  }

  // Selector: <select data-selector-idioma></select>
  function montarSelectores() {
    document.querySelectorAll('select[data-selector-idioma]').forEach(s => {
      if (!s.options.length) {
        s.append(new Option('Español', 'es'), new Option('English', 'en'));
      }
      s.addEventListener('change', () => cambiar(s.value));
    });
  }

  window.I18N = {
    t,
    get idioma() { return actual; },
    cambiar,
    alCambiar: (f) => oyentes.push(f),
    aplicar,
  };

  document.addEventListener('DOMContentLoaded', () => { montarSelectores(); aplicar(); });
})();
