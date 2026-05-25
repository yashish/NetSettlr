using Domain.Exceptions;

namespace Domain.Entities
{
    // ─────────────────────────────────────────────────────────────────────────────
    // BilateralPosition
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Running gross accumulator for a single directional flow between two participants
    /// within one settlement cycle.
    /// Two rows per pair (A→B and B→A) to preserve gross flows for audit.
    /// High write-contention: RowVersion is mandatory for optimistic concurrency.
    /// Unique constraint: (SettlementCycleId, DebitingRtn, CreditingRtn)
    /// </summary>
    public sealed class BilateralPosition : BaseEntity
    {
        private BilateralPosition() { }

        public Guid SettlementCycleId { get; private init; }
        /// <summary>The RTN whose Fed account will be debited.</summary>
        public string DebitingRtn { get; private init; } = string.Empty;
        /// <summary>The RTN whose Fed account will be credited.</summary>
        public string CreditingRtn { get; private init; } = string.Empty;
        public long GrossAmountCents { get; private set; }
        public int TransactionCount { get; private set; }
        public DateTime LastUpdatedAt { get; private set; }

        public static BilateralPosition Create(
            Guid cycleId, string debitingRtn, string creditingRtn) =>
            new()
            {
                SettlementCycleId = cycleId,
                DebitingRtn = debitingRtn,
                CreditingRtn = creditingRtn,
                GrossAmountCents = 0,
                TransactionCount = 0,
                LastUpdatedAt = DateTime.UtcNow
            };

        /// <summary>
        /// Accumulates a transaction amount into this bilateral position.
        /// Called by the netting engine. Thread-safety is handled at the
        /// repository/database level via RowVersion optimistic concurrency.
        /// </summary>
        public void Accumulate(long amountCents)
        {
            if (amountCents <= 0)
                throw new DomainException($"Bilateral position accumulation requires positive amount. Got: {amountCents}");

            GrossAmountCents += amountCents;
            TransactionCount++;
            LastUpdatedAt = DateTime.UtcNow;
        }
    }
}
