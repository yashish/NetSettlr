using Domain.Enums;
using Domain.Exceptions;
using TransactionStatus = Domain.Enums.TransactionStatus;

namespace Domain.Entities
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Transaction
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The atomic financial unit — maps 1:1 to a NACHA Entry Detail record.
    /// SettlementCycleId is null at ingestion and assigned when
    /// the netting engine picks this transaction from Kafka.
    /// </summary>
    public sealed class Transaction : BaseEntity
    {
        private Transaction() { }

        public Guid BatchId { get; private init; }
        public string TraceNumber { get; private init; } = string.Empty; // char(15), unique within batch
        public NachaTransactionCode TransactionCode { get; private init; }
        public TransactionType TransactionType { get; private init; }
        public AccountType AccountType { get; private init; }

        /// <summary>
        /// ODFI — originating depository financial institution RTN (9 digits).
        /// Denormalized from Batch for query performance at netting time.
        /// </summary>
        public string OriginatingRtn { get; private init; } = string.Empty;

        /// <summary>
        /// RDFI — receiving depository financial institution RTN (9 digits).
        /// From the Entry Detail record's receiving DFI identification field.
        /// </summary>
        public string ReceivingRtn { get; private init; } = string.Empty;

        /// <summary>Always stored as positive cents.</summary>
        public long AmountCents { get; private init; }

        public string IndividualName { get; private init; } = string.Empty;
        public string AccountNumber { get; private init; } = string.Empty;
        public DateOnly EffectiveDate { get; private init; }  // Denormalized from Batch
        public TransactionStatus Status { get; private set; }

        /// <summary>
        /// Assigned when the netting engine consumes this transaction from Kafka.
        /// Null until then — this is the explicit late-binding handoff point.
        /// </summary>
        public Guid? SettlementCycleId { get; private set; }
        public DateTime? AllocatedAt { get; private set; }

        public static Transaction Create(
            Guid batchId,
            string traceNumber,
            NachaTransactionCode transactionCode,
            string originatingRtn,
            string receivingRtn,
            long amountCents,
            string individualName,
            string accountNumber,
            DateOnly effectiveDate)
        {
            if (amountCents < 0)
                throw new DomainException("Transaction amount must be non-negative");

            return new Transaction
            {
                BatchId = batchId,
                TraceNumber = traceNumber.Trim(),
                TransactionCode = transactionCode,
                TransactionType = DeriveTransactionType(transactionCode),
                AccountType = DeriveAccountType(transactionCode),
                OriginatingRtn = originatingRtn,
                ReceivingRtn = receivingRtn,
                AmountCents = amountCents,
                IndividualName = individualName.Trim(),
                AccountNumber = accountNumber.Trim(),
                EffectiveDate = effectiveDate,
                Status = TransactionStatus.Pending
            };
        }

        /// <summary>
        /// Assigns this transaction to a settlement cycle.
        /// Must only be called on Pending transactions.
        /// The cycle must be in Open or Closing (drain) status.
        /// </summary>
        public void AllocateToCycle(Guid cycleId)
        {
            if (Status != TransactionStatus.Pending)
                throw new DomainException($"Cannot allocate transaction {Id}: status is {Status}");

            SettlementCycleId = cycleId;
            Status = TransactionStatus.Allocated;
            AllocatedAt = DateTime.UtcNow;
        }

        public void MarkNetted()
        {
            if (Status != TransactionStatus.Allocated)
                throw new DomainException($"Cannot mark transaction {Id} as netted: status is {Status}");
            Status = TransactionStatus.Netted;
        }

        public void MarkSettled()
        {
            if (Status != TransactionStatus.Netted)
                throw new DomainException($"Cannot mark transaction {Id} as settled: status is {Status}");
            Status = TransactionStatus.Settled;
        }

        /// <summary>
        /// Determines settlement direction from NACHA transaction code.
        /// Credit (22/32): ODFI is debtor (pays out), RDFI is creditor (receives).
        /// Debit  (27/37): RDFI is debtor (funds pulled), ODFI is creditor (receives).
        /// </summary>
        public bool IsPrenote =>
            TransactionType == TransactionType.Prenote;

        /// <summary>
        /// For a credit: OriginatingRtn owes ReceivingRtn.
        /// For a debit:  ReceivingRtn owes OriginatingRtn.
        /// Returns (debitingRtn, creditingRtn) for BilateralPosition update.
        /// </summary>
        public (string DebitingRtn, string CreditingRtn) GetSettlementDirection() =>
            TransactionType == TransactionType.Credit
                ? (OriginatingRtn, ReceivingRtn)   // ODFI pays RDFI
                : (ReceivingRtn, OriginatingRtn); // RDFI pays ODFI

        private static TransactionType DeriveTransactionType(NachaTransactionCode code) => code switch
        {
            NachaTransactionCode.CheckingCredit or
            NachaTransactionCode.SavingsCredit or
            NachaTransactionCode.GLCredit or
            NachaTransactionCode.LoanCredit => TransactionType.Credit,

            NachaTransactionCode.CheckingDebit or
            NachaTransactionCode.SavingsDebit or
            NachaTransactionCode.GLDebit => TransactionType.Debit,

            _ => TransactionType.Prenote
        };

        private static AccountType DeriveAccountType(NachaTransactionCode code) => code switch
        {
            NachaTransactionCode.CheckingCredit or
            NachaTransactionCode.CheckingCredit_Prenote or
            NachaTransactionCode.CheckingDebit or
            NachaTransactionCode.CheckingDebit_Prenote => AccountType.Checking,

            NachaTransactionCode.SavingsCredit or
            NachaTransactionCode.SavingsCredit_Prenote or
            NachaTransactionCode.SavingsDebit or
            NachaTransactionCode.SavingsDebit_Prenote => AccountType.Savings,

            NachaTransactionCode.GLCredit or
            NachaTransactionCode.GLDebit => AccountType.GeneralLedger,

            NachaTransactionCode.LoanCredit => AccountType.Loan,
            _ => AccountType.Checking
        };
    }

    public sealed record TransactionAllocatedEvent(
    Guid TransactionId, Guid CycleId) : IDomainEvent;
}
