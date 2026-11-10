namespace FamilyTrustFund.Application.Loans;

/// <summary>
/// Raised when another Guarantor changed the same loan at the same time (for
/// example the last two unanimous approvals racing to finalise it). Callers
/// reload the authoritative loan state and treat the decision as idempotent
/// rather than failing the user's request (ADR-052).
/// </summary>
public sealed class LoanDecisionConflictException : Exception
{
    public LoanDecisionConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
