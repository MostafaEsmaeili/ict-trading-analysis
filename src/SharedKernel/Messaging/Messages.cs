namespace IctTrader.SharedKernel.Messaging;

/// <summary>
/// A request handled by exactly one handler, returning no result (plan §3.0a). Modules send commands on
/// the <see cref="IMessageBus"/> instead of calling each other directly.
/// </summary>
public interface ICommand;

/// <summary>A request handled by exactly one handler, returning <typeparamref name="TResult"/>.</summary>
public interface IQuery<out TResult>;

/// <summary>A notification fanned out to zero or more handlers across modules.</summary>
public interface IEvent;
