using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities
{
    // ─────────────────────────────────────────────────────────────────────────────
    // SettlementCycle
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The bounded time window in which transactions accumulate before netting.
    /// Acts as the root aggregate for the netting/settlement sub-domain.
    /// State machine: Open → Closing → Closed → Settled | Failed
    /// </summary>
    public sealed class SettlementCycle : BaseEntity
    {
        private SettlementCycle() { }

        public string CycleReference { get; private init; } = string.Empty;
        public SettlementCycleStatus Status { get; private set; }
        public DateTime OpenedAt { get; private init; }
        public DateTime? ClosedAt { get; private set; }
        public DateTime? SettledAt { get; private set; }
        public TriggerType TriggerType { get; private init; }
        public string TriggeredBy { get; private init; } = string.Empty;

        // Running totals — incremented as transactions are allocated
        public long TotalTransactionCount { get; private set; }
        public long TotalCreditCents { get; private set; }
        public long TotalDebitCents { get; private set; }

        public static SettlementCycle Open(string cycleReference, TriggerType trigger, string triggeredBy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cycleReference);
            ArgumentException.ThrowIfNullOrWhiteSpace(triggeredBy);

            var now = DateTime.UtcNow;
            return new SettlementCycle
            {
                CycleReference = cycleReference,
                Status = SettlementCycleStatus.Open,
                OpenedAt = now,
                TriggerType = trigger,
                TriggeredBy = triggeredBy
            };
        }

        /// <summary>
        /// Returns true if the cycle can still accept new transaction allocations.
        /// Transactions may arrive during Closing while Kafka drain is in progress.
        /// </summary>
        public bool IsAcceptingTransactions =>
            Status is SettlementCycleStatus.Open or SettlementCycleStatus.Closing;

        public void BeginClosing()
        {
            EnsureStatus(SettlementCycleStatus.Open, SettlementCycleStatus.Closing);
            Status = SettlementCycleStatus.Closing;
            RaiseDomainEvent(new SettlementCycleClosingEvent(Id, CycleReference));
        }

        public void Close()
        {
            EnsureStatus(SettlementCycleStatus.Closing, SettlementCycleStatus.Closed);
            Status = SettlementCycleStatus.Closed;
            ClosedAt = DateTime.UtcNow;
            RaiseDomainEvent(new SettlementCycleClosedEvent(Id, CycleReference, TotalTransactionCount));
        }

        public void MarkSettled()
        {
            EnsureStatus(SettlementCycleStatus.Closed, SettlementCycleStatus.Settled);
            Status = SettlementCycleStatus.Settled;
            SettledAt = DateTime.UtcNow;
            RaiseDomainEvent(new SettlementCycleSettledEvent(Id, CycleReference));
        }

        public void MarkFailed(string reason)
        {
            if (Status is SettlementCycleStatus.Settled)
                throw new InvalidCycleTransitionException(Status.ToString(), SettlementCycleStatus.Failed.ToString());

            Status = SettlementCycleStatus.Failed;
            RaiseDomainEvent(new SettlementCycleFailedEvent(Id, CycleReference, reason));
        }

        /// <summary>
        /// Called by the netting engine for each transaction consumed from Kafka.
        /// Updates running totals on the cycle aggregate.
        /// </summary>
        public void RecordTransactionAllocated(long amountCents, TransactionType transactionType)
        {
            if (!IsAcceptingTransactions)
                throw new CycleNotAcceptingTransactionsException(Id, Status.ToString());

            TotalTransactionCount++;

            if (transactionType == TransactionType.Credit)
                TotalCreditCents += amountCents;
            else if (transactionType == TransactionType.Debit)
                TotalDebitCents += amountCents;
        }

        private void EnsureStatus(SettlementCycleStatus expected, SettlementCycleStatus target)
        {
            if (Status != expected)
                throw new InvalidCycleTransitionException(Status.ToString(), target.ToString());
        }
    }

    // Domain events
    public sealed record SettlementCycleClosingEvent(Guid CycleId, string Reference) : IDomainEvent;
    public sealed record SettlementCycleClosedEvent(Guid CycleId, string Reference, long TxCount) : IDomainEvent;
    public sealed record SettlementCycleSettledEvent(Guid CycleId, string Reference) : IDomainEvent;
    public sealed record SettlementCycleFailedEvent(Guid CycleId, string Reference, string Reason) : IDomainEvent;
}
