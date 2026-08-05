using System.Text;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.Infrastructure.Reporting;

public sealed class MarkdownReportWriter : IReportWriter
{
    private static readonly Encoding MarkdownEncoding = new UTF8Encoding(false);
    public ReportFormat Format => ReportFormat.Markdown;

    public async Task<long> WriteAsync(ReportDocument document, Stream destination, ReportExportRequest request,
        IProgress<ReportProgress>? progress, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, MarkdownEncoding, 64 * 1024, true);
        await writer.WriteLineAsync($"# {Escape(document.Title)}".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
        if (request.IncludeMetadata)
        {
            await WriteMetadataAsync(writer, document.Metadata, cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteLineAsync("## Summary".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync($"Assets: {document.Metadata.AssetCount}; estimated rows: {document.Metadata.RowCount}.".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync("## Rows".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);

        using var enumerator = document.Rows.GetEnumerator();
        var hasRows = enumerator.MoveNext();
        var fields = hasRows ? ReportRowProjection.Fields(enumerator.Current) : [];
        var headers = hasRows ? fields.Select(static field => field.Key) : ReportHeaders.For(request.Profile);
        await writer.WriteLineAsync($"| {string.Join(" | ", headers.Select(Escape))} |".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync($"| {string.Join(" | ", headers.Select(static _ => "---"))} |".AsMemory(), cancellationToken).ConfigureAwait(false);
        if (!hasRows)
        {
            await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
            await writer.WriteLineAsync("_No rows._".AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }


        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long count = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            fields = ReportRowProjection.Fields(enumerator.Current);
            await writer.WriteLineAsync($"| {string.Join(" | ", fields.Select(field => Escape(ReportRowProjection.Format(field.Value))))} |".AsMemory(), cancellationToken).ConfigureAwait(false);
            count++;
            if (count % 128 == 0)
            {
                progress?.Report(new(ReportExportPhase.WritingReport, (int)Math.Min(count, int.MaxValue), count, stopwatch.Elapsed));
            }
        }
        while (enumerator.MoveNext());

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new(ReportExportPhase.WritingReport, document.Metadata.AssetCount, count, stopwatch.Elapsed));
        return count;
    }

    internal static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("\r\n", "<br>", StringComparison.Ordinal)
        .Replace("\r", "<br>", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    private static async Task WriteMetadataAsync(StreamWriter writer, ReportMetadataDto metadata, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync("## Metadata".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
        foreach (var pair in new[]
                 {
                     ("Report schema version", metadata.ReportSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                     ("Report type", metadata.ReportType), ("Format", metadata.ExportFormat),
                     ("Generated at UTC", metadata.GeneratedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                     ("Application version", metadata.ApplicationVersion), ("Commit", metadata.CommitSha),
                     ("Source scope", metadata.SourceScope), ("Filters", metadata.FilterSummary),
                     ("Sort", metadata.SortSummary), ("Asset count", metadata.AssetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                     ("Row count", metadata.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                 })
        {
            await writer.WriteLineAsync($"- **{Escape(pair.Item1)}:** {Escape(pair.Item2)}".AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.WriteLineAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
    }
}
