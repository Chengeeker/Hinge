namespace Hinge.App;

internal static class HingeDragMetadata
{
    public const string InternalRemoteFile = "Hinge.InternalRemoteFileDrag";
    public const string WebDavRemoteFile = "Hinge.WebDavRemoteFileDrag";

    public static bool IsInternalRemoteFileDrag(Windows.ApplicationModel.DataTransfer.DataPackageView dataView)
    {
        return dataView.Properties.TryGetValue(InternalRemoteFile, out var value) &&
            value is bool isInternal &&
            isInternal;
    }

    public static bool IsWebDavRemoteFileDrag(Windows.ApplicationModel.DataTransfer.DataPackageView dataView) =>
        dataView.Properties.TryGetValue(WebDavRemoteFile, out var value) && value is true;
}

public sealed class ComputerFilesDroppedEventArgs : EventArgs
{
    public ComputerFilesDroppedEventArgs(
        IReadOnlyList<string> filePaths,
        string destinationPath)
    {
        FilePaths = filePaths;
        DestinationPath = destinationPath;
    }

    public IReadOnlyList<string> FilePaths { get; }

    /// <summary>
    /// Relative path under the Android shared-storage root, for example
    /// Download or Pictures/WeiXin.
    /// </summary>
    public string DestinationPath { get; }
}
