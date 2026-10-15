namespace ImageStore.Application.Common;

/// <summary>
/// The requested resource does not exist <em>for the current user</em>. Resources owned by other
/// users produce the same exception so that their existence is never revealed.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resourceName)
        : base($"{resourceName} was not found.")
    {
        ResourceName = resourceName;
    }

    public string ResourceName { get; }
}

/// <summary>The request failed validation. Keys are camelCase field names (or <c>files[i]</c>).</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public RequestValidationException(string field, string message)
        : this(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>An operation that requires an authenticated user was invoked anonymously.</summary>
public sealed class UnauthenticatedException : Exception
{
    public UnauthenticatedException()
        : base("An authenticated user is required.")
    {
    }
}

/// <summary>The operation conflicts with the current state (for example a concurrent edit).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
