namespace ImageStore.Application.Common;

/// <summary>Accumulates field-level validation errors before failing a request.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        if (!messages.Contains(message, StringComparer.Ordinal))
        {
            messages.Add(message);
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);

    public void ThrowIfAny()
    {
        if (HasErrors)
        {
            throw new RequestValidationException(ToDictionary());
        }
    }
}
