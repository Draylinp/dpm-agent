using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Data;

/// <summary>
/// Cifrado a nivel de campo del teléfono del paciente y su número de afiliado, con la Data Protection API.
/// Como el texto cifrado cambia cada vez, para buscar por teléfono se guarda además un HMAC-SHA256 del número
/// normalizado (<see cref="Entidades.Paciente.TelefonoHash"/>). La llave del HMAC vive en la base, protegida con las
/// mismas llaves de Data Protection: sin esas llaves no se puede leer nada, por eso en producción deben ser persistentes.
/// </summary>
public sealed class CifradoCampos
{
    /// <summary>Prefijo del texto cifrado: lo que no lo lleva es un dato de antes del cifrado (se cifra al arrancar).</summary>
    public const string Prefijo = "dpm1:";

    private const string NombreLlaveHmac = "hmac_telefono";
    private readonly IDataProtector _protector;
    private byte[]? _llaveHmac;

    public CifradoCampos(IDataProtectionProvider proveedor)
    {
        _protector = proveedor.CreateProtector("dpm.campos.v1");
    }

    private static readonly Lazy<CifradoCampos> SinContenedorLazy = new(() =>
    {
        var c = new CifradoCampos(new EphemeralDataProtectionProvider());
        c._llaveHmac = RandomNumberGenerator.GetBytes(32);
        return c;
    });

    /// <summary>
    /// Para contextos creados sin el contenedor (pruebas, herramientas de diseño): llaves efímeras, una sola instancia
    /// por proceso para que todos esos contextos lean lo que cifró otro. La aplicación usa siempre la del contenedor.
    /// </summary>
    public static CifradoCampos SinContenedor => SinContenedorLazy.Value;

    public string? Proteger(string? valor) =>
        valor is null || valor.StartsWith(Prefijo, StringComparison.Ordinal) ? valor : Prefijo + _protector.Protect(valor);

    public string? Desproteger(string? valor) =>
        valor is not null && valor.StartsWith(Prefijo, StringComparison.Ordinal) ? _protector.Unprotect(valor[Prefijo.Length..]) : valor;

    /// <summary>HMAC del teléfono normalizado (10 dígitos), o null si no es un teléfono.</summary>
    public string? HashTelefono(string? telefono)
    {
        if (Dpm.Telefono.Normalizar(telefono) is not { } normalizado)
            return null;
        var llave = _llaveHmac ?? throw new InvalidOperationException("El cifrado de campos no está inicializado (falta InicializarAsync).");
        return Convert.ToHexString(HMACSHA256.HashData(llave, Encoding.UTF8.GetBytes(normalizado))).ToLowerInvariant();
    }

    /// <summary>Carga (o crea la primera vez) la llave del HMAC, guardada en la base y protegida.</summary>
    public async Task InicializarAsync(DpmDbContext db, CancellationToken ct)
    {
        if (_llaveHmac is not null)
            return;
        var fila = await db.SecretosApp.FirstOrDefaultAsync(s => s.Nombre == NombreLlaveHmac, ct);
        if (fila is null)
        {
            var nueva = RandomNumberGenerator.GetBytes(32);
            db.SecretosApp.Add(new Entidades.SecretoApp { Nombre = NombreLlaveHmac, ValorProtegido = _protector.Protect(Convert.ToBase64String(nueva)) });
            await db.SaveChangesAsync(ct);
            _llaveHmac = nueva;
            return;
        }

        _llaveHmac = Convert.FromBase64String(_protector.Unprotect(fila.ValorProtegido));
    }

    /// <summary>
    /// Cifra los pacientes guardados antes del cifrado (teléfono y afiliado en claro, sin HMAC). Se ejecuta al arrancar,
    /// después de las migraciones; no hace nada si ya están cifrados.
    /// </summary>
    public static async Task<int> CifrarExistentesAsync(DpmDbContext db, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var lote = await db.Pacientes.IgnoreQueryFilters().Where(p => p.TelefonoHash == null).Take(200).ToListAsync(ct);
            if (lote.Count == 0)
                return total;
            foreach (var p in lote)
            {
                // El valor en memoria es el mismo (el conversor deja pasar el texto sin prefijo): se fuerza la escritura cifrada.
                db.Entry(p).Property(x => x.Telefono).IsModified = true;
                db.Entry(p).Property(x => x.NumeroAfiliado).IsModified = true;
            }

            await db.SaveChangesAsync(ct); // SaveChanges calcula el HMAC
            total += lote.Count;
        }
    }
}
