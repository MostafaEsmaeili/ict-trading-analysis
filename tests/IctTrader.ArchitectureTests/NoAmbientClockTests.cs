using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FluentAssertions;

namespace IctTrader.ArchitectureTests;

/// <summary>
/// Enforces the time-zone discipline (plan §4.8): the pure assemblies must take time ONLY from the injected
/// <c>TimeProvider</c>/<c>NyClock</c>, never an ambient clock. Inspects each assembly's metadata for member
/// references to the forbidden ambient-time members, so a future regression fails the build.
/// </summary>
public class NoAmbientClockTests
{
    private static readonly (string Type, string Member)[] Forbidden =
    [
        ("DateTime", "get_Now"),
        ("DateTime", "get_UtcNow"),
        ("DateTimeOffset", "get_Now"),
        ("DateTimeOffset", "get_UtcNow"),
        ("TimeZoneInfo", "get_Local"),
    ];

    [Theory]
    [InlineData("IctTrader.Domain")]
    [InlineData("IctTrader.SharedKernel")]
    public void Pure_assemblies_use_no_ambient_clock(string assemblyName)
    {
        var location = Assembly.Load(new AssemblyName(assemblyName)).Location;
        using var stream = File.OpenRead(location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var violations = new List<string>();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var typeName = metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)member.Parent).Name);
            var memberName = metadata.GetString(member.Name);
            if (Forbidden.Any(f => f.Type == typeName && f.Member == memberName))
            {
                violations.Add($"{typeName}.{memberName}");
            }
        }

        violations.Should().BeEmpty(
            $"{assemblyName} must take time only from the injected TimeProvider/NyClock (plan §4.8)");
    }
}
