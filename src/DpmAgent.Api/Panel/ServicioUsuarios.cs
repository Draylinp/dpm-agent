using System.Security.Claims;
using System.Security.Cryptography;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Panel;

public sealed record UsuarioActual(int Id, int ConsultorioId, string Usuario, string Nombre, string Rol);

/// <summary>Usuarios del panel: validación de clave con bloqueo por intentos fallidos.</summary>
public sealed class ServicioUsuarios
{
    public const int MinLargoClave = 10;

    public const string ClaimConsultorio = "dpm:consultorio";

    private static readonly PasswordHasher<UsuarioPanel> Hasher = new();

    // Hash de una clave cualquiera: se verifica contra él cuando el usuario no existe,
    // para que la respuesta tarde lo mismo y no revele qué usuarios existen.
    private static readonly string HashFicticio = Hasher.HashPassword(null!, Guid.NewGuid().ToString());

    private readonly DpmDbContext _db;
    private readonly PanelOptions _opciones;
    private readonly TimeProvider _clock;

    public ServicioUsuarios(DpmDbContext db, IOptions<PanelOptions> opciones, TimeProvider clock)
    {
        _db = db;
        _opciones = opciones.Value;
        _clock = clock;
    }

    /// <returns>El usuario si la clave es correcta y no está bloqueado; null en cualquier otro caso.</returns>
    public async Task<UsuarioActual?> ValidarAsync(string usuario, string clave, CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var u = await _db.UsuariosPanel.FirstOrDefaultAsync(x => x.Usuario == usuario.Trim().ToLowerInvariant(), ct);

        if (u is null || !u.Activo)
        {
            Hasher.VerifyHashedPassword(null!, HashFicticio, clave);
            return null;
        }

        if (u.BloqueadoHastaUtc > ahora)
            return null;

        var resultado = Hasher.VerifyHashedPassword(u, u.HashClave, clave);
        if (resultado == PasswordVerificationResult.Failed)
        {
            u.IntentosFallidos++;
            if (u.IntentosFallidos >= _opciones.IntentosAntesDeBloquear)
            {
                u.BloqueadoHastaUtc = ahora.AddMinutes(_opciones.MinutosBloqueo);
                u.IntentosFallidos = 0;
            }
            await _db.SaveChangesAsync(ct);
            return null;
        }

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            u.HashClave = Hasher.HashPassword(u, clave);

        u.IntentosFallidos = 0;
        u.BloqueadoHastaUtc = null;
        u.UltimoAccesoUtc = ahora;
        await _db.SaveChangesAsync(ct);
        return AUsuario(u);
    }

    public async Task<UsuarioActual> CrearAsync(int consultorioId, string usuario, string nombre, string clave, string rol, CancellationToken ct)
    {
        usuario = usuario.Trim().ToLowerInvariant();
        if (usuario.Length is < 3 or > 60 || !usuario.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new ArgumentException("El usuario debe tener de 3 a 60 caracteres: letras, números, punto, guion o guion bajo.");
        if (clave.Length < MinLargoClave)
            throw new ArgumentException($"La clave debe tener al menos {MinLargoClave} caracteres.");
        if (rol is not ("secretaria" or "admin"))
            throw new ArgumentException("El rol debe ser 'secretaria' o 'admin'.");
        if (await _db.UsuariosPanel.AnyAsync(x => x.Usuario == usuario, ct))
            throw new InvalidOperationException("Ese usuario ya existe.");

        var u = new UsuarioPanel
        {
            ConsultorioId = consultorioId,
            Usuario = usuario,
            Nombre = nombre.Trim(),
            HashClave = "",
            Rol = rol,
            CreadoUtc = _clock.GetUtcNow().UtcDateTime,
        };
        u.HashClave = Hasher.HashPassword(u, clave);
        _db.UsuariosPanel.Add(u);
        await _db.SaveChangesAsync(ct);
        return AUsuario(u);
    }

    /// <summary>Crea el administrador inicial de cada consultorio que no tenga usuarios.</summary>
    public async Task AsegurarUsuarioInicialAsync(bool esDesarrollo, ILogger logger, CancellationToken ct)
    {
        var sinUsuarios = await _db.Consultorios.AsNoTracking()
            .Where(c => !_db.UsuariosPanel.Any(u => u.ConsultorioId == c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var consultorio in sinUsuarios)
        {
            var clave = _opciones.ClaveInicial;
            if (string.IsNullOrWhiteSpace(clave))
            {
                if (!esDesarrollo)
                {
                    logger.LogWarning("El consultorio {Id} no tiene usuarios del panel. Defina Panel__ClaveInicial para crear el inicial.", consultorio);
                    continue;
                }

                clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));
                logger.LogWarning("Usuario inicial del panel (solo desarrollo): {Usuario} / {Clave}", _opciones.UsuarioInicial, clave);
            }

            var usuario = consultorio == 1 ? _opciones.UsuarioInicial : $"{_opciones.UsuarioInicial}{consultorio}";
            await CrearAsync(consultorio, usuario, _opciones.NombreInicial, clave, "admin", ct);
        }
    }

    public static ClaimsPrincipal APrincipal(UsuarioActual u, string esquema) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
        new Claim(ClaimTypes.Name, u.Usuario),
        new Claim("nombre", u.Nombre),
        new Claim(ClaimTypes.Role, u.Rol),
        new Claim(ClaimConsultorio, u.ConsultorioId.ToString()),
    ], esquema));

    private static UsuarioActual AUsuario(UsuarioPanel u) => new(u.Id, u.ConsultorioId, u.Usuario, u.Nombre, u.Rol);
}
