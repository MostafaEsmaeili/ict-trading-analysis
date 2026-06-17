using Microsoft.Extensions.Options;

namespace IctTrader.Host;

/// <summary>
/// The structural live-trading guardrail (plan §0/§6.3). <see cref="LiveTradingEnabled"/> exists ONLY to
/// be asserted false; the validator below fails startup if it is ever true. The system has no
/// order-routing path, so even a true flag could not execute — this is defence in depth.
/// </summary>
public sealed class DefensiveOptions
{
    public const string SectionName = "Ict:DefensiveMode";

    public bool LiveTradingEnabled { get; init; }
}

/// <summary>Fails fast at startup if anyone tries to enable live trading.</summary>
internal sealed class DefensiveOptionsValidator : IValidateOptions<DefensiveOptions>
{
    public ValidateOptionsResult Validate(string? name, DefensiveOptions options)
        => options.LiveTradingEnabled
            ? ValidateOptionsResult.Fail(
                "LiveTradingEnabled must be false. This system is analysis + paper-trading ONLY and has no live-order path.")
            : ValidateOptionsResult.Success;
}
