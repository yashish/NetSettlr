using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    // ─────────────────────────────────────────────────────────────────────────────
    // NettingCalculator
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Consumes allocated transactions for a closed cycle and accumulates
    /// bilateral positions. Called by the netting worker after a cycle closes.
    ///
    /// For each non-prenote transaction:
    ///   Credit (22/32): OriginatingRTN is DEBTOR, ReceivingRTN is CREDITOR
    ///   Debit  (27/37): ReceivingRTN is DEBTOR, OriginatingRTN is CREDITOR
    ///
    /// Uses optimistic concurrency (RowVersion) on BilateralPosition.
    /// Caller must handle DbUpdateConcurrencyException with retry.
    /// </summary>
    public sealed class NettingCalculator(
        ITransactionRepository transactionRepo,
        IBilateralPositionRepository positionRepo,
        IUnitOfWork uow,
        ILogger<NettingCalculator> logger)
    {
        /// <summary>
        /// Processes all allocated transactions for the given cycle.
        /// Returns the number of transactions netted.
        /// </summary>
        public async Task<int> NetCycleAsync(Guid cycleId, CancellationToken ct = default)
        {
            logger.LogInformation("Starting netting for cycle {CycleId}", cycleId);

            var transactions = await transactionRepo.GetByCycleIdAsync(cycleId, ct);
            var toNet = transactions
                .Where(t => t.Status == TransactionStatus.Allocated && !t.IsPrenote)
                .ToList();

            if (toNet.Count == 0)
            {
                logger.LogWarning("Cycle {CycleId} has no transactions to net", cycleId);
                return 0;
            }

            // Load existing positions into a local dictionary to minimise DB round-trips
            var existingPositions = (await positionRepo.GetByCycleAsync(cycleId, ct))
                .ToDictionary(p => (p.DebitingRtn, p.CreditingRtn));

            int count = 0;
            foreach (var tx in toNet)
            {
                var (debitingRtn, creditingRtn) = tx.GetSettlementDirection();

                if (!existingPositions.TryGetValue((debitingRtn, creditingRtn), out var position))
                {
                    position = BilateralPosition.Create(cycleId, debitingRtn, creditingRtn);
                    await positionRepo.AddAsync(position, ct);
                    existingPositions[(debitingRtn, creditingRtn)] = position;
                }

                position.Accumulate(tx.AmountCents);
                positionRepo.Update(position);

                tx.MarkNetted();
                transactionRepo.Update(tx);
                count++;
            }

            await uow.SaveChangesAsync(ct);

            logger.LogInformation(
                "Netting complete for cycle {CycleId}: {Count} transactions, {Positions} bilateral positions",
                cycleId, count, existingPositions.Count);

            return count;
        }
    }
}
