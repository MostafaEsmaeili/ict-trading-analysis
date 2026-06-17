namespace IctTrader.SharedKernel.Messaging;

/// <summary>
/// The in-process message bus that decouples the modules (plan §3.0a). Commands and queries route to
/// exactly one handler; events fan out to many. We deliberately use this thin abstraction instead of
/// MediatR (which is commercially licensed). Because the only coupling between modules is this bus plus
/// the published <c>*.Contracts</c>, the in-memory transport can later be swapped for a distributed
/// broker (Redis/RabbitMQ/Kafka) with no change to module logic — the seam that keeps the modular
/// monolith extractable into services.
/// </summary>
public interface IMessageBus
{
    /// <summary>Dispatches a command to its single handler.</summary>
    Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>Dispatches a query to its single handler and returns the result.</summary>
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>Publishes an event to every subscribed handler.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
