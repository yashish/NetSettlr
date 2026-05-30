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
    /// <summary>
    /// Parses a NACHA ACH file into a fully-constructed domain object graph.
    ///
    /// NACHA fixed-width format rules:
    ///   — Every record is exactly 94 characters + newline
    ///   — Record type is always character 0
    ///   — All amounts are integer cents (no decimal point)
    ///   — Files are padded to a multiple of 10 records with all-9 padding lines
    ///
    /// Validation performed here (structural):
    ///   — Record length (94)
    ///   — Batch entry hash (sum of receiving RTNs)
    ///   — Batch debit/credit totals
    ///   — File-level batch count, entry count, entry hash
    ///   — Sequential record ordering (header → batches → footer)
    ///
    /// RTN existence validation is performed by the calling command handler
    /// against the participant cache — not here.
    /// </summary>
    public sealed class NachaFileParser(ILogger<NachaFileParser> logger)
    {
        private const int RecordLength = 94;
        private const string PaddingRecord = "9999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999";

        public async Task<NachaFile> ParseAsync(
            Stream content,
            string s3Key,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(s3Key);

            var lines = await ReadLinesAsync(content, ct);

            if (lines.Count == 0)
                throw new NachaParseException("File is empty");

            if (lines[0].Length != RecordLength || lines[0][0] != '1')
                throw new NachaParseException("First record must be a File Header (type 1)", 1);

            var header = ParseFileHeader(lines[0].AsSpan(), lineNo: 1);
            NachaFile? file = null;
            Batch? currentBatch = null;
            int lineNumber = 1;

            // Accumulators for file-level validation
            int batchCount = 0;
            long fileEntryCount = 0;
            long fileEntryHash = 0;
            long fileTotalDebitCents = 0;
            long fileTotalCreditCents = 0;

            for (int i = 1; i < lines.Count; i++)
            {
                lineNumber = i + 1;
                var line = lines[i].AsSpan();

                if (line.Length == 0) continue;

                if (line.Length != RecordLength)
                    throw new NachaParseException($"Record must be {RecordLength} chars, got {line.Length}", lineNumber);

                var recordType = line[0];

                switch (recordType)
                {
                    case '5': // Batch Header
                        if (currentBatch is not null)
                            throw new NachaParseException("Batch header found before previous batch was closed", lineNumber);

                        var batchHeader = ParseBatchHeader(line, lineNumber);

                        if (file is null)
                        {
                            // Build NachaFile lazily — we need all data before constructing
                            // but we create it here on the first batch (after header is parsed)
                            file = NachaFile.Create(
                                s3Key: s3Key,
                                originatingRtn: header.OriginatingRtn,
                                destinationRtn: header.DestinationRtn,
                                creationDate: header.CreationDate,
                                creationTime: header.CreationTime,
                                fileIdModifier: header.FileIdModifier,
                                totalDebitCents: 0,   // Updated from File Control
                                totalCreditCents: 0,
                                batchCount: 0,
                                entryAddendaCount: 0,
                                entryHash: 0);
                        }

                        currentBatch = Batch.Create(
                            nachaFileId: file.Id,
                            sequenceNumber: batchCount + 1,
                            serviceClassCode: batchHeader.ServiceClassCode,
                            companyName: batchHeader.CompanyName,
                            companyId: batchHeader.CompanyId,
                            secCode: batchHeader.SecCode,
                            entryDescription: batchHeader.EntryDescription,
                            effectiveEntryDate: batchHeader.EffectiveEntryDate,
                            originatingDfi: batchHeader.OriginatingDfi,
                            batchNumber: batchHeader.BatchNumber,
                            totalDebitCents: 0,   // validated against Batch Control
                            totalCreditCents: 0,
                            entryAddendaCount: 0,
                            entryHash: 0);
                        break;

                    case '6': // Entry Detail
                        if (currentBatch is null)
                            throw new NachaParseException("Entry Detail found outside of a batch", lineNumber);

                        var entry = ParseEntryDetail(line, lineNumber, currentBatch.Id, currentBatch.OriginatingDfi, currentBatch.EffectiveEntryDate);
                        currentBatch.AddTransaction(entry);
                        break;

                    case '7': // Addenda — skip in POC (not needed for netting)
                        logger.LogDebug("Addenda record at line {Line} skipped", lineNumber);
                        break;

                    case '8': // Batch Control
                        if (currentBatch is null)
                            throw new NachaParseException("Batch Control found without an open batch", lineNumber);

                        var batchControl = ParseBatchControl(line, lineNumber);
                        ValidateBatchControl(currentBatch, batchControl, lineNumber);

                        currentBatch.Accept();
                        file!.AddBatch(currentBatch);

                        batchCount++;
                        fileEntryCount += batchControl.EntryAddendaCount;
                        fileEntryHash += batchControl.EntryHash;
                        fileTotalDebitCents += batchControl.TotalDebitCents;
                        fileTotalCreditCents += batchControl.TotalCreditCents;

                        currentBatch = null;
                        break;

                    case '9':
                        // Padding records are all 9s — skip them
                        if (lines[i] == PaddingRecord) continue;

                        // File Control record
                        var fileControl = ParseFileControl(line, lineNumber);
                        ValidateFileControl(fileControl, batchCount, fileEntryCount,
                            fileEntryHash, fileTotalDebitCents, fileTotalCreditCents, lineNumber);
                        goto doneReading;

                    default:
                        throw new NachaParseException($"Unknown record type '{recordType}'", lineNumber);
                }
            }

        doneReading:
            if (file is null)
                throw new NachaParseException("File contains no batches");

            if (currentBatch is not null)
                throw new NachaParseException("File ended with an unclosed batch");

            file.MarkAccepted();
            logger.LogInformation(
                "Parsed NACHA file {S3Key}: {BatchCount} batches, {TxCount} transactions",
                s3Key, file.Batches.Count,
                file.Batches.Sum(b => b.Transactions.Count));

            return file;
        }

        // ── Record Parsers ────────────────────────────────────────────────────────

        private static FileHeaderRecord ParseFileHeader(ReadOnlySpan<char> line, int lineNo)
        {
            // Destination RTN is at positions 3-12 (leading space + 9 digits)
            var destinationRtn = line[4..13].ToString().Trim();
            var originatingRtn = line[13..23].ToString().Trim();
            var dateStr = line[23..29].ToString();  // YYMMDD
            var timeStr = line[29..33].ToString();  // HHMM
            var modifier = line[33];

            if (!DateOnly.TryParseExact(dateStr, "yyMMdd", out var creationDate))
                throw new NachaParseException($"Invalid file creation date '{dateStr}'", lineNo);

            if (!TimeOnly.TryParseExact(timeStr, "HHmm", out var creationTime))
                creationTime = TimeOnly.MinValue; // Time is optional per spec

            return new FileHeaderRecord(destinationRtn, originatingRtn, creationDate, creationTime, modifier);
        }

        private static BatchHeaderRecord ParseBatchHeader(ReadOnlySpan<char> line, int lineNo)
        {
            var serviceClassCode = ParseInt(line[1..4], lineNo, "Service Class Code");
            var companyName = line[4..20].ToString().TrimEnd();
            var companyId = line[40..50].ToString().TrimEnd();
            var secCodeStr = line[50..53].ToString().Trim();
            var entryDesc = line[53..63].ToString().TrimEnd();
            var effectiveDateStr = line[69..75].ToString();
            var originatingDfi = line[79..87].ToString().Trim();
            var batchNumber = ParseInt(line[87..94], lineNo, "Batch Number");

            if (!Enum.TryParse<ServiceClassCode>(serviceClassCode.ToString(), out var scc))
                throw new NachaParseException($"Unknown Service Class Code {serviceClassCode}", lineNo);

            if (!Enum.TryParse<SecCode>(secCodeStr, ignoreCase: true, out var secCode))
                throw new NachaParseException($"Unknown SEC code '{secCodeStr}'", lineNo);

            if (!DateOnly.TryParseExact(effectiveDateStr, "yyMMdd", out var effectiveDate))
                throw new NachaParseException($"Invalid effective entry date '{effectiveDateStr}'", lineNo);

            return new BatchHeaderRecord(scc, companyName, companyId, secCode,
                entryDesc, effectiveDate, originatingDfi, batchNumber);
        }

        private static Transaction ParseEntryDetail(
            ReadOnlySpan<char> line, int lineNo, Guid batchId,
            string originatingDfi, DateOnly effectiveDate)
        {
            var txCodeRaw = ParseInt(line[1..3], lineNo, "Transaction Code");
            var receivingDfi = line[3..11].ToString().Trim();
            var checkDigit = line[11];
            var accountNumber = line[12..29].ToString().TrimEnd();
            var amountCents = ParseLong(line[29..39], lineNo, "Amount");
            var individualName = line[54..76].ToString().TrimEnd();
            var traceNumber = line[79..94].ToString().Trim();

            // Reconstruct the full 9-digit receiving RTN (8-digit DFI + check digit)
            var receivingRtn = receivingDfi + checkDigit;

            // Full originating RTN needs check digit — it's stored as 8 digits in batch header.
            // For netting purposes the 8-digit form is sufficient to identify the institution;
            // we carry it as-is here and resolve against the participant registry by prefix match.
            var originatingRtn = originatingDfi;

            if (!Enum.TryParse<NachaTransactionCode>(txCodeRaw.ToString(), out var txCode))
                throw new NachaParseException($"Unknown transaction code {txCodeRaw}", lineNo);

            return Transaction.Create(
                batchId: batchId,
                traceNumber: traceNumber,
                transactionCode: txCode,
                originatingRtn: originatingRtn,
                receivingRtn: receivingRtn,
                amountCents: amountCents,
                individualName: individualName,
                accountNumber: accountNumber,
                effectiveDate: effectiveDate);
        }

        private static BatchControlRecord ParseBatchControl(ReadOnlySpan<char> line, int lineNo)
        {
            var entryCount = ParseInt(line[4..10], lineNo, "Entry Count");
            var entryHash = ParseLong(line[10..20], lineNo, "Entry Hash");
            var totalDebit = ParseLong(line[20..32], lineNo, "Total Debit");
            var totalCredit = ParseLong(line[32..44], lineNo, "Total Credit");
            var originatingDfi = line[79..87].ToString().Trim();

            return new BatchControlRecord(entryCount, entryHash, totalDebit, totalCredit, originatingDfi);
        }

        private static FileControlRecord ParseFileControl(ReadOnlySpan<char> line, int lineNo)
        {
            var batchCount = ParseInt(line[1..7], lineNo, "Batch Count");
            var entryCount = ParseLong(line[13..21], lineNo, "Entry Count");
            var entryHash = ParseLong(line[21..31], lineNo, "Entry Hash");
            var totalDebit = ParseLong(line[31..43], lineNo, "Total Debit");
            var totalCredit = ParseLong(line[43..55], lineNo, "Total Credit");

            return new FileControlRecord(batchCount, entryCount, entryHash, totalDebit, totalCredit);
        }

        // ── Validation ────────────────────────────────────────────────────────────

        private static void ValidateBatchControl(
            Batch batch, BatchControlRecord control, int lineNo)
        {
            var transactions = batch.Transactions;

            // Entry/addenda count
            if (transactions.Count != control.EntryAddendaCount)
                throw new NachaParseException(
                    $"Batch entry count mismatch: expected {control.EntryAddendaCount}, found {transactions.Count}", lineNo);

            // Entry hash: sum of receiving DFI RTNs (first 8 digits), last 10 digits only
            long computedHash = transactions.Sum(t =>
                long.TryParse(t.ReceivingRtn[..Math.Min(8, t.ReceivingRtn.Length)], out var v) ? v : 0L);
            long hashLastTen = computedHash % 10_000_000_000L;

            if (hashLastTen != control.EntryHash % 10_000_000_000L)
                throw new NachaParseException(
                    $"Batch entry hash mismatch: expected {control.EntryHash}, computed {computedHash}", lineNo);

            // Debit/credit totals
            long computedDebit = transactions.Where(t => t.TransactionType == TransactionType.Debit).Sum(t => t.AmountCents);
            long computedCredit = transactions.Where(t => t.TransactionType == TransactionType.Credit).Sum(t => t.AmountCents);

            if (computedDebit != control.TotalDebitCents)
                throw new NachaParseException(
                    $"Batch debit total mismatch: expected {control.TotalDebitCents}, computed {computedDebit}", lineNo);

            if (computedCredit != control.TotalCreditCents)
                throw new NachaParseException(
                    $"Batch credit total mismatch: expected {control.TotalCreditCents}, computed {computedCredit}", lineNo);
        }

        private static void ValidateFileControl(
            FileControlRecord control, int actualBatchCount,
            long actualEntryCount, long actualEntryHash,
            long actualDebit, long actualCredit, int lineNo)
        {
            if (actualBatchCount != control.BatchCount)
                throw new NachaParseException(
                    $"File batch count mismatch: expected {control.BatchCount}, found {actualBatchCount}", lineNo);

            if (actualEntryCount != control.EntryAddendaCount)
                throw new NachaParseException(
                    $"File entry count mismatch: expected {control.EntryAddendaCount}, found {actualEntryCount}", lineNo);

            if (actualEntryHash % 10_000_000_000L != control.EntryHash % 10_000_000_000L)
                throw new NachaParseException(
                    $"File entry hash mismatch: expected {control.EntryHash}, computed {actualEntryHash}", lineNo);

            if (actualDebit != control.TotalDebitCents)
                throw new NachaParseException(
                    $"File debit total mismatch: expected {control.TotalDebitCents}, computed {actualDebit}", lineNo);

            if (actualCredit != control.TotalCreditCents)
                throw new NachaParseException(
                    $"File credit total mismatch: expected {control.TotalCreditCents}, computed {actualCredit}", lineNo);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static async Task<List<string>> ReadLinesAsync(Stream stream, CancellationToken ct)
        {
            var lines = new List<string>(capacity: 10_000);
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }

        private static int ParseInt(ReadOnlySpan<char> span, int lineNo, string fieldName)
        {
            if (!int.TryParse(span, out var value))
                throw new NachaParseException($"Field '{fieldName}' is not a valid integer: '{span}'", lineNo);
            return value;
        }

        private static long ParseLong(ReadOnlySpan<char> span, int lineNo, string fieldName)
        {
            if (!long.TryParse(span, out var value))
                throw new NachaParseException($"Field '{fieldName}' is not a valid long: '{span}'", lineNo);
            return value;
        }

        // ── Private record types (parse intermediaries, not domain objects) ───────

        private sealed record FileHeaderRecord(
            string DestinationRtn, string OriginatingRtn,
            DateOnly CreationDate, TimeOnly CreationTime, char FileIdModifier);

        private sealed record BatchHeaderRecord(
            ServiceClassCode ServiceClassCode, string CompanyName, string CompanyId,
            SecCode SecCode, string EntryDescription, DateOnly EffectiveEntryDate,
            string OriginatingDfi, int BatchNumber);

        private sealed record BatchControlRecord(
            int EntryAddendaCount, long EntryHash,
            long TotalDebitCents, long TotalCreditCents, string OriginatingDfi);

        private sealed record FileControlRecord(
            int BatchCount, long EntryAddendaCount,
            long EntryHash, long TotalDebitCents, long TotalCreditCents);
    }
}
