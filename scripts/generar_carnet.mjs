// Dibuja un carnet de ARS inventado para probar la lectura por foto sin usar carnets reales.
// Sin logos ni marcas de terceros: solo texto y formas. Uso: node scripts/generar_carnet.mjs
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const raiz = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const salida = path.join(raiz, 'src/DpmAgent.Api/wwwroot/demo/carnet-ficticio.png');

const html = `<!doctype html><html><head><meta charset="utf-8"><style>
  body { margin:0; background:#fff; font-family: "DejaVu Sans", Arial, sans-serif; }
  .carnet { position:relative; width:640px; height:400px; box-sizing:border-box; padding:28px 32px; border-radius:24px;
    background:linear-gradient(135deg,#e8f4f1,#cfe7e1); border:3px solid #0b6b5e; color:#12302b; overflow:hidden; }
  .titulo { font-size:30px; font-weight:700; letter-spacing:1px; }
  .sub { font-size:16px; margin-top:4px; color:#2c5049; }
  .fila { margin-top:22px; font-size:15px; color:#2c5049; text-transform:uppercase; letter-spacing:1px; }
  .valor { font-size:26px; font-weight:700; color:#12302b; text-transform:none; letter-spacing:0; }
  .marca { position:absolute; right:24px; bottom:70px; transform:rotate(-16deg); font-size:32px; font-weight:800;
    color:rgba(176,0,32,.28); border:5px solid rgba(176,0,32,.28); padding:4px 18px; }
</style></head><body><div class="carnet">
  <div class="titulo">ARS DEMO SALUD</div>
  <div class="sub">Carnet de afiliado · ejemplo ficticio</div>
  <div class="fila">Nombre<div class="valor">María Rodríguez (ficticia)</div></div>
  <div class="fila">Número de afiliado<div class="valor">DEMO-000123</div></div>
  <div class="fila">Plan<div class="valor">Básico de ejemplo</div></div>
  <div class="marca">MUESTRA · NO VÁLIDO</div>
</div></body></html>`;

const navegador = await chromium.launch();
const pagina = await navegador.newPage({ viewport: { width: 640, height: 400 } });
await pagina.setContent(html);
await pagina.locator('.carnet').screenshot({ path: salida });
await navegador.close();
console.log('Escrito', salida);
