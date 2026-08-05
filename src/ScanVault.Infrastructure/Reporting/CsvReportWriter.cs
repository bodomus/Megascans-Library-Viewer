using System.Text;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.Infrastructure.Reporting;

public sealed class CsvReportWriter : IReportWriter
{
    private static readonly Encoding CsvEncoding = new UTF8Encoding(true);
    public ReportFormat Format => ReportFormat.Csv;

    public async Task<long> WriteAsync(ReportDocument document, Stream destination, ReportExportRequest request,
        IProgress<ReportProgress>? progress, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, CsvEncoding, 64 * 1024, true);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var enumerator = document.Rows.GetEnumerator();
        var hasRows = enumerator.MoveNext();
        var fields = hasRows ? ReportRowProjection.Fields(enumerator.Current) : [];
        var headers = hasRows ? fields.Select(static field => field.Key) : ReportHeaders.For(request.Profile);
        await writer.WriteLineAsync(string.Join(',', headers.Select(Escape)).AsMemory(), cancellationToken).ConfigureAwait(false);
        if (!hasRows)
        {
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        long count = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            fields = ReportRowProjection.Fields(enumerator.Current);
            await writer.WriteLineAsync(string.Join(',', fields.Select(field => Escape(ReportRowProjection.Format(field.Value)))).AsMemory(), cancellationToken).ConfigureAwait(false);
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

    internal static string Escape(string value) => value.IndexOfAny([',', ';', '"', '\r', '\n']) >= 0
        ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        : value;
}
