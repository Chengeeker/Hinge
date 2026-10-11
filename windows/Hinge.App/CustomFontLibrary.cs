using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage;

namespace Hinge.App;

internal sealed record CustomFontEntry(string FileName, string FamilyName, string StyleName, string VersionName, string PostScriptName, string ContentHash, string SourceName)
{
    public string DisplayName => $"{FamilyName} · {StyleName}";
    public string FontFamilySource =>
        $"ms-appx:///HingeCustomFonts/{Uri.EscapeDataString(FileName)}#{Uri.EscapeDataString(FamilyName)}";
}

internal sealed record CustomFontImportResult(IReadOnlyList<CustomFontEntry> Imported, int Duplicates, int Skipped);

internal static class CustomFontLibrary
{
    private const int MaxFontBytes = 30 * 1024 * 1024;
    private const int MaxBatchBytes = 120 * 1024 * 1024;
    private const int MaxFontsPerBatch = 64;
    private static readonly string FontDirectory = Path.Combine(AppContext.BaseDirectory, "HingeCustomFonts");

    public static IReadOnlyList<CustomFontEntry> GetFonts()
    {
        if (!Directory.Exists(FontDirectory)) return [];
        return Directory.EnumerateFiles(FontDirectory)
            .Where(path => IsFontExtension(Path.GetExtension(path)))
            .Select(path => TryReadFont(path))
            .Where(font => font != null)
            .Cast<CustomFontEntry>()
            .DistinctBy(font => font.ContentHash, StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(font => font.StyleName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static CustomFontEntry? Find(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName) return null;
        return TryReadFont(Path.Combine(FontDirectory, fileName));
    }

    public static async Task<CustomFontImportResult> ImportAsync(IReadOnlyList<StorageFile> files)
    {
        Directory.CreateDirectory(FontDirectory);
        var imported = new List<CustomFontEntry>();
        var knownHashes = GetFonts().Select(font => font.ContentHash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        var skipped = 0;
        var totalBytes = 0L;

        foreach (var file in files)
        {
            if (string.Equals(Path.GetExtension(file.Name), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (new FileInfo(file.Path).Length > MaxBatchBytes) { skipped++; continue; }
                    using var archive = ZipFile.OpenRead(file.Path);
                    foreach (var entry in archive.Entries)
                    {
                        if (!IsFontExtension(Path.GetExtension(entry.Name))) continue;
                        if (imported.Count >= MaxFontsPerBatch || entry.Length is <= 0 or > MaxFontBytes || totalBytes + entry.Length > MaxBatchBytes)
                        {
                            skipped++;
                            continue;
                        }
                        try
                        {
                            await using var stream = entry.Open();
                            var bytes = await ReadBoundedAsync(stream, entry.Length);
                            var font = await TryStoreFontAsync(bytes, Path.GetExtension(entry.Name), entry.Name, knownHashes);
                            if (font is null) duplicates++;
                            else imported.Add(font);
                            totalBytes += bytes.Length;
                        }
                        catch
                        {
                            skipped++;
                        }
                    }
                }
                catch
                {
                    skipped++;
                }
            }
            else if (IsFontExtension(Path.GetExtension(file.Name)))
            {
                try
                {
                    var length = new FileInfo(file.Path).Length;
                    if (length is <= 0 or > MaxFontBytes || imported.Count >= MaxFontsPerBatch || totalBytes + length > MaxBatchBytes)
                    {
                        skipped++;
                        continue;
                    }
                    var bytes = await File.ReadAllBytesAsync(file.Path);
                    var font = await TryStoreFontAsync(bytes, Path.GetExtension(file.Name), file.Name, knownHashes);
                    if (font is null) duplicates++;
                    else imported.Add(font);
                    totalBytes += bytes.Length;
                }
                catch
                {
                    skipped++;
                }
            }
            else
            {
                skipped++;
            }
        }

        return new CustomFontImportResult(imported, duplicates, skipped);
    }

    public static int Delete(CustomFontEntry font)
    {
        if (Path.GetFileName(font.FileName) != font.FileName || !Directory.Exists(FontDirectory)) return 0;
        var current = Find(font.FileName);
        if (current?.ContentHash != font.ContentHash) return 0;

        int deleted = 0;
        foreach (var path in Directory.EnumerateFiles(FontDirectory).Where(path => IsFontExtension(Path.GetExtension(path))))
        {
            var candidate = TryReadFont(path);
            if (candidate?.ContentHash != font.ContentHash) continue;
            try
            {
                File.Delete(path);
                deleted++;
            }
            catch { }
        }
        return deleted;
    }

    public static Task<int> DeleteAsync(CustomFontEntry font) => Task.Run(() => Delete(font));

    private static async Task<CustomFontEntry?> TryStoreFontAsync(byte[] bytes, string extension, string sourceName, HashSet<string> knownHashes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxFontBytes) throw new InvalidDataException("字体文件大小无效。");
        var names = ReadFontNames(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!knownHashes.Add(hash)) return null;

        var safeSourceName = SanitizeSourceName(sourceName);
        var fileName = $"{hash}_{safeSourceName}{extension.ToLowerInvariant()}";
        var path = Path.Combine(FontDirectory, fileName);
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            return new CustomFontEntry(fileName, names.FamilyName, names.StyleName, names.VersionName, names.PostScriptName, hash, safeSourceName);
        }
        catch
        {
            knownHashes.Remove(hash);
            try { File.Delete(path); } catch { }
            throw;
        }
    }

    private static string SanitizeSourceName(string sourceName)
    {
        var name = Path.GetFileNameWithoutExtension(sourceName);
        var safe = new string(name.Where(character => !char.IsControl(character) && !Path.GetInvalidFileNameChars().Contains(character)).Take(72).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "font" : safe;
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long declaredLength)
    {
        if (declaredLength is <= 0 or > MaxFontBytes) throw new InvalidDataException("字体文件大小无效。");
        using var buffer = new MemoryStream((int)declaredLength);
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) break;
            if (buffer.Length + read > MaxFontBytes) throw new InvalidDataException("字体文件超过大小限制。");
            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }
        return buffer.ToArray();
    }

    private static CustomFontEntry? TryReadFont(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaxFontBytes) return null;
            using var stream = File.OpenRead(path);
            var names = ReadFontNames(stream);
            stream.Position = 0;
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var stem = Path.GetFileNameWithoutExtension(info.Name);
            var sourceName = stem.Length > 65 && stem[64] == '_' ? stem[65..] : names.PostScriptName;
            return new CustomFontEntry(info.Name, names.FamilyName, names.StyleName, names.VersionName, names.PostScriptName, hash, sourceName);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFontExtension(string extension) =>
        extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".otf", StringComparison.OrdinalIgnoreCase);

    private static (string FamilyName, string StyleName, string VersionName, string PostScriptName) ReadFontNames(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return ReadFontNames(stream);
    }

    private static (string FamilyName, string StyleName, string VersionName, string PostScriptName) ReadFontNames(Stream stream)
    {
        if (stream.Length < 12) throw new InvalidDataException("不是有效的 OpenType 字体。");
        byte[] header = ReadAt(stream, 0, 12);
        uint signature = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (signature is not (0x00010000 or 0x4F54544F)) throw new InvalidDataException("仅支持 TTF 和 OTF 字体。");

        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        int nameOffset = -1, nameLength = 0;
        for (int index = 0; index < tableCount; index++)
        {
            byte[] record = ReadAt(stream, 12L + index * 16, 16);
            if (!record.AsSpan(0, 4).SequenceEqual("name"u8)) continue;
            uint tableOffset = BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(8));
            uint tableLength = BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(12));
            if (tableOffset > stream.Length || tableLength > stream.Length - tableOffset || tableLength > 4 * 1024 * 1024)
                throw new InvalidDataException("字体名称表已损坏。");
            nameOffset = (int)tableOffset;
            nameLength = (int)tableLength;
            break;
        }
        if (nameOffset < 0 || nameLength < 6) throw new InvalidDataException("字体缺少名称表。");

        var nameTable = ReadAt(stream, nameOffset, nameLength).AsSpan();
        int recordCount = BinaryPrimitives.ReadUInt16BigEndian(nameTable[2..]);
        int stringsOffset = BinaryPrimitives.ReadUInt16BigEndian(nameTable[4..]);
        if (6 + recordCount * 12 > nameTable.Length || stringsOffset > nameTable.Length) throw new InvalidDataException("字体名称表已损坏。");

        var candidates = new Dictionary<int, List<(int Rank, string Name)>>();
        for (int index = 0, offset = 6; index < recordCount; index++, offset += 12)
        {
            var record = nameTable[offset..];
            int platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            int language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            int stringOffset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            if (nameId is not (1 or 2 or 5 or 6 or 16 or 17) || stringsOffset + stringOffset + length > nameTable.Length) continue;
            string name;
            try
            {
                var bytesForName = nameTable.Slice(stringsOffset + stringOffset, length);
                name = platform is 0 or 3
                    ? Encoding.BigEndianUnicode.GetString(bytesForName)
                    : Encoding.Latin1.GetString(bytesForName);
                name = name.Trim('\0', ' ', '\t', '\r', '\n');
            }
            catch { continue; }
            if (name.Length is 0 or > 128 || name.Any(char.IsControl)) continue;
            int preferredNameRank = nameId is 16 or 17 ? 0 : nameId is 1 or 2 ? 100 : 0;
            int languageRank = language == 0x0409 ? 0 : language is 0 or 0x0804 ? 1 : 10;
            int rank = preferredNameRank + languageRank + (platform is 0 or 3 ? 0 : 1);
            if (!candidates.TryGetValue(nameId, out var values)) candidates[nameId] = values = [];
            values.Add((rank, name));
        }
        string SelectName(params int[] ids) => ids
            .Where(candidates.ContainsKey)
            .SelectMany(id => candidates[id].OrderBy(candidate => candidate.Rank))
            .Select(candidate => candidate.Name)
            .FirstOrDefault() ?? string.Empty;

        var family = SelectName(16, 1);
        if (family.Length is 0 or > 128 || family.Any(char.IsControl)) throw new InvalidDataException("无法读取字体系列名称。");
        var style = SelectName(17, 2);
        if (style.Length is 0 or > 128 || style.Any(char.IsControl)) style = "Regular";
        return (family, style, SelectName(5), SelectName(6));
    }

    private static byte[] ReadAt(Stream stream, long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > stream.Length) throw new InvalidDataException("字体文件结构超出范围。");
        stream.Position = offset;
        var bytes = new byte[length];
        int read = 0;
        while (read < bytes.Length)
        {
            int current = stream.Read(bytes, read, bytes.Length - read);
            if (current == 0) throw new EndOfStreamException();
            read += current;
        }
        return bytes;
    }
}
