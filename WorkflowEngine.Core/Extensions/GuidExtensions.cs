namespace WorkflowEngine.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="Guid"/>.
/// </summary>
public static class GuidExtensions
{
    /// <summary>
    /// Returns a short representation of the GUID: 6 hex characters without dashes.
    /// Suitable for checkpoint namespace suffixes to keep identifiers readable and compact.
    /// </summary>
    public static string ToShortGuid(this Guid value)
    {
        return value.ToString("N")[..6];
    }
}
