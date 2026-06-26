using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.SharedKernel.Messaging;

/// <summary>
/// The default in-process implementation of <see cref="IMessageBus"/> (plan §3.0a). It resolves handlers
/// from a <b>fresh DI scope per dispatch</b> — so each command/query/event delivery gets a clean
/// unit-of-work (e.g. a per-dispatch EF <c>DbContext</c>) — and is itself a stateless singleton. A
/// published event's handlers all share that single dispatch scope (one delivery = one scope), running
/// sequentially within it.
/// <para>
/// Dispatch semantics: a command and a query route to <b>exactly one</b> handler (fail-fast if zero or
/// more than one is registered); an event fans out to its <b>0..N</b> handlers, awaited <b>sequentially
/// in registration order</b>. The scan path is deterministic and order-dependent (a stop-out must settle
/// before the next bar is processed), so events are NOT fanned out concurrently or via channels here;
/// that asynchrony belongs only to a later distributed-broker transport swap. Handler exceptions
/// propagate (fail-fast) rather than being swallowed per handler.
/// </para>
/// </summary>
public sealed class InMemoryMessageBus(IServiceScopeFactory scopeFactory) : IMessageBus
{
    private readonly IServiceScopeFactory _scopeFactory =
        scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    // The handler method name, read once off the interface definition (no literal, no dummy type). All
    // three handler interfaces declare the same single method, so the query path can invoke it by name.
    private static readonly string HandleAsyncName = typeof(IQueryHandler<,>).GetMethods().Single().Name;

    public async Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(command);

        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<ICommandHandler<TCommand>>().ToList();
        ExactlyOne(handlers.Count, typeof(ICommandHandler<TCommand>), "command");

        await handlers[0].HandleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // QueryAsync takes IQuery<TResult> by interface, so the closed handler type must be reconstructed
        // from the query's CONCRETE runtime type — there is no compile-time TQuery to bind (cf. SendAsync).
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));

        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();
        ExactlyOne(handlers.Count, handlerType, "query");

        var handleAsync = handlerType.GetMethod(HandleAsyncName)!;
        var task = (Task<TResult>)handleAsync.Invoke(handlers[0], [query, cancellationToken])!;
        return await task.ConfigureAwait(false);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        using var scope = _scopeFactory.CreateScope();
        // Sequential, in registration order, fail-fast — deterministic for the order-dependent scan path.
        foreach (var handler in scope.ServiceProvider.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ExactlyOne(int found, Type handlerType, string kind)
    {
        if (found != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {kind} handler for '{handlerType}', but found {found}.");
        }
    }
}
