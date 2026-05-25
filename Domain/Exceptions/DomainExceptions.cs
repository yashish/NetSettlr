using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Exceptions
{
    /// <summary>Base for all domain-layer exceptions.</summary>
    public class DomainException(string message, Exception? inner = null)
        : Exception(message, inner);

    /// <summary>
    /// Raised when a domain invariant is violated.
    /// E.g. zero-sum check on settlement obligations fails.
    /// </summary>
    public sealed class InvariantViolationException(string message)
        : DomainException(message);

    /// <summary>
    /// Raised when an RTN fails length or Mod-10 check digit validation.
    /// </summary>
    public sealed class InvalidRtnException(string message)
        : DomainException(message);

    /// <summary>
    /// Raised when a participant's net debit position would exceed their cap.
    /// This is a hard stop — do not settle.
    /// </summary>
    public sealed class NetDebitCapExceededException(
        string rtn, long capCents, long netDebitCents)
        : DomainException(
            $"Participant {rtn} net debit {netDebitCents:N0}¢ exceeds cap {capCents:N0}¢");

    /// <summary>
    /// Raised when an attempt is made to transition a settlement cycle
    /// to an invalid state (e.g. Open → Settled without going through Closed).
    /// </summary>
    public sealed class InvalidCycleTransitionException(
        string currentStatus, string targetStatus)
        : DomainException(
            $"Cannot transition settlement cycle from {currentStatus} to {targetStatus}");

    /// <summary>
    /// Raised when a transaction allocation is attempted on a cycle that is
    /// no longer accepting transactions (Closed / Settled / Failed).
    /// </summary>
    public sealed class CycleNotAcceptingTransactionsException(Guid cycleId, string status)
        : DomainException(
            $"Settlement cycle {cycleId} is in status '{status}' and cannot accept new transactions");
}
