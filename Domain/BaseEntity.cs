namespace Domain
{
    /// <summary>
    /// Marker interface for domain events raised by entities.
    /// Consumed by the infrastructure layer after commit.
    /// </summary>
    public interface IDomainEvent { }

    /// <summary>
    /// Base Entity and Domain Event class for all entities and domain events in the system.
    /// Carries a surrogate UUID PK, a RowVersion for optimistic concurrency,
    /// and a collection of uncommitted domain events.
    /// </summary>
    public abstract class BaseEntity
    {
        public Guid Id { get; protected init; } = Guid.NewGuid();

        /// <summary>
        /// EF Core / database-managed concurrency token.
        /// Any concurrent write that does not carry the current token is rejected.
        /// </summary>
        public byte[] RowVersion { get; private set; } = [];

        private readonly List<IDomainEvent> _domainEvents = [];

        /// <summary>
        /// Domain events raised during this unit of work.
        /// The infrastructure layer dispatches these after the transaction commits.
        /// </summary>
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        // Identify parameter as @event because event is a reserved keyword in C#
        protected void RaiseDomainEvent(IDomainEvent @event) =>
            _domainEvents.Add(@event);

        public void ClearDomainEvents() =>
            _domainEvents.Clear();
    }
}
