using System.Security.Claims;
using System.Security.Cryptography;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Panel;

/// <param name="Restriccion">cambiar_clave o activar_totp: hasta resolverlo, la sesión no ve los datos del consultorio.</param>
public sealed record UsuarioActual(int Id, int ConsultorioId, string Usuario, string Nombre, string Rol, string Sello = "", string? Restriccion = null);

/// <param name="PideCodigo">La clave es correcta pero falta el código de la aplicación autenticadora.</param>
public sealed record ResultadoLogin(UsuarioActual? Usuario, bool PideCodigo = false);

/// <summary>
/// Usuarios del panel: clave con bloqueo por intentos fallidos, segundo factor TOTP opcional (obligatorio para los
/// administradores si el consultorio lo pide), cambio obligatorio de la clave inicial y sello que invalida sesiones.
/// </summary>
public sealed class ServicioUsuarios
{
    public const int MinLargoClave = PoliticaClaves.MinLargo;

    public const string ClaimConsultorio = "dpm:consultorio";
    public const string ClaimSello = "dpm:sello";
    public const string ClaimInicio = "dpm:inicio";
    public const string ClaimRestriccion = "dpm:restriccion";
    public const string CambiarClave = "cambiar_clave";
    public const string ActivarTotp = "activar_totp";

    private static readonly PasswordHasher<UsuarioPanel> Hasher = new();

    // Hash de una clave cualquiera: se verifica contra él cuando el usuario no existe,
    // para que la respuesta tarde lo mismo y no revele qué usuarios existen.
    private static readonly string HashFicticio = Hasher.HashPassword(null!, Guid.NewGuid().ToString());

    private readonly DpmDbContext _db;
    private readonly PanelOptions _opciones;
    private readonly TimeProvider _clock;
    private readonly IDataProtector _protector;

    public ServicioUsuarios(DpmDbContext db, IOptions<PanelOptions> opciones, TimeProvider clock, IDataProtectionProvider proteccion)
    {
        _db = db;
        _opciones = opciones.Value;
        _clock = clock;
        _protector = proteccion.CreateProtector("dpm.panel.totp");
    }

    /// <summary>
    /// Valida usuario, clave y (si lo tiene) el código TOTP. Usuario inexistente, inactivo, bloqueado o clave mal dan
    /// la misma respuesta y siempre se calcula un hash, para que el tiempo tampoco revele qué usuarios existen.
    /// </summary>
    public async Task<ResultadoLogin> ValidarAsync(string usuario, string clave, string? codigo, CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var u = await _db.UsuariosPanel.FirstOrDefaultAsync(x => x.Usuario == usuario.Trim().ToLowerInvariant(), ct);

        if (u is null || !u.Activo || u.BloqueadoHastaUtc > ahora)
        {
            Hasher.VerifyHashedPassword(null!, HashFicticio, clave);
            return new ResultadoLogin(null);
        }

        var resultado = Hasher.VerifyHashedPassword(u, u.HashClave, clave);
        if (resultado == PasswordVerificationResult.Failed)
        {
            await FalloAsync(u, ahora, ct);
            return new ResultadoLogin(null);
        }

        // Segundo factor: con la clave correcta, falta el código; un código equivocado cuenta como intento fallido.
        if (u.TotpSecreto is not null)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                return new ResultadoLogin(null, PideCodigo: true);
            if (Totp.Verificar(Secreto(u.TotpSecreto), codigo, _clock.GetUtcNow(), u.TotpUltimoPaso) is not { } paso)
            {
                await FalloAsync(u, ahora, ct);
                return new ResultadoLogin(null);
            }

            u.TotpUltimoPaso = paso;
        }

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            u.HashClave = Hasher.HashPassword(u, clave);

        // Una clave que ya no cumple la política (de antes de esta versión) se debe cambiar ahora.
        if (!u.ClaveFija && PoliticaClaves.Validar(clave, u.Usuario) is not null)
            u.DebeCambiarClave = true;

        u.IntentosFallidos = 0;
        u.BloqueadoHastaUtc = null;
        u.UltimoAccesoUtc = ahora;
        await _db.SaveChangesAsync(ct);
        return new ResultadoLogin(await AUsuarioAsync(u, ct));
    }

    private async Task FalloAsync(UsuarioPanel u, DateTime ahora, CancellationToken ct)
    {
        u.IntentosFallidos++;
        if (u.IntentosFallidos >= _opciones.IntentosAntesDeBloquear)
        {
            u.BloqueadoHastaUtc = ahora.AddMinutes(_opciones.MinutosBloqueo);
            u.IntentosFallidos = 0;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>El usuario de la sesión, si sigue activo y su sello coincide (no cambió la clave ni el segundo factor).</summary>
    public async Task<UsuarioPanel?> DeSesionAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return null;
        var u = await _db.UsuariosPanel.FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is { Activo: true } && u.SelloSeguridad == principal.FindFirstValue(ClaimSello) ? u : null;
    }

    public async Task<UsuarioActual> CambiarClaveAsync(UsuarioPanel u, string actual, string nueva, CancellationToken ct)
    {
        if (u.ClaveFija)
            throw new InvalidOperationException("La clave de este usuario no se puede cambiar.");
        if (Hasher.VerifyHashedPassword(u, u.HashClave, actual) == PasswordVerificationResult.Failed)
            throw new ArgumentException("La clave actual no es correcta.");
        if (PoliticaClaves.Validar(nueva, u.Usuario) is { } error)
            throw new ArgumentException(error);
        if (nueva == actual)
            throw new ArgumentException("La clave nueva debe ser distinta de la actual.");

        u.HashClave = Hasher.HashPassword(u, nueva);
        u.DebeCambiarClave = false;
        u.SelloSeguridad = Guid.NewGuid().ToString("N"); // cierra las demás sesiones de este usuario
        await _db.SaveChangesAsync(ct);
        return await AUsuarioAsync(u, ct);
    }

    /// <summary>Genera un secreto TOTP nuevo (pendiente hasta confirmarlo con un código) y devuelve la clave y el enlace.</summary>
    public async Task<(string Clave, string Uri)> IniciarTotpAsync(UsuarioPanel u, string emisor, CancellationToken ct)
    {
        if (u.Rol != "admin")
            throw new InvalidOperationException("El segundo factor es para administradores.");
        var secreto = Totp.NuevoSecreto();
        u.TotpPendiente = _protector.Protect(Convert.ToBase64String(secreto));
        await _db.SaveChangesAsync(ct);
        return (Totp.Base32(secreto), Totp.Uri(emisor, u.Usuario, secreto));
    }

    public async Task<UsuarioActual> ActivarTotpAsync(UsuarioPanel u, string codigo, CancellationToken ct)
    {
        if (u.TotpPendiente is null)
            throw new InvalidOperationException("Primero genere la clave del segundo factor.");
        var secreto = Secreto(u.TotpPendiente);
        if (Totp.Verificar(secreto, codigo, _clock.GetUtcNow(), null) is not { } paso)
            throw new ArgumentException("El código no es correcto. Revise la hora del teléfono y pruebe con el código nuevo.");

        u.TotpSecreto = u.TotpPendiente;
        u.TotpPendiente = null;
        u.TotpUltimoPaso = paso;
        u.SelloSeguridad = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(ct);
        return await AUsuarioAsync(u, ct);
    }

    public async Task<UsuarioActual> DesactivarTotpAsync(UsuarioPanel u, string clave, CancellationToken ct)
    {
        if (Hasher.VerifyHashedPassword(u, u.HashClave, clave) == PasswordVerificationResult.Failed)
            throw new ArgumentException("La clave no es correcta.");
        if (await _db.Consultorios.AnyAsync(c => c.Id == u.ConsultorioId && c.TotpObligatorioAdmin, ct) && u.Rol == "admin")
            throw new InvalidOperationException("El consultorio exige el segundo factor a los administradores.");
        u.TotpSecreto = null;
        u.TotpPendiente = null;
        u.SelloSeguridad = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync(ct);
        return await AUsuarioAsync(u, ct);
    }

    private byte[] Secreto(string protegido) => Convert.FromBase64String(_protector.Unprotect(protegido));

    /// <param name="claveFija">Solo el usuario de la demo pública: su clave está publicada, no se exige la política ni se puede cambiar.</param>
    public async Task<UsuarioActual> CrearAsync(int consultorioId, string usuario, string nombre, string clave, string rol, CancellationToken ct,
        bool claveFija = false)
    {
        usuario = usuario.Trim().ToLowerInvariant();
        if (usuario.Length is < 3 or > 60 || !usuario.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new ArgumentException("El usuario debe tener de 3 a 60 caracteres: letras, números, punto, guion o guion bajo.");
        if (!claveFija && PoliticaClaves.Validar(clave, usuario) is { } error)
            throw new ArgumentException(error);
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
            // La clave la puso otra persona (inicial o la creó un admin): se cambia en el primer inicio de sesión.
            DebeCambiarClave = !claveFija,
            ClaveFija = claveFija,
        };
        u.HashClave = Hasher.HashPassword(u, clave);
        _db.UsuariosPanel.Add(u);
        await _db.SaveChangesAsync(ct);
        return await AUsuarioAsync(u, ct);
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

    /// <param name="inicio">Momento del inicio de sesión (para la expiración absoluta); se conserva al renovar la sesión.</param>
    public static ClaimsPrincipal APrincipal(UsuarioActual u, string esquema, DateTimeOffset inicio)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, u.Usuario),
            new("nombre", u.Nombre),
            new(ClaimTypes.Role, u.Rol),
            new(ClaimConsultorio, u.ConsultorioId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimSello, u.Sello),
            new(ClaimInicio, inicio.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (u.Restriccion is { } r)
            claims.Add(new Claim(ClaimRestriccion, r));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, esquema));
    }

    private async Task<UsuarioActual> AUsuarioAsync(UsuarioPanel u, CancellationToken ct)
    {
        string? restriccion = null;
        if (u.DebeCambiarClave && !u.ClaveFija)
            restriccion = CambiarClave;
        else if (u.Rol == "admin" && u.TotpSecreto is null && await _db.Consultorios.AnyAsync(c => c.Id == u.ConsultorioId && c.TotpObligatorioAdmin, ct))
            restriccion = ActivarTotp;
        return new UsuarioActual(u.Id, u.ConsultorioId, u.Usuario, u.Nombre, u.Rol, u.SelloSeguridad, restriccion);
    }
}
