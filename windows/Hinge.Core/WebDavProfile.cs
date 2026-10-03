namespace Hinge.Core;

/// <summary>A named WebDAV endpoint. Passwords are protected by the Windows settings store.</summary>
public sealed record WebDavProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Url { get; init; } = "";
    public string Username { get; init; } = "";
    public string ProtectedPassword { get; init; } = "";
    public string UserAgent { get; init; } = "";
    public string InitialPath { get; init; } = "";
    public string ThumbnailMountPath { get; init; } = "";
    public string Remark { get; init; } = "";
    public bool TrustAllCertificates { get; init; }
    public bool ParallelTransfers { get; init; }
    public string DisplayName => !string.IsNullOrWhiteSpace(Remark)
        ? Remark : Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : "WebDAV";
}
