namespace Fhir.Application.Security;

public static class Roles
{
    public const string Clinician = "Clinician";
    public const string Admin = "Admin";

    /// <summary>Given to hospital systems that authenticate with an ingest API key.</summary>
    public const string HospitalSystem = "HospitalSystem";
}
