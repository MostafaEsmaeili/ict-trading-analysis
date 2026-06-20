using System.Diagnostics.CodeAnalysis;

namespace IctTrader.Domain.Common;

/// <summary>
/// Raised when a domain invariant is violated (plan §3.0). Value objects and aggregates self-validate
/// and throw this rather than allowing invalid state to exist.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Guard clauses that enforce invariants by throwing <see cref="DomainException"/>.</summary>
public static class Guard
{
    public static void Against([DoesNotReturnIf(true)] bool condition, string message)
    {
        if (condition)
        {
            throw new DomainException(message);
        }
    }

    public static void AgainstNullOrWhiteSpace(string? value, string message)
        => Against(string.IsNullOrWhiteSpace(value), message);
}
