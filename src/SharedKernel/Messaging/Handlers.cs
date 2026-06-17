namespace IctTrader.SharedKernel.Messaging;

/// <summary>Handles a single <see cref="ICommand"/> type. Registered once; resolved by the bus.</summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Handles a single <see cref="IQuery{TResult}"/> type and returns its result.</summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Reacts to a published <see cref="IEvent"/>. Many handlers may subscribe to one event.</summary>
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
