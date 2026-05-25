using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities
{
    // ─────────────────────────────────────────────────────────────────────────────
    // SettlementObligation
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The final multilateral net obligation for one participant in one cycle.
    /// Positive NetAmountCents = net creditor (Fed credits this bank's account).
    /// Negative NetAmountCents = net debtor (Fed debits this bank's account).
    ///
    /// System invariant: SUM(NetAmountCents) across all obligations for a cycle MUST = 0.
    /// This is the zero-sum accounting identity. Violation must halt settlement.
    /// Unique constraint: (SettlementCycleId, ParticipantRtn)
    /// </summary>
    public sealed class SettlementObligation : BaseEntity
    {
        private SettlementObligation() { }

        public Guid SettlementCycleId { get; private init; }
        public string ParticipantRtn { get; private init; } = string.Empty;
        /// <summary>Positive = creditor. Negative = debtor.</summary>
        public long NetAmountCents { get; private init; }
        public long GrossCreditCents { get; private init; }
        public long GrossDebitCents { get; private init; }
        public SettlementObligationStatus Status { get; private set; }
        public DateTime ComputedAt { get; private init; }
        public DateTime? SubmittedAt { get; private set; }
        public DateTime? ConfirmedAt { get; private set; }
        public string? FedReference { get; private set; }

        public static SettlementObligation Compute(
            Guid cycleId,
            string participantRtn,
            long grossCreditCents,
            long grossDebitCents) =>
            new()
            {
                SettlementCycleId = cycleId,
                ParticipantRtn = participantRtn,
                NetAmountCents = grossCreditCents - grossDebitCents,
                GrossCreditCents = grossCreditCents,
                GrossDebitCents = grossDebitCents,
                Status = SettlementObligationStatus.Computed,
                ComputedAt = DateTime.UtcNow
            };

        public bool IsNetDebtor => NetAmountCents < 0;
        public bool IsNetCreditor => NetAmountCents > 0;

        public void MarkSubmitted()
        {
            if (Status != SettlementObligationStatus.Computed)
                throw new DomainException($"Cannot submit obligation in status {Status}");
            
            Status = SettlementObligationStatus.Submitted;
            SubmittedAt = DateTime.UtcNow;
        }

        public void MarkConfirmed(string fedReference)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fedReference);
            
            if (Status != SettlementObligationStatus.Submitted)
                throw new DomainException($"Cannot confirm obligation in status {Status}");
            
            Status = SettlementObligationStatus.Confirmed;
            FedReference = fedReference;
            ConfirmedAt = DateTime.UtcNow;
        }

        public void MarkFailed()
        {
            Status = SettlementObligationStatus.Failed;
        }
    }
}
