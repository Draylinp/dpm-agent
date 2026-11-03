namespace DpmAgent.Api.Agenda;

/// <summary>Configuración de la lista de espera, los recordatorios y el despacho de mensajes (sección "Agenda").</summary>
public sealed class AgendaOptions
{
    public const string SectionName = "Agenda";

    /// <summary>Si es false no corre la tarea en segundo plano (útil en pruebas).</summary>
    public bool MotorActivo { get; set; } = true;

    /// <summary>Cada cuánto revisa huecos, ofertas vencidas, recordatorios y mensajes pendientes.</summary>
    public int IntervaloSegundos { get; set; } = 15;

    /// <summary>Tiempo que tiene el paciente para aceptar un hueco antes de pasar al siguiente.</summary>
    public int OfertaMinutos { get; set; } = 30;

    /// <summary>No se ofrecen huecos que empiecen antes de este margen.</summary>
    public int AnticipacionMinimaMinutos { get; set; } = 60;

    public int RecordatorioHorasAntes { get; set; } = 24;

    /// <summary>No se recuerda una cita que empieza en menos de esto.</summary>
    public int RecordatorioMinimoHoras { get; set; } = 2;

    /// <summary>Una cita reservada hace menos de esto no necesita recordatorio.</summary>
    public int RecordatorioSiCreadaHaceHoras { get; set; } = 6;

    public int MaxIntentosEnvio { get; set; } = 5;

    /// <summary>Intentos de contacto de un seguimiento antes de pasarlo a "sin respuesta".</summary>
    public int SeguimientoIntentos { get; set; } = 3;

    /// <summary>Días entre un intento de contacto y el siguiente.</summary>
    public int SeguimientoDiasEntreIntentos { get; set; } = 3;

    /// <summary>Recordatorio extra antes de una cita de seguimiento (0 lo apaga). El de 24 horas sigue igual.</summary>
    public int SeguimientoRecordatorioDiasAntes { get; set; } = 7;

    /// <summary>Avisar al paciente cuando se crea un seguimiento lejano (con la fecha aproximada).</summary>
    public bool SeguimientoAvisarAlCrear { get; set; } = true;
}
