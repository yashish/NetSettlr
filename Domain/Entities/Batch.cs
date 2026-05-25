using Domain.Enums;

namespace Domain.Entities
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Batch
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class Batch : BaseEntity
    {
        private Batch() { }

        public Guid NachaFileId { get; private init; }
        public int BatchSequenceNumber { get; private init; }
        public ServiceClassCode ServiceClassCode { get; private init; }
        public string CompanyName { get; private init; } = string.Empty;
        public string CompanyId { get; private init; } = string.Empty;
        public SecCode SecCode { get; private init; }
        public string EntryDescription { get; private init; } = string.Empty;
        public DateOnly EffectiveEntryDate { get; private init; }
        /// <summary>
        /// 8-digit originating DFI (RTN without check digit) from Batch Header field positions 73-80.
        /// </summary>
        public string OriginatingDfi { get; private init; } = string.Empty;
        public int BatchNumber { get; private init; }
        public BatchStatus Status { get; private set; }
        public long TotalDebitCents { get; private init; }
        public long TotalCreditCents { get; private init; }
        public int EntryAddendaCount { get; private init; }
        public long EntryHash { get; private init; }

        private readonly List<Transaction> _transactions = [];
        public IReadOnlyList<Transaction> Transactions => _transactions.AsReadOnly();

        public static Batch Create(
            Guid nachaFileId,
            int sequenceNumber,
            ServiceClassCode serviceClassCode,
            string companyName,
            string companyId,
            SecCode secCode,
            string entryDescription,
            DateOnly effectiveEntryDate,
            string originatingDfi,
            int batchNumber,
            long totalDebitCents,
            long totalCreditCents,
            int entryAddendaCount,
            long entryHash)
        {
            return new Batch
            {
                NachaFileId = nachaFileId,
                BatchSequenceNumber = sequenceNumber,
                ServiceClassCode = serviceClassCode,
                CompanyName = companyName.Trim(),
                CompanyId = companyId.Trim(),
                SecCode = secCode,
                EntryDescription = entryDescription.Trim(),
                EffectiveEntryDate = effectiveEntryDate,
                OriginatingDfi = originatingDfi.Trim(),
                BatchNumber = batchNumber,
                Status = BatchStatus.Pending,
                TotalDebitCents = totalDebitCents,
                TotalCreditCents = totalCreditCents,
                EntryAddendaCount = entryAddendaCount,
                EntryHash = entryHash
            };
        }

        public void AddTransaction(Transaction transaction) => _transactions.Add(transaction);
        public void Accept() => Status = BatchStatus.Accepted;
        public void Reject() => Status = BatchStatus.Rejected;
    }
}
