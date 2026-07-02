using IctTrader.Domain.Services;
using IctTrader.Performance.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Performance.Application;

/// <summary>
/// The per-model performance breakdown read-side (plan §16): one §5.3 summary row per setup model that has
/// closed trades, computed by folding each model's own close stream through the SAME pure
/// <see cref="PerformanceCalculator"/> the headline summary uses — so a model row and the global aggregate can
/// never disagree on a formula. Pure read; read-only analytics (§6.3).
/// </summary>
public sealed class GetModelPerformanceQueryHandler(PerformanceState state)
    : IQueryHandler<GetModelPerformanceQuery, IReadOnlyList<ModelPerformanceDto>>
{
    private readonly PerformanceState _state = state ?? throw new ArgumentNullException(nameof(state));

    public Task<IReadOnlyList<ModelPerformanceDto>> HandleAsync(
        GetModelPerformanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<ModelPerformanceDto> rows = _state.Models()
            .Select(model =>
            {
                var closes = _state.Snapshot(model);
                var summary = PerformanceCalculator.Summarize(closes);
                return new ModelPerformanceDto(model, closes.Count, PerformanceMapper.ToDto(summary));
            })
            .ToList();

        return Task.FromResult(rows);
    }
}
