using System.Globalization;
using System.Text.RegularExpressions;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Idiomas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DpmAgent.Eval;

/// <summary>
/// Capa 1 de la evaluación: comprobaciones deterministas sobre las herramientas usadas,
/// el estado final de la base y el texto de las respuestas.
/// </summary>
public static class Verificador
{
    public static async Task<List<string>> VerificarAsync(IServiceProvider sp, Caso caso, IReadOnlyList<ResultadoTurno> turnos, CancellationToken ct)
    {
        var e = caso.Esperado;
        var fallos = new List<string>();
        var usadas = turnos.SelectMany(t => t.Pasos).Select(p => p.Herramienta).ToHashSet();
        var respuestas = turnos.Select(t => t.Respuesta).ToList();

        foreach (var h in e.Herramientas ?? [])
        {
            if (!usadas.Contains(h))
                fallos.Add($"No se usó la herramienta {h}.");
        }

        foreach (var h in e.HerramientasProhibidas ?? [])
        {
            if (usadas.Contains(h))
                fallos.Add($"Se usó la herramienta prohibida {h}.");
        }

        foreach (var patron in e.NoDebeContener ?? [])
        {
            var regex = new Regex(patron, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (respuestas.FirstOrDefault(regex.IsMatch) is { } r)
                fallos.Add($"Una respuesta contiene /{patron}/: \"{Recortar(r)}\"");
        }

        foreach (var texto in e.DebeContener ?? [])
        {
            if (!respuestas.Any(r => r.Contains(texto, StringComparison.OrdinalIgnoreCase)))
                fallos.Add($"Ninguna respuesta contiene \"{texto}\".");
        }

        foreach (var prohibido in e.ResultadoNoContiene ?? [])
        {
            var filtrados = turnos.SelectMany(t => t.Pasos).Where(p => p.Herramienta == prohibido.Herramienta);
            if (filtrados.Any(p => p.Resultado.Contains(prohibido.Texto, StringComparison.OrdinalIgnoreCase)))
                fallos.Add($"El resultado de {prohibido.Herramienta} contiene \"{prohibido.Texto}\".");
        }

        if (e.IdiomaRespuesta is { } idiomaRespuesta)
        {
            // La última respuesta (el paciente pudo cambiar de idioma); si es ambigua, todas juntas.
            var detectado = DetectorIdioma.Detectar(respuestas.LastOrDefault())?.Codigo
                             ?? DetectorIdioma.Detectar(string.Join(" ", respuestas))?.Codigo;
            if (detectado is not null && detectado != idiomaRespuesta)
                fallos.Add($"Las respuestas están en {detectado}; se esperaba {idiomaRespuesta}.");
        }

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DpmDbContext>();
        var telefono = Telefono.Normalizar(caso.Telefono);
        var paciente = await db.Pacientes.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(p => p.Telefono == telefono, ct);

        if (e.IdiomaSesion is { } idiomaSesion)
        {
            var sesion = await scope.ServiceProvider.GetRequiredService<IConversationStore>()
                .ObtenerAsync(Api.Canales.CanalesConocidos.SesionWhatsApp(telefono!), ct);
            if (sesion.Idioma != idiomaSesion)
                fallos.Add($"La sesión quedó en idioma {sesion.Idioma ?? "(ninguno)"}; se esperaba {idiomaSesion}.");
        }

        if (e.CitasPaciente is { } citasPaciente)
        {
            var n = paciente is null ? 0 : await db.Citas.CountAsync(c => c.PacienteId == paciente.Id && c.Estado == "confirmada", ct);
            if (n != citasPaciente)
                fallos.Add($"El paciente tiene {n} citas confirmadas; se esperaban {citasPaciente}.");
        }

        foreach (var (pacienteId, esperadas) in e.CitasDe ?? [])
        {
            var id = IdsDpm.Leer(pacienteId, "PAC");
            var n = await db.Citas.CountAsync(c => c.PacienteId == id && c.Estado == "confirmada", ct);
            if (n != esperadas)
                fallos.Add($"{pacienteId} tiene {n} citas confirmadas; se esperaban {esperadas}.");
        }

        if (e.CitaEn is { } citaEn)
        {
            var momento = DateTime.ParseExact(citaEn.FechaHora, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            var utc = HorarioConsultorio.EnHoraRd(DateOnly.FromDateTime(momento), TimeOnly.FromDateTime(momento)).UtcDateTime;
            var id = IdsDpm.Leer(citaEn.Paciente, "PAC");
            if (!await db.Citas.AnyAsync(c => c.PacienteId == id && c.FechaHoraUtc == utc && c.Estado == "confirmada", ct))
                fallos.Add($"No hay cita confirmada de {citaEn.Paciente} el {citaEn.FechaHora}.");
        }

        var escalamientos = await db.Escalamientos.AsNoTracking().ToListAsync(ct);
        if (e.Escalado is { } escalado && escalamientos.Count > 0 != escalado)
            fallos.Add(escalado ? "No se escaló al personal." : "Se escaló al personal sin necesidad.");

        if (e.Urgente is { } urgente && escalamientos.Any(x => x.Urgente) != urgente)
            fallos.Add(urgente ? "El escalamiento no se marcó urgente." : "Se marcó urgente sin serlo.");

        if (e.ListaEspera is { } listaEspera)
        {
            var n = await db.ListaEspera.CountAsync(l => l.Estado == "activa", ct);
            if (n != listaEspera)
                fallos.Add($"Hay {n} entradas activas en la lista de espera; se esperaban {listaEspera}.");
        }

        if (e.Seguro is { } seguro)
        {
            var id = IdsDpm.Leer(seguro.Paciente, "PAC");
            var p = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null || p.Ars != seguro.Ars || p.NumeroAfiliado != seguro.Afiliado)
                fallos.Add($"El seguro de {seguro.Paciente} es {p?.Ars ?? "-"} / {p?.NumeroAfiliado ?? "-"}; se esperaba {seguro.Ars ?? "-"} / {seguro.Afiliado ?? "-"}.");
        }

        if (e.CarnetsPendientes is { } pendientes)
        {
            var n = await db.CarnetsPendientes.CountAsync(c => c.Estado == "pendiente", ct);
            if (n != pendientes)
                fallos.Add($"Hay {n} carnets pendientes; se esperaban {pendientes}.");
        }

        if (e.PacienteConTelefono is { } telefonoNuevo)
        {
            var normalizado = Telefono.Normalizar(telefonoNuevo);
            if (!await db.Pacientes.AnyAsync(p => p.Telefono == normalizado, ct))
                fallos.Add($"No se registró el paciente con teléfono {telefonoNuevo}.");
        }

        return fallos;
    }

    private static string Recortar(string s) => s.Length <= 120 ? s : s[..120] + "…";
}
