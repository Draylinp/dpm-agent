using DpmAgent.Api.Canales;

namespace DpmAgent.Api.Agenda;

public static class AgendaExtensions
{
    public static IServiceCollection AddAgenda(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AgendaOptions>(config.GetSection(AgendaOptions.SectionName));
        services.AddSingleton<DespertadorAgenda>();
        services.AddScoped<Bitacora>();
        services.AddScoped<Notificador>();
        services.AddScoped<ServicioListaEspera>();
        services.AddScoped<MotorAgenda>();
        services.AddScoped<DespachadorMensajes>();
        services.AddSingleton<IMessageChannel, WebChatChannel>();
        services.AddSingleton<AgendaHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<AgendaHostedService>());
        return services;
    }
}
