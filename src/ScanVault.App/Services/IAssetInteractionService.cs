namespace ScanVault.App.Services;

public interface IAssetInteractionService
{
    void CopyText(string text);
    void OpenFolder(string folderPath);
    void OpenFile(string filePath);
}
