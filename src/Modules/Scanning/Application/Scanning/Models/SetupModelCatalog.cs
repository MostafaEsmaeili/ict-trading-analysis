using IctTrader.Domain.Setups;

namespace IctTrader.Scanning.Application.Scanning.Models;

/// <summary>
/// The registry of every setup model the scanner can run (plan §16), keyed by <see cref="SetupModel"/> — the
/// model-side sibling of <c>InstrumentCatalog</c>. Pure and immutable; the module registers
/// <see cref="Default"/> as a singleton and the scanner factory resolves the requested model's definition from
/// it. Resolving an unregistered model fails fast with the model name, so a config/typo selection can never
/// silently scan the wrong recipe.
/// </summary>
public sealed class SetupModelCatalog
{
    private readonly IReadOnlyDictionary<SetupModel, SetupModelDefinition> _byId;

    public SetupModelCatalog(IReadOnlyList<SetupModelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count == 0)
        {
            throw new ArgumentException("A setup-model catalog needs at least one definition.", nameof(definitions));
        }

        var byId = new Dictionary<SetupModel, SetupModelDefinition>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (!byId.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Setup model '{definition.Id}' is registered more than once.", nameof(definitions));
            }
        }

        _byId = byId;
        All = definitions;
    }

    /// <summary>The built-in catalog: every implemented model. The ICT 2024 model registers here once its
    /// mined pipeline ships (plan §2.6).</summary>
    public static SetupModelCatalog Default { get; } = new([Ict2022ModelDefinition.Definition]);

    public IReadOnlyList<SetupModelDefinition> All { get; }

    public SetupModelDefinition Resolve(SetupModel model)
        => _byId.TryGetValue(model, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Setup model '{model}' has no registered definition — it cannot be scanned. " +
                $"Registered models: {string.Join(", ", _byId.Keys)}.");
}
