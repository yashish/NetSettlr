using Domain.Enums;
using Domain.Exceptions;

namespace Domain.Entities
{
    // ─────────────────────────────────────────────────────────────────────────────
    // NachaFile
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents one NACHA file submission. Immutable once accepted.
    /// The S3 key retains the original bytes permanently for audit.
    /// </summary>
    public sealed class NachaFile : BaseEntity
    {
        private NachaFile() { }

        /// <summary>
        /// Composite idempotency key: OriginatingRTN + FileCreationDate(YYMMDD) + FileIdModifier.
        /// Matches the NACHA spec definition of file uniqueness.
        /// </summary>
        public string IdempotencyKey { get; private init; } = string.Empty;
        public string S3Key { get; private init; } = string.Empty;
        public string OriginatingRtn { get; private init; } = string.Empty;
        public string DestinationRtn { get; private init; } = string.Empty;
        public DateOnly FileCreationDate { get; private init; }
        public TimeOnly FileCreationTime { get; private init; }
        public char FileIdModifier { get; private init; }
        public NachaFileStatus Status { get; private set; }
        public long TotalDebitCents { get; private set; }
        public long TotalCreditCents { get; private set; }
        public int BatchCount { get; private set; }
        public int EntryAddendaCount { get; private set; }
        public long EntryHash { get; private set; }
        public DateTime ReceivedAt { get; private init; }
        public DateTime? ProcessedAt { get; private set; }
        public string? RejectionReason { get; private set; }

        private readonly List<Batch> _batches = [];
        public IReadOnlyList<Batch> Batches => _batches.AsReadOnly();

        public static NachaFile Create(
            string s3Key,
            string originatingRtn,
            string destinationRtn,
            DateOnly creationDate,
            TimeOnly creationTime,
            char fileIdModifier,
            long totalDebitCents,
            long totalCreditCents,
            int batchCount,
            int entryAddendaCount,
            long entryHash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(s3Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(originatingRtn);

            var idempotencyKey = BuildIdempotencyKey(originatingRtn, creationDate, fileIdModifier);

            return new NachaFile
            {
                IdempotencyKey = idempotencyKey,
                S3Key = s3Key,
                OriginatingRtn = originatingRtn,
                DestinationRtn = destinationRtn,
                FileCreationDate = creationDate,
                FileCreationTime = creationTime,
                FileIdModifier = fileIdModifier,
                Status = NachaFileStatus.Received,
                TotalDebitCents = totalDebitCents,
                TotalCreditCents = totalCreditCents,
                BatchCount = batchCount,
                EntryAddendaCount = entryAddendaCount,
                EntryHash = entryHash,
                ReceivedAt = DateTime.UtcNow
            };
        }

        public void AddBatch(Batch batch) => _batches.Add(batch);

        public void MarkAccepted()
        {
            Status = NachaFileStatus.Accepted;
            ProcessedAt = DateTime.UtcNow;
        }

        public void MarkRejected(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Status = NachaFileStatus.Rejected;
            RejectionReason = reason;
            ProcessedAt = DateTime.UtcNow;
            RaiseDomainEvent(new NachaFileRejectedEvent(Id, IdempotencyKey, reason));
        }

        public void MarkProcessed()
        {
            if (Status != NachaFileStatus.Accepted)
                throw new DomainException("Only accepted files can be marked processed");

            Status = NachaFileStatus.Processed;
            ProcessedAt = DateTime.UtcNow;
        }

        public static string BuildIdempotencyKey(string rtn, DateOnly date, char modifier) =>
            $"{rtn}{date:yyMMdd}{modifier}";
    }

    // Domain event
    public sealed record NachaFileRejectedEvent(
        Guid FileId, string IdempotencyKey, string Reason) : IDomainEvent;
}
