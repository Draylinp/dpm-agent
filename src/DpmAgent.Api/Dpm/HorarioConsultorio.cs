namespace DpmAgent.Api.Dpm;

/// <summary>
/// Horario de atención y zona horaria de República Dominicana.
/// Lunes a viernes, 8:00 a 12:00 y 14:00 a 17:00, citas de 30 minutos.
/// </summary>
public static class HorarioConsultorio
{
    public static readonly TimeZoneInfo ZonaRd = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private static readonly (int Inicio, int Fin)[] Bloques = [(8, 12), (14, 17)];

    private const int MinutosPorCita = 30;

    public static DateTimeOffset EnHoraRd(DateOnly fecha, TimeOnly hora)
    {
        var local = fecha.ToDateTime(hora, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ZonaRd.GetUtcOffset(local));
    }

    public static DateOnly FechaRd(DateTimeOffset momento) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, ZonaRd).DateTime);

    /// <summary>Todos los turnos del día según el horario, sin mirar la agenda.</summary>
    public static IReadOnlyList<DateTimeOffset> TurnosDelDia(DateOnly fecha)
    {
        var turnos = new List<DateTimeOffset>();
        if (fecha.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return turnos;

        foreach (var (inicio, fin) in Bloques)
        {
            for (var t = new TimeOnly(inicio, 0); t < new TimeOnly(fin, 0); t = t.AddMinutes(MinutosPorCita))
                turnos.Add(EnHoraRd(fecha, t));
        }

        return turnos;
    }
}
