using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Hinge.Setup;

internal static class InstallerFileChecks
{
    public static bool IsIdenticalShellExtension(ZipArchiveEntry entry, string path)
    {
        if (!entry.Name.StartsWith("Hinge.ShellExtension.v", StringComparison.OrdinalIgnoreCase) ||
            !entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
        try
        {
            if (entry.Length <= 0 || entry.Length > 16 * 1024 * 1024) return false;
            using var installed = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (installed.Length <= 0 || installed.Length > 16 * 1024 * 1024) return false;
            using var packaged = new MemoryStream((int)entry.Length);
            using (var entryStream = entry.Open()) entryStream.CopyTo(packaged);
            if (packaged.Length != entry.Length) return false;
            return GetUnsignedImageHash(installed).AsSpan().SequenceEqual(GetUnsignedImageHash(packaged));
        }
        catch (BadImageFormatException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static byte[] GetUnsignedImageHash(Stream image)
    {
        image.Position = 0;
        using var reader = new PEReader(image, PEStreamOptions.LeaveOpen);
        var signature = reader.PEHeaders.PEHeader?.CertificateTableDirectory
            ?? throw new InvalidDataException("Shell 扩展不是有效的 PE 文件。");
        var signatureStart = (long)signature.RelativeVirtualAddress;
        var signatureEnd = signatureStart + signature.Size;
        if (signatureStart <= 0 || signature.Size <= 0 || signatureEnd > image.Length)
        {
            throw new InvalidDataException("Shell 扩展缺少有效的 Authenticode 签名区域。");
        }

        var dosHeader = new byte[64];
        image.Position = 0;
        if (image.Read(dosHeader, 0, dosHeader.Length) != dosHeader.Length ||
            dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
        {
            throw new InvalidDataException("Shell 扩展缺少有效的 DOS 头。");
        }
        var peHeaderOffset = BitConverter.ToInt32(dosHeader, 0x3C);
        var checksumOffset = (long)peHeaderOffset + 24 + 64;
        if (peHeaderOffset < dosHeader.Length || checksumOffset + sizeof(uint) > signatureStart)
        {
            throw new InvalidDataException("Shell 扩展的 PE 校验和位置无效。");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendRange(hash, image, 0, checksumOffset);
        AppendRange(hash, image, checksumOffset + sizeof(uint), signatureStart - checksumOffset - sizeof(uint));
        AppendRange(hash, image, signatureEnd, image.Length - signatureEnd);
        return hash.GetHashAndReset();
    }

    private static void AppendRange(IncrementalHash hash, Stream stream, long offset, long length)
    {
        stream.Position = offset;
        var buffer = new byte[81920];
        while (length > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0) throw new EndOfStreamException();
            hash.AppendData(buffer, 0, read);
            length -= read;
        }
    }
}
