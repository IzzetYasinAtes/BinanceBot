using System.Reflection;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.Behaviors;
using BinanceBot.Application.Sizing;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BinanceBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // ADR-0011 §11.4 — pure, stateless sizing → singleton.
        services.AddSingleton<IPositionSizingService, PositionSizingService>();

        return services;
    }
}
