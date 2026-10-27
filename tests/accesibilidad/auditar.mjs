// Prueba básica de accesibilidad: axe-core (WCAG 2.1 A y AA) sobre la demo, el login del panel y el aviso
// de privacidad, en modo claro y oscuro y en español e inglés, más una revisión de teclado y tamaño de letra.
// Uso: BASE_URL=http://127.0.0.1:5199 node tests/accesibilidad/auditar.mjs   (la app con Demo:Habilitado=true)
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
// axe se inyecta con evaluate (no con una etiqueta <script> en línea), así la CSP estricta de la app sigue activa.
const axeFuente = readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
const base = process.env.BASE_URL || 'http://127.0.0.1:5199';

const paginas = ['/', '/panel.html', '/privacidad.html'];
const fallos = [];

const navegador = await chromium.launch();

// Cualquier violación de la Content-Security-Policy o error de JavaScript en cualquier página es un fallo.
function vigilar(pagina) {
  pagina.on('console', (m) => {
    if (m.type() === 'error' && /Content Security Policy|Refused to (execute|load|apply|connect|frame)/i.test(m.text()))
      fallos.push(`CSP: ${pagina.url()} → ${m.text().slice(0, 200)}`);
  });
  pagina.on('pageerror', (e) => fallos.push(`JavaScript: ${pagina.url()} → ${String(e.message).slice(0, 200)}`));
}
const nuevoContexto = navegador.newContext.bind(navegador);
navegador.newContext = async (opciones) => { const c = await nuevoContexto(opciones); c.on('page', vigilar); return c; };
const nuevaPagina = navegador.newPage.bind(navegador);
navegador.newPage = async (opciones) => { const p = await nuevaPagina(opciones); vigilar(p); return p; };
for (const esquema of ['light', 'dark']) {
  for (const idioma of ['es', 'en']) {
    const contexto = await navegador.newContext({ colorScheme: esquema, locale: idioma === 'es' ? 'es-DO' : 'en-US', viewport: { width: 1280, height: 800 } });
    const pagina = await contexto.newPage();
    for (const ruta of paginas) {
      await pagina.goto(base + ruta);
      await pagina.waitForLoadState('networkidle');
      // En el chat, usar un ejemplo para que corra también el código de mensajes (bajo la CSP).
      if (ruta === '/' && await pagina.locator('#ejemplos button').count() > 0) {
        await pagina.locator('#ejemplos button').first().click();
        await pagina.waitForTimeout(800);
      }
      await pagina.evaluate(axeFuente);
      const r = await pagina.evaluate(async () => await window.axe.run(document, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
      }));
      for (const v of r.violations) {
        fallos.push(`${ruta} [${esquema}, ${idioma}] ${v.id}: ${v.help} → ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
      }
    }
    await contexto.close();
  }
}

// Panel con sesión iniciada (si se da la clave de la secretaria): cada pestaña.
if (process.env.PANEL_CLAVE) {
  for (const esquema of ['light', 'dark']) {
    const contexto = await navegador.newContext({ colorScheme: esquema, locale: 'es-DO' });
    const pagina = await contexto.newPage();
    await pagina.goto(base + '/panel.html');
    await pagina.fill('#usuario', 'secretaria');
    await pagina.fill('#clave', process.env.PANEL_CLAVE);
    await pagina.click('#formLogin button');
    await pagina.waitForSelector('#app:not([hidden])');
    // El panel no usa alert/prompt/confirm del navegador: sus diálogos son propios y accesibles.
    pagina.on('dialog', async (d) => { fallos.push(`/panel.html [${esquema}] diálogo nativo del navegador: ${d.type()}`); await d.dismiss(); });
    for (const tab of ['citas', 'escalamientos', 'carnets', 'autorizaciones', 'urgencias', 'seguimientos', 'llamadas', 'medicos', 'familias', 'pacientes', 'costos', 'bitacora']) {
      await pagina.click(`#tabs button[data-tab="${tab}"]`);
      await pagina.waitForTimeout(500);
      await pagina.evaluate(axeFuente);
      const r = await pagina.evaluate(async () => await window.axe.run(document, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
      }));
      for (const v of r.violations) {
        fallos.push(`/panel.html#${tab} [${esquema}] ${v.id}: ${v.help} → ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
      }
    }

    // Diálogo propio abierto (el de borrar datos): axe, foco dentro y Escape lo cierra.
    await pagina.evaluate(() => { window.__dlg = Dialogo.pedirTexto(I18N.t('pac.confirmar_borrado'), I18N.t('dlg.escriba_borrar')); });
    await pagina.waitForSelector('dialog.dialogo[open]');
    const foco = await pagina.evaluate(() => !!document.activeElement.closest('dialog.dialogo'));
    if (!foco) fallos.push(`/panel.html diálogo [${esquema}]: el foco no está dentro del diálogo`);
    const rd = await pagina.evaluate(async () => await window.axe.run(document, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
    }));
    for (const v of rd.violations) {
      fallos.push(`/panel.html diálogo [${esquema}] ${v.id}: ${v.help} → ${v.nodes.slice(0, 3).map(n => n.target.join(' ')).join(' | ')}`);
    }
    await pagina.keyboard.press('Escape');
    const cerrado = await pagina.evaluate(async () => (await window.__dlg) === null && !document.querySelector('dialog.dialogo'));
    if (!cerrado) fallos.push(`/panel.html diálogo [${esquema}]: Escape no lo cierra`);
    await contexto.close();
  }
}

// Celular (390 px, lo que usará un juez desde X o Discord): sin desborde horizontal en ninguna página.
{
  const contexto = await navegador.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, locale: 'es-DO' });
  const pagina = await contexto.newPage();
  for (const ruta of paginas) {
    await pagina.goto(base + ruta);
    await pagina.waitForTimeout(500);
    const { ancho, visible } = await pagina.evaluate(() => ({ ancho: document.documentElement.scrollWidth, visible: document.documentElement.clientWidth }));
    if (ancho > visible + 1) fallos.push(`celular: ${ruta} se desborda a lo ancho (${ancho}px en ${visible}px)`);
  }
  await contexto.close();
}

// Teclado: desde el inicio de la demo, con Tab se llega al campo de mensaje y al botón Enviar,
// y cada elemento enfocado muestra el foco.
{
  const pagina = await navegador.newPage();
  await pagina.goto(base + '/');
  await pagina.waitForLoadState('networkidle');
  const vistos = new Set();
  for (let i = 0; i < 40; i++) {
    await pagina.keyboard.press('Tab');
    const info = await pagina.evaluate(() => {
      const e = document.activeElement;
      if (!e || e === document.body) return null;
      const s = getComputedStyle(e);
      const visible = (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) >= 2) || s.boxShadow !== 'none';
      return { id: e.id || e.getAttribute('data-i18n') || e.tagName, visible };
    });
    if (!info) continue;
    vistos.add(info.id);
    if (!info.visible) fallos.push(`teclado: el foco no se ve en ${info.id}`);
  }
  for (const id of ['input', 'send', 'telefono', 'selectorIdioma', 'reiniciar', 'tamanoLetra']) {
    if (!vistos.has(id)) fallos.push(`teclado: con Tab no se llega a #${id}`);
  }

  // Tamaño de letra ajustable: el botón agranda el texto del chat.
  const antes = await pagina.evaluate(() => parseFloat(getComputedStyle(document.documentElement).fontSize));
  if (await pagina.locator('#tamanoLetra').count() === 0) fallos.push('tamaño de letra: falta el botón #tamanoLetra');
  else await pagina.click('#tamanoLetra');
  const despues = await pagina.evaluate(() => parseFloat(getComputedStyle(document.documentElement).fontSize));
  if (!(despues > antes)) fallos.push(`tamaño de letra: no cambió (${antes}px → ${despues}px)`);
  await pagina.close();
}

await navegador.close();
if (fallos.length) {
  console.log(fallos.join('\n'));
  console.log(`\n${fallos.length} problemas de accesibilidad.`);
  process.exit(1);
}
console.log('Accesibilidad: sin problemas (axe WCAG 2.1 AA, teclado, tamaño de letra y sin violaciones de CSP).');
