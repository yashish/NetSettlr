namespace Application.Abstractions;

public sealed record CachedParticipant(
    Guid Id,
    string Rtn,
    string LegalName,
    Domain.Enums.ParticipantStatus Status,
    long NetDebitCapCents,
    DateTimeOffset CachedAt);
