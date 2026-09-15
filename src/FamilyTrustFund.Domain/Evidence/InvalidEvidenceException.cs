namespace FamilyTrustFund.Domain.Evidence;

/// <summary>Raised for invalid payment-evidence operations.</summary>
public class InvalidEvidenceException : Exception
{
    public InvalidEvidenceException(string message) : base(message)
    {
    }
}
