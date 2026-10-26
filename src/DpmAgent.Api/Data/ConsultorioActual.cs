namespace DpmAgent.Api.Data;

/// <summary>
/// Consultorio al que pertenece la petición actual. Por ahora es fijo por configuración;
/// con WhatsApp (F5) se resolverá a partir del número que recibe el mensaje.
/// </summary>
public interface IConsultorioActual
{
    int Id { get; }
}

public sealed class ConsultorioFijo(int id) : IConsultorioActual
{
    public int Id { get; } = id;
}
