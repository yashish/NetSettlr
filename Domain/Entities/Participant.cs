using Domain.Enums;
using Domain.Exceptions;
using Domain.ValueObjects;

namespace Domain.Entities
{
    public sealed class Participant : BaseEntity
    {
        // Private parameterless constructor for EF Core
        private Participant() { }

        public RoutingTransitNumber RoutingTransitNumber { get; private init; } = null!;
        public string LegalName { get; private set; } = string.Empty;
        public string ShortName { get; private set; } = string.Empty; // As it appears in NACHA headers
        public ParticipantStatus Status { get; private set; }
        public Money NetDebitCap { get; private set; } = Money.Zero;   // Hard credit risk ceiling
        public DateOnly EffectiveDate { get; private init; }
        public DateOnly? TerminationDate { get; private set; }
        public DateTime CreatedAt { get; private init; }
        public DateTime UpdatedAt { get; private set; }

        public static Participant Create(
            string rtn,
            string legalName,
            string shortName,
            long netDebitCapCents,
            DateOnly effectiveDate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
            ArgumentException.ThrowIfNullOrWhiteSpace(shortName);

            if (netDebitCapCents <= 0)
                throw new DomainException($"Net debit cap must be positive. Got: {netDebitCapCents}") { };

            var now = DateTime.UtcNow;
            return new Participant
            {
                RoutingTransitNumber = RoutingTransitNumber.Parse(rtn),
                LegalName = legalName.Trim(),
                ShortName = shortName.Trim()[..Math.Min(shortName.Trim().Length, 20)],
                Status = ParticipantStatus.Active,
                NetDebitCap = Money.FromCents(netDebitCapCents),
                EffectiveDate = effectiveDate,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
    }

    // Domain events
    public sealed record ParticipantSuspendedEvent(Guid ParticipantId, string Rtn) : IDomainEvent;
    public sealed record ParticipantTerminatedEvent(Guid ParticipantId, string Rtn) : IDomainEvent;
}
