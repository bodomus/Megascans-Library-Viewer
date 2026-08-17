using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ScanVault.App.Services;

public sealed class DesktopAssetInteractionService : IAssetInteractionService
{
    public void CopyText(string text) => Clipboard.SetText(text);

    public void OpenFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Asset folder does not exist: {folderPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(folderPath);
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Explorer could not be started.");
    }

    public void OpenFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Asset metadata file does not exist: {filePath}", filePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = $"/select,\"{filePath}\""
        };
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Explorer could not be started.");
    }
}
