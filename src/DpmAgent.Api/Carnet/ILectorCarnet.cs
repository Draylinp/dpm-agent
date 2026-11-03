using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Carnet;

/// <param name="EsCarnet">false si la imagen no parece un carnet de ARS legible.</param>
/// <param name="Datos">Datos ya limpiados con <see cref="DatosCarnet.Limpiar"/>.</param>
public sealed record LecturaCarnet(bool EsCarnet, DatosCarnet Datos);

/// <summary>Lee los datos administrativos de la foto de un carnet de ARS.</summary>
public interface ILectorCarnet
{
    Task<LecturaCarnet> LeerAsync(ImagenRecibida imagen, CancellationToken ct);
}
