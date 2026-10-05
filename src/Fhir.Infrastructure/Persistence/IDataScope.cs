using Fhir.Application.Security;

namespace Fhir.Infrastructure.Persistence;

/// <summary>
/// Decides which hospital's rows a <see cref="FhirDbContext"/> can see.
/// </summary>
public interface IDataScope
{
    /// <summary>
    /// True only for trusted background processing (HL7 message handling, migrations), never for a request user.
    /// </summary>
    bool IsSystem { get; }

    string? HospitalCode { get; }
}

/// <summary>Scope for HL7 processing and migrations: sees every hospital.</summary>
public sealed class SystemDataScope : IDataScope
{
    public static readonly SystemDataScope Instance = new();

    private SystemDataScope()
    {
    }

    public bool IsSystem => true;

    public string? HospitalCode => null;
}

/// <summary>
/// Scope for request handling: only the current user's hospital. A user with no hospital claim sees nothing.
/// </summary>
public sealed class CurrentUserDataScope(ICurrentUser currentUser) : IDataScope
{
    public bool IsSystem => false;

    public string? HospitalCode => currentUser.HospitalCode;
}
