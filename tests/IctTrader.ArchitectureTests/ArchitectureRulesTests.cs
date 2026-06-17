using System.Reflection;
using FluentAssertions;

namespace IctTrader.ArchitectureTests;

/// <summary>
/// Enforces the modular-monolith boundaries (plan §3.0a/§11 DoD) by inspecting each production
/// assembly's recorded references: SharedKernel and Domain depend on nothing internal; modules reach
/// each other only through <c>*.Contracts</c>; and no production code pulls in MediatR or test/commercial
/// libraries. The C# compiler omits unused references, so a clean reference list also proves the absence
/// of real coupling.
/// </summary>
public class ArchitectureRulesTests
{
    private static readonly string[] Modules =
        ["MarketData", "Scanning", "PaperTrading", "Performance", "Alerting"];

    private static Assembly Load(string simpleName) => Assembly.Load(new AssemblyName(simpleName));

    private static IReadOnlyList<string?> ReferencedAssemblyNames(string assemblyName)
        => Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name).ToList();

    public static TheoryData<string> ProductionAssemblies()
    {
        var data = new TheoryData<string>
        {
            "IctTrader.SharedKernel",
            "IctTrader.Domain",
            "IctTrader.Host",
        };

        foreach (var module in Modules)
        {
            data.Add($"IctTrader.{module}.Contracts");
            data.Add($"IctTrader.{module}.Application");
            data.Add($"IctTrader.{module}.Infrastructure");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ProductionAssemblies))]
    public void Production_code_carries_no_commercial_messaging_or_test_dependency(string assemblyName)
    {
        string[] banned = ["MediatR", "FluentAssertions", "Moq", "SpecFlow"];
        var referenced = ReferencedAssemblyNames(assemblyName);

        foreach (var name in banned)
        {
            referenced.Should().NotContain(
                name,
                "the modular monolith uses its own IMessageBus and production code carries no test/commercial libs");
        }
    }

    [Theory]
    [InlineData("IctTrader.Domain")]
    [InlineData("IctTrader.SharedKernel")]
    public void Foundational_assemblies_depend_on_nothing_internal(string assemblyName)
    {
        var internalRefs = ReferencedAssemblyNames(assemblyName)
            .Where(name => name is not null && name.StartsWith("IctTrader", StringComparison.Ordinal));

        internalRefs.Should().BeEmpty("SharedKernel and Domain are the dependency-free core (plan §3.1)");
    }

    [Fact]
    public void Modules_reach_each_other_only_through_contracts()
    {
        foreach (var module in Modules)
        {
            foreach (var layer in new[] { "Application", "Infrastructure" })
            {
                var referenced = ReferencedAssemblyNames($"IctTrader.{module}.{layer}");

                foreach (var other in Modules.Where(o => o != module))
                {
                    referenced.Should().NotContain(
                        $"IctTrader.{other}.Application",
                        $"{module}.{layer} must reach {other} only via its Contracts (plan §3.0a)");
                    referenced.Should().NotContain(
                        $"IctTrader.{other}.Infrastructure",
                        $"{module}.{layer} must reach {other} only via its Contracts (plan §3.0a)");
                }
            }
        }
    }
}
