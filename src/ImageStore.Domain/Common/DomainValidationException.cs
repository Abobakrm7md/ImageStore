namespace ImageStore.Domain.Common;

/// <summary>
/// Raised when an operation would violate a domain invariant (for example an empty title).
/// The application layer validates requests first, so reaching this exception indicates a
/// programming error or a request that bypassed validation.
/// </summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(string propertyName, string message)
        : base(message)
    {
        PropertyName = propertyName;
    }

    /// <summary>The camelCase name of the property that failed validation.</summary>
    public string PropertyName { get; }
}
