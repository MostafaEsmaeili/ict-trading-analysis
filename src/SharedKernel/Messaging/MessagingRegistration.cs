using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IctTrader.SharedKernel.Messaging;

/// <summary>
/// Composition-root wiring for the in-memory bus (plan §3.0a/§3.2). Registers the singleton
/// <see cref="InMemoryMessageBus"/> and assembly-scans the supplied module <c>*.Application</c> assemblies
/// for every <see cref="ICommandHandler{TCommand}"/>, <see cref="IQueryHandler{TQuery,TResult}"/>, and
/// <see cref="IEventHandler{TEvent}"/>, registering each under its handler interface with a <b>Scoped</b>
/// lifetime so a fresh unit-of-work is created per dispatch. Handlers are auto-discovered (Scrutor), so a
/// new module use-case is wired by adding a handler class — no manual DI line.
/// </summary>
public static class MessagingRegistration
{
    public static IServiceCollection AddMessaging(
        this IServiceCollection services, params Assembly[] handlerAssemblies)
    {
        services.TryAddSingleton<IMessageBus, InMemoryMessageBus>();

        if (handlerAssemblies.Length == 0)
        {
            return services;
        }

        // Register each handler ONLY under its matched handler interface (filtering AsImplementedInterfaces
        // to the target open generic) — so a class that also implements IDisposable or a second role is
        // not registered under those incidental interfaces and can't pollute resolution.
        services.Scan(scan => scan
            .FromAssemblies(handlerAssemblies)
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces(IsClosedHandler(typeof(ICommandHandler<>))).WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces(IsClosedHandler(typeof(IQueryHandler<,>))).WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces(IsClosedHandler(typeof(IEventHandler<>))).WithScopedLifetime());

        return services;
    }

    private static Func<Type, bool> IsClosedHandler(Type openHandlerType) =>
        type => type.IsGenericType && type.GetGenericTypeDefinition() == openHandlerType;
}
