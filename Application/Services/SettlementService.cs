using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Services
{
    // ─────────────────────────────────────────────────────────────────────────────
    // SettlementService
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes final multilateral net obligations from bilateral positions
    /// and submits them to the Federal Reserve (simulated in POC).
    ///
    /// Critical invariants enforced before submission:
    ///   1. Zero-sum: SUM(NetAmountCents) across all obligations == 0
    ///   2. NetDebitCap: no participant's net debit exceeds their registered cap
    ///
    /// If either invariant fails, the cycle is marked Failed and settlement halts.
    /// These are hard stops — do not proceed with partial settlement.
    /// </summary>
    public sealed class SettlementService(
        ISettlementCycleRepository cycleRepo,
        IBilateralPositionRepository positionRepo,
        ISettlementObligationRepository obligationRepo,
        IParticipantRepository participantRepo,
        IUnitOfWork uow,
        ILogger<SettlementService> logger)
    {
        public async Task<SettlementResult> SettleCycleAsync(Guid cycleId, CancellationToken ct = default)
        {
            var cycle = await cycleRepo.GetByIdAsync(cycleId, ct)
                ?? throw new InvalidOperationException($"Cycle {cycleId} not found");

            if (cycle.Status != SettlementCycleStatus.Closed)
                throw new InvalidCycleTransitionException(cycle.Status.ToString(), "Settled");

            logger.LogInformation("Computing settlement obligations for cycle {Reference}", cycle.CycleReference);

            try
            {
                var obligations = await ComputeObligationsAsync(cycle, ct);
                await PersistAndSubmitAsync(cycle, obligations, ct);
                return SettlementResult.Success(obligations.Count);
            }
            catch (Exception ex) when (ex is InvariantViolationException or NetDebitCapExceededException)
            {
                logger.LogCritical(ex,
                    "Settlement invariant violated for cycle {Reference} — halting settlement", cycle.CycleReference);

                cycle.MarkFailed(ex.Message);
                cycleRepo.Update(cycle);
                await uow.SaveChangesAsync(ct);

                return SettlementResult.Failed(ex.Message);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private async Task<List<SettlementObligation>> ComputeObligationsAsync(
            SettlementCycle cycle, CancellationToken ct)
        {
            var positions = await positionRepo.GetByCycleAsync(cycle.Id, ct);

            if (positions.Count == 0)
                throw new InvariantViolationException($"Cycle {cycle.CycleReference} has no bilateral positions to settle");

            // Aggregate bilateral flows into per-participant net positions
            // positive = net creditor, negative = net debtor
            var netPositions = new Dictionary<string, (long Credits, long Debits)>();

            foreach (var pos in positions)
            {
                Accumulate(netPositions, pos.CreditingRtn, credits: pos.GrossAmountCents, debits: 0);
                Accumulate(netPositions, pos.DebitingRtn, credits: 0, debits: pos.GrossAmountCents);
            }

            // ── Invariant 1: Zero-sum check ───────────────────────────────────────
            long sumOfNets = netPositions.Values.Sum(p => p.Credits - p.Debits);
            if (sumOfNets != 0)
                throw new InvariantViolationException(
                    $"Zero-sum invariant violated for cycle {cycle.CycleReference}: " +
                    $"net positions sum to {sumOfNets:N0} cents (must be 0). " +
                    "This indicates a netting computation error and requires investigation.");

            logger.LogInformation("Zero-sum invariant passed for cycle {Reference}", cycle.CycleReference);

            // ── Invariant 2: Net debit cap check ─────────────────────────────────
            var obligations = new List<SettlementObligation>(netPositions.Count);

            foreach (var (rtn, (credits, debits)) in netPositions)
            {
                var netAmount = credits - debits;

                if (netAmount < 0) // Net debtor
                {
                    var participant = await participantRepo.GetByRtnAsync(rtn, ct);
                    if (participant is not null && Math.Abs(netAmount) > participant.NetDebitCap.Cents)
                        throw new NetDebitCapExceededException(rtn, participant.NetDebitCap.Cents, Math.Abs(netAmount));
                }

                obligations.Add(SettlementObligation.Compute(cycle.Id, rtn, credits, debits));
            }

            logger.LogInformation(
                "Net debit cap check passed. {Count} obligations computed for cycle {Reference}",
                obligations.Count, cycle.CycleReference);

            return obligations;
        }

        private async Task PersistAndSubmitAsync(
            SettlementCycle cycle,
            List<SettlementObligation> obligations,
            CancellationToken ct)
        {
            foreach (var obligation in obligations)
            {
                await obligationRepo.AddAsync(obligation, ct);
            }

            // Submit each obligation to the Fed (simulated in POC)
            foreach (var obligation in obligations)
            {
                obligation.MarkSubmitted();

                // POC: simulate Fed confirmation with a generated reference
                var fedRef = $"FED-{cycle.CycleReference}-{obligation.ParticipantRtn}";
                obligation.MarkConfirmed(fedRef);

                obligationRepo.Update(obligation);

                logger.LogInformation(
                    "Obligation submitted for {Rtn}: net {NetCents:N0} cents ({Direction}) | FedRef: {Ref}",
                    obligation.ParticipantRtn,
                    Math.Abs(obligation.NetAmountCents),
                    obligation.IsNetDebtor ? "DEBIT" : "CREDIT",
                    fedRef);
            }

            cycle.MarkSettled();
            cycleRepo.Update(cycle);

            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Cycle {Reference} settled successfully", cycle.CycleReference);
        }

        private static void Accumulate(
            Dictionary<string, (long Credits, long Debits)> dict,
            string rtn, long credits, long debits)
        {
            if (!dict.TryGetValue(rtn, out var existing))
                dict[rtn] = (credits, debits);
            else
                dict[rtn] = (existing.Credits + credits, existing.Debits + debits);
        }
    }

    public sealed record SettlementResult(bool Succeeded, int ObligationCount, string? ErrorMessage)
    {
        public static SettlementResult Success(int count) => new(true, count, null);
        public static SettlementResult Failed(string error) => new(false, 0, error);
    }
}
