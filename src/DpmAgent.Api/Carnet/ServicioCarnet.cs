using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Carnet;

public sealed record CarnetConfirmado(CarnetLeido Carnet, Paciente Paciente, bool PacienteNuevo);

/// <summary>
/// Aplica al paciente los datos de un carnet confirmado. Si el número todavía no tiene paciente,
/// lo registra con el nombre del carnet.
/// </summary>
public sealed class ServicioCarnet
{
    public const string PorPaciente = "paciente";
    public const string PorSecretaria = "secretaria";

    private readonly IDpmGateway _dpm;

    public ServicioCarnet(IDpmGateway dpm) => _dpm = dpm;

    /// <param name="pacienteSesion">Paciente ya identificado en la conversación, si lo hay.</param>
    /// <param name="exigirMismoNombre">
    /// Cuando confirma el paciente, el nombre del carnet debe coincidir con el del paciente del número
    /// (evita, por ejemplo, ponerle a la madre la ARS del hijo). La secretaria puede confirmar igual.
    /// </param>
    public async Task<CarnetConfirmado> ConfirmarAsync(
        CarnetLeido carnet,
        DatosCarnet? correccion,
        string resueltoPor,
        string? pacienteSesion,
        bool exigirMismoNombre,
        CancellationToken ct)
    {
        if (carnet.Estado != "pendiente")
            throw new InvalidOperationException($"El carnet {carnet.Id} ya fue {carnet.Estado}.");

        var datos = carnet.Datos.Corregir(correccion);
        if (datos.Ars is null && datos.NumeroAfiliado is null)
            throw new InvalidOperationException("El carnet no tiene ARS ni número de afiliado legibles.");

        var paciente = await PacienteDelCarnetAsync(carnet, pacienteSesion, ct);
        var nuevo = false;

        if (paciente is null)
        {
            if (datos.Nombre is null)
                throw new InvalidOperationException("Falta el nombre para registrar al paciente.");

            paciente = await _dpm.RegistrarPacienteAsync(datos.Nombre, carnet.TelefonoCanal, datos.Ars, ct);
            nuevo = true;
        }
        else if (exigirMismoNombre && datos.Nombre is not null && !DatosCarnet.NombresCoinciden(datos.Nombre, paciente.Nombre))
        {
            throw new InvalidOperationException(
                "El nombre del carnet no coincide con el paciente de este número. La secretaria lo revisará.");
        }

        // Primero se marca el carnet: si otra persona lo resolvió, no se toca al paciente.
        var resuelto = await _dpm.ResolverCarnetAsync(carnet.Id, "confirmado", datos, paciente.Id, resueltoPor, ct);
        paciente = await _dpm.ActualizarSeguroAsync(paciente.Id, datos.Ars, datos.NumeroAfiliado, ct);

        return new CarnetConfirmado(resuelto, paciente, nuevo);
    }

    public Task<CarnetLeido> DescartarAsync(CarnetLeido carnet, string resueltoPor, CancellationToken ct) =>
        _dpm.ResolverCarnetAsync(carnet.Id, "descartado", carnet.Datos, null, resueltoPor, ct);

    private async Task<Paciente?> PacienteDelCarnetAsync(CarnetLeido carnet, string? pacienteSesion, CancellationToken ct)
    {
        foreach (var id in new[] { pacienteSesion, carnet.PacienteId })
        {
            if (id is not null && await _dpm.BuscarPacienteAsync(id, ct) is { } p)
                return p;
        }

        var porTelefono = await _dpm.BuscarPacienteAsync(carnet.TelefonoCanal, ct);
        return porTelefono is not null && Telefono.Iguales(porTelefono.Telefono, carnet.TelefonoCanal) ? porTelefono : null;
    }
}
