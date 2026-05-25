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
}
