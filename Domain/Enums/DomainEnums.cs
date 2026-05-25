using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Enums
{
    // ── Participant ──────────────────────────────────────────────────────────────
    public enum ParticipantStatus
    {
        Active,
        Suspended,    // Temporarily blocked; existing cycles still settle
        Terminated    // Permanently removed; all new submissions rejected
    }

    // ── NACHA File ───────────────────────────────────────────────────────────────
    public enum NachaFileStatus
    {
        Received,     // Arrived in S3, not yet parsed
        Processing,   // Ingestion service has started parsing
        Accepted,     // Passed all structural and checksum validations
        Rejected,     // Failed validation; see RejectionReason
        Processed     // All transactions emitted to Kafka
    }

    // ── Batch ────────────────────────────────────────────────────────────────────
    public enum BatchStatus
    {
        Pending,
        Accepted,
        Rejected
    }

    /// <summary>
    /// Standard Entry Class codes define the transaction type and applicable rules.
    /// </summary>
    public enum SecCode
    {
        PPD,   // Prearranged Payment and Deposit — consumer payroll, recurring bills
        CCD,   // Corporate Credit or Debit — B2B payments
        WEB,   // Internet-initiated entries — online banking originated
        TEL,   // Telephone-initiated entries
        CTX,   // Corporate Trade Exchange — with remittance addenda
        ARC,   // Accounts Receivable Entry
        BOC,   // Back Office Conversion
        POP,   // Point of Purchase
        RCK,   // Re-presented Check Entry
        IAT    // International ACH Transaction
    }

    /// <summary>
    /// Service class code from NACHA Batch Header record (field positions 2-4).
    /// </summary>
    public enum ServiceClassCode
    {
        Mixed = 200,  // Batch contains both credits and debits
        Credits = 220,  // Batch contains only credit entries
        Debits = 225   // Batch contains only debit entries
    }

    // ── Transaction ──────────────────────────────────────────────────────────────

    /// <summary>
    /// NACHA transaction code (field position 2 of Entry Detail record).
    /// Encodes both the direction (credit/debit) and account type.
    /// </summary>
    public enum NachaTransactionCode
    {
        CheckingCredit = 22,
        CheckingCredit_Prenote = 23, // Zero-dollar; establishes account
        CheckingDebit = 27,
        CheckingDebit_Prenote = 28,
        SavingsCredit = 32,
        SavingsCredit_Prenote = 33,
        SavingsDebit = 37,
        SavingsDebit_Prenote = 38,
        GLCredit = 42,   // General Ledger
        GLDebit = 47,
        LoanCredit = 52
    }

    public enum TransactionType
    {
        Credit,   // Money flows TO the RDFI (receiving bank gets credited at Fed)
        Debit,    // Money flows FROM the RDFI (receiving bank gets debited at Fed)
        Prenote   // Zero-dollar test; excluded from netting
    }

    public enum AccountType
    {
        Checking,
        Savings,
        GeneralLedger,
        Loan
    }

    /// <summary>
    /// Lifecycle of a single transaction through the settlement pipeline.
    /// </summary>
    public enum TransactionStatus
    {
        Pending,     // Validated and persisted; not yet assigned to a cycle
        Allocated,   // Assigned to an open SettlementCycle
        Netted,      // Incorporated into a BilateralPosition
        Settled,     // Cycle has settled with the Fed
        Returned     // Returned by RDFI (e.g. NSF); out of scope for this POC
    }

    // ── Settlement Cycle ──────────────────────────────────────────────────────────

    /// <summary>
    /// State machine for a settlement cycle.
    /// Open → Closing → Closed → Settled
    /// Any non-terminal state → Failed on critical error.
    /// </summary>
    public enum SettlementCycleStatus
    {
        Open,      // Accepting transactions
        Closing,   // Trigger fired; draining in-flight Kafka messages
        Closed,    // All transactions allocated; netting can begin
        Settled,   // Net obligations published to Fed; terminal
        Failed     // Requires manual intervention; no further transitions
    }

    public enum TriggerType
    {
        Manual,     // Operator-initiated via API
        Scheduled   // Time-window based (Phase 2)
    }

    // ── Settlement Obligation ────────────────────────────────────────────────────

    public enum SettlementObligationStatus
    {
        Computed,    // NetAmount calculated; not yet sent to Fed
        Submitted,   // Instruction sent to Fed
        Confirmed,   // Fed confirmed receipt and execution
        Failed       // Fed rejected or timed out; requires investigation
    }
}
