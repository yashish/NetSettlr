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
}
