namespace ScanVault.Infrastructure.Reporting;

internal static class ReportFilePublisher
{
    public static void Publish(
        string temporaryPath,
        string destination,
        string? temporaryMetadataPath,
        string? metadataPath)
    {
        string? metadataBackupPath = null;
        try
        {
            if (temporaryMetadataPath is not null && metadataPath is not null)
            {
                if (File.Exists(metadataPath))
                {
                    metadataBackupPath = metadataPath + $".{Guid.NewGuid():N}.backup";
                    File.Move(metadataPath, metadataBackupPath);
                }

                File.Move(temporaryMetadataPath, metadataPath);
            }

            try
            {
                File.Move(temporaryPath, destination, true);
            }
            catch
            {
                if (metadataPath is not null)
                {
                    TryDelete(metadataPath);
                    if (metadataBackupPath is not null && File.Exists(metadataBackupPath))
                    {
                        File.Move(metadataBackupPath, metadataPath);
                        metadataBackupPath = null;
                    }
                }

                throw;
            }
        }
        finally
        {
            if (metadataBackupPath is not null)
            {
                TryDelete(metadataBackupPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Publishing succeeded; an obsolete backup may be removed manually.
        }
        catch (UnauthorizedAccessException)
        {
            // Publishing succeeded; an obsolete backup may be removed manually.
        }
    }
}
