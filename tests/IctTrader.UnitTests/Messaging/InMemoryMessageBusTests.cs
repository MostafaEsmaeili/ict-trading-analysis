using System.Reflection;
using FluentAssertions;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IctTrader.UnitTests.Messaging;

/// <summary>
/// Locks the in-memory bus dispatch contract (plan §3.0a): commands/queries route to exactly one handler
/// (fail-fast otherwise), events fan out to every handler sequentially in registration order, each
/// dispatch runs in its own DI scope, and <c>AddMessaging</c> auto-discovers handlers by assembly scan.
/// </summary>
public class InMemoryMessageBusTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddMessaging();                 // bus singleton only — handlers registered per-test
        services.AddSingleton<Recorder>();
        services.AddScoped<UnitOfWorkProbe>();
        register(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SendAsync_dispatches_to_the_single_command_handler()
    {
        using var sp = BuildProvider(s => s.AddScoped<ICommandHandler<PingCommand>, PingHandler>());
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.SendAsync(new PingCommand("hello"));

        sp.GetRequiredService<Recorder>().Log.Should().ContainSingle().Which.Should().Be("cmd:hello");
    }

    [Fact]
    public async Task QueryAsync_routes_to_the_handler_and_returns_its_result()
    {
        using var sp = BuildProvider(s => s.AddScoped<IQueryHandler<AskQuery, int>, AskHandler>());
        var bus = sp.GetRequiredService<IMessageBus>();

        var result = await bus.QueryAsync(new AskQuery(21));

        result.Should().Be(42);
    }

    [Fact]
    public async Task PublishAsync_fans_out_to_every_handler_in_registration_order()
    {
        using var sp = BuildProvider(s =>
        {
            s.AddScoped<IEventHandler<ThingHappened>, FirstThingHandler>();
            s.AddScoped<IEventHandler<ThingHappened>, SecondThingHandler>();
        });
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new ThingHappened("x"));

        sp.GetRequiredService<Recorder>().Log.Should().Equal("first:x", "second:x");
    }

    [Fact]
    public async Task PublishAsync_handlers_share_one_dispatch_scope()
    {
        using var sp = BuildProvider(s =>
        {
            s.AddScoped<IEventHandler<ThingHappened>, FirstThingHandler>();
            s.AddScoped<IEventHandler<ThingHappened>, SecondThingHandler>();
        });
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.PublishAsync(new ThingHappened("y"));

        // Both handlers ran in ONE publish, so they resolved the SAME scoped probe — one delivery = one scope.
        var scopeIds = sp.GetRequiredService<Recorder>().ScopeIds;
        scopeIds.Should().HaveCount(2);
        scopeIds[0].Should().Be(scopeIds[1]);
    }

    [Fact]
    public async Task PublishAsync_with_no_subscribers_is_a_no_op()
    {
        using var sp = BuildProvider(_ => { });
        var bus = sp.GetRequiredService<IMessageBus>();

        var publish = async () => await bus.PublishAsync(new ThingHappened("nobody-listening"));

        await publish.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_with_no_handler_fails_fast()
    {
        using var sp = BuildProvider(_ => { });
        var bus = sp.GetRequiredService<IMessageBus>();

        var send = async () => await bus.SendAsync(new PingCommand("orphan"));

        (await send.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("exactly one command handler");
    }

    [Fact]
    public async Task SendAsync_with_two_handlers_fails_fast()
    {
        using var sp = BuildProvider(s =>
        {
            s.AddScoped<ICommandHandler<DoubleCommand>, DoubleHandlerA>();
            s.AddScoped<ICommandHandler<DoubleCommand>, DoubleHandlerB>();
        });
        var bus = sp.GetRequiredService<IMessageBus>();

        var send = async () => await bus.SendAsync(new DoubleCommand());

        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Each_dispatch_runs_in_its_own_scope()
    {
        using var sp = BuildProvider(s => s.AddScoped<ICommandHandler<PingCommand>, PingHandler>());
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.SendAsync(new PingCommand("a"));
        await bus.SendAsync(new PingCommand("b"));

        // A scoped UnitOfWorkProbe resolved within one scope is identity-stable; two DISTINCT ids prove
        // each dispatch opened a fresh scope (a shared scope would reuse the same probe instance).
        var scopeIds = sp.GetRequiredService<Recorder>().ScopeIds;
        scopeIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AddMessaging_assembly_scan_auto_discovers_handlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddMessaging(typeof(InMemoryMessageBusTests).Assembly);   // scans THIS assembly
        using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<IMessageBus>();

        await bus.SendAsync(new ScanPing());

        sp.GetRequiredService<Recorder>().Log.Should().Contain("scanned");
    }
}

// ----- test doubles: messages + handlers exercised above (internal so the assembly scan finds them) -----

internal sealed record PingCommand(string Note) : ICommand;

internal sealed record DoubleCommand : ICommand;

internal sealed record ScanPing : ICommand;

internal sealed record AskQuery(int Value) : IQuery<int>;

internal sealed record ThingHappened(string What) : IEvent;

/// <summary>Singleton sink the scoped handlers write to, so assertions can read across dispatch scopes.</summary>
internal sealed class Recorder
{
    public List<string> Log { get; } = [];
    public List<Guid> ScopeIds { get; } = [];
}

/// <summary>A scoped unit-of-work stand-in; its id reveals which DI scope resolved it.</summary>
internal sealed class UnitOfWorkProbe
{
    public Guid Id { get; } = Guid.NewGuid();
}

internal sealed class PingHandler(Recorder recorder, UnitOfWorkProbe unitOfWork) : ICommandHandler<PingCommand>
{
    public Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        recorder.Log.Add($"cmd:{command.Note}");
        recorder.ScopeIds.Add(unitOfWork.Id);
        return Task.CompletedTask;
    }
}

internal sealed class DoubleHandlerA : ICommandHandler<DoubleCommand>
{
    public Task HandleAsync(DoubleCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class DoubleHandlerB : ICommandHandler<DoubleCommand>
{
    public Task HandleAsync(DoubleCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class ScanPingHandler(Recorder recorder) : ICommandHandler<ScanPing>
{
    public Task HandleAsync(ScanPing command, CancellationToken cancellationToken = default)
    {
        recorder.Log.Add("scanned");
        return Task.CompletedTask;
    }
}

internal sealed class AskHandler : IQueryHandler<AskQuery, int>
{
    public Task<int> HandleAsync(AskQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(query.Value * 2);
}

internal sealed class FirstThingHandler(Recorder recorder, UnitOfWorkProbe unitOfWork)
    : IEventHandler<ThingHappened>
{
    public Task HandleAsync(ThingHappened @event, CancellationToken cancellationToken = default)
    {
        recorder.Log.Add($"first:{@event.What}");
        recorder.ScopeIds.Add(unitOfWork.Id);
        return Task.CompletedTask;
    }
}

internal sealed class SecondThingHandler(Recorder recorder, UnitOfWorkProbe unitOfWork)
    : IEventHandler<ThingHappened>
{
    public Task HandleAsync(ThingHappened @event, CancellationToken cancellationToken = default)
    {
        recorder.Log.Add($"second:{@event.What}");
        recorder.ScopeIds.Add(unitOfWork.Id);
        return Task.CompletedTask;
    }
}
