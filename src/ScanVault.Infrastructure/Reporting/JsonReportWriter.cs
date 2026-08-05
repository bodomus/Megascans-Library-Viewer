using System.Text.Json;
using System.Text.Json.Serialization;
using ScanVault.Core.Abstractions;
using ScanVault.Core.Models;

namespace ScanVault.Infrastructure.Reporting;

public sealed class JsonReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.Json;

    public async Task<long> WriteAsync(ReportDocument document, Stream destination, ReportExportRequest request,
        IProgress<ReportProgress>? progress, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = request.PrettyJson,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        using var writer = new Utf8JsonWriter(destination, new() { Indented = request.PrettyJson });
        writer.WriteStartObject();
        writer.WriteNumber("reportSchemaVersion", ReportContract.SchemaVersion);
        writer.WritePropertyName("metadata");
        JsonSerializer.Serialize(writer, document.Metadata, options);
        writer.WritePropertyName("rows");
        writer.WriteStartArray();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long count = 0;
        foreach (var row in document.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer, row, row.GetType(), options);
            count++;
            if (count % 128 == 0)
            {
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                progress?.Report(new(ReportExportPhase.WritingReport, (int)Math.Min(count, int.MaxValue), count, stopwatch.Elapsed));
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new(ReportExportPhase.WritingReport, document.Metadata.AssetCount, count, stopwatch.Elapsed));
        return count;
    }
}
