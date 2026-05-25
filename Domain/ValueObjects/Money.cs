using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.ValueObjects
{
    /// <summary>
    /// Represents a monetary amount as integer cents.
    /// NEVER use floating-point for financial arithmetic.
    /// All amounts in this system are stored and computed as long (cents).
    /// </summary>
    public sealed class Money(long Cents) : IComparable<Money>
    {
        public static readonly Money Zero = new(0);

        public long Cents { get; private set; }

        public static Money FromCents(long cents) => new(cents);
        public static Money FromDollars(decimal dollars) => new((long)(dollars * 100));
        public int CompareTo(Money? other) =>
            other is null ? 1 : Cents.CompareTo(other.Cents);
    }
}
