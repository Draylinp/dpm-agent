namespace DpmAgent.Api.Data;

/// <summary>
/// Consultorio al que pertenece la petición o el trabajo en curso. Todas las consultas
/// de <see cref="DpmDbContext"/> se filtran por él.
/// </summary>
public interface IConsultorioActual
{
    int Id { get; }
}

public sealed class ConsultorioFijo(int id) : IConsultorioActual
{
    public int Id { get; } = id;
}

/// <summary>
/// Consultorio por ámbito (petición o tarea en segundo plano). Empieza con el de la configuración;
/// el webhook de WhatsApp y el motor de agenda lo cambian antes de usar la base de datos.
/// </summary>
public sealed class ConsultorioDelAmbito(int predeterminado) : IConsultorioActual
{
    private int _id = predeterminado;
    private bool _usado;

    public int Id
    {
        get
        {
            _usado = true;
            return _id;
        }
    }

    /// <summary>Debe llamarse antes de crear el contexto de base de datos del ámbito.</summary>
    public void Establecer(int consultorioId)
    {
        if (_usado && consultorioId != _id)
            throw new InvalidOperationException("El consultorio ya se usó en este ámbito y no se puede cambiar.");
        _id = consultorioId;
    }
}
