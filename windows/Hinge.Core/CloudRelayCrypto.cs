using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hinge.Core;

/// <summary>
/// Cryptographic format shared by the Windows and Dart clients. This class
/// uses only .NET BCL primitives; it does not reuse Hinge's legacy identity
/// publicKey field, which is not a real long-term asymmetric key.
/// </summary>
public static class CloudRelayCrypto
{
    public static string GenerateEncryptionKey()
    {
        return ToBase64Url(RandomNumberGenerator.GetBytes(CloudRelayConstants.RelayKeySize));
    }

    public static string ComputeRelayDeviceId(string relayEncryptionKey, string hingeDeviceId)
    {
        byte[] key = DecodeKey(relayEncryptionKey);
        byte[] context = Encoding.UTF8.GetBytes($"Hinge-Relay-Device-v1|{hingeDeviceId.Trim()}");
        return ToBase64Url(HMACSHA256.HashData(key, context));
    }

    public static byte[] DeriveTransferKey(
        string relayEncryptionKey,
        string transferId,
        string senderRelayDeviceId,
        string receiverRelayDeviceId)
    {
        byte[] key = DecodeKey(relayEncryptionKey);
        byte[] salt = SHA256.HashData(Encoding.UTF8.GetBytes(transferId));
        byte[] info = Encoding.UTF8.GetBytes(
            $"Hinge-Cloud-Relay-v1|{senderRelayDeviceId}|{receiverRelayDeviceId}");
        return HkdfSha256(key, salt, info, 32);
    }

    public static byte[] EncryptPart(
        ReadOnlySpan<byte> plaintext,
        byte[] transferKey,
        string transferId,
        string senderRelayDeviceId,
        string receiverRelayDeviceId,
        int partNumber)
    {
        byte[] nonce = PartNonce(transferId, partNumber);
        byte[] aad = PartAssociatedData(
            transferId,
            senderRelayDeviceId,
            receiverRelayDeviceId,
            partNumber,
            plaintext.Length);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[CloudRelayConstants.GcmTagSize];
        using var aes = new AesGcm(transferKey, CloudRelayConstants.GcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return Combine(ciphertext, tag);
    }

    public static byte[] DecryptPart(
        ReadOnlySpan<byte> encrypted,
        byte[] transferKey,
        string transferId,
        string senderRelayDeviceId,
        string receiverRelayDeviceId,
        int partNumber,
        int plaintextLength)
    {
        if (encrypted.Length != plaintextLength + CloudRelayConstants.GcmTagSize)
        {
            throw new CryptographicException("Cloud Relay encrypted part length is invalid.");
        }

        byte[] nonce = PartNonce(transferId, partNumber);
        byte[] aad = PartAssociatedData(
            transferId,
            senderRelayDeviceId,
            receiverRelayDeviceId,
            partNumber,
            plaintextLength);
        byte[] ciphertext = encrypted[..plaintextLength].ToArray();
        byte[] tag = encrypted[plaintextLength..].ToArray();
        byte[] plaintext = new byte[plaintextLength];
        using var aes = new AesGcm(transferKey, CloudRelayConstants.GcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        return plaintext;
    }

    public static (string Ciphertext, string Nonce, string Tag) EncryptMetadata(
        CloudRelayFileMetadata metadata,
        byte[] transferKey,
        string transferId,
        string senderRelayDeviceId,
        string receiverRelayDeviceId)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(metadata);
        byte[] nonce = MetadataNonce(transferId);
        byte[] aad = MetadataAssociatedData(transferId, senderRelayDeviceId, receiverRelayDeviceId);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[CloudRelayConstants.GcmTagSize];
        using var aes = new AesGcm(transferKey, CloudRelayConstants.GcmTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return (ToBase64Url(ciphertext), ToBase64Url(nonce), ToBase64Url(tag));
    }

    public static CloudRelayFileMetadata DecryptMetadata(
        CloudRelayManifest manifest,
        string relayEncryptionKey)
    {
        byte[] transferKey = DeriveTransferKey(
            relayEncryptionKey,
            manifest.TransferId,
            manifest.SenderRelayDeviceId,
            manifest.ReceiverRelayDeviceId);
        byte[] ciphertext = FromBase64Url(manifest.MetadataCiphertext);
        byte[] nonce = FromBase64Url(manifest.MetadataNonce);
        byte[] tag = FromBase64Url(manifest.MetadataTag);
        if (nonce.Length != CloudRelayConstants.GcmNonceSize || tag.Length != CloudRelayConstants.GcmTagSize)
        {
            throw new CryptographicException("Cloud Relay metadata authentication data is invalid.");
        }

        byte[] aad = MetadataAssociatedData(
            manifest.TransferId,
            manifest.SenderRelayDeviceId,
            manifest.ReceiverRelayDeviceId);
        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(transferKey, CloudRelayConstants.GcmTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        return JsonSerializer.Deserialize<CloudRelayFileMetadata>(plaintext)
            ?? throw new CryptographicException("Cloud Relay metadata is empty.");
    }

    public static byte[] DecodeKey(string relayEncryptionKey)
    {
        byte[] key = FromBase64Url(relayEncryptionKey);
        if (key.Length != CloudRelayConstants.RelayKeySize)
        {
            throw new CryptographicException("Relay encryption key must be exactly 32 bytes.");
        }
        return key;
    }

    public static string ToBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    public static byte[] FromBase64Url(string value)
    {
        string normalized = value.Trim().Replace("-", "+").Replace("_", "/");
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private static byte[] PartNonce(string transferId, int partNumber)
    {
        if (partNumber < 1) throw new ArgumentOutOfRangeException(nameof(partNumber));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(transferId));
        byte[] nonce = new byte[CloudRelayConstants.GcmNonceSize];
        digest.AsSpan(0, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), checked((uint)partNumber));
        return nonce;
    }

    private static byte[] MetadataNonce(string transferId)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes($"Hinge-Cloud-Relay-v1|metadata|{transferId}"));
        return digest[..CloudRelayConstants.GcmNonceSize];
    }

    private static byte[] PartAssociatedData(
        string transferId,
        string senderRelayDeviceId,
        string receiverRelayDeviceId,
        int partNumber,
        int plaintextLength)
    {
        return Encoding.UTF8.GetBytes(
            $"Hinge-Cloud-Relay-v1|{transferId}|{senderRelayDeviceId}|{receiverRelayDeviceId}|{partNumber}|{plaintextLength}");
    }

    private static byte[] MetadataAssociatedData(
        string transferId,
        string senderRelayDeviceId,
        string receiverRelayDeviceId)
    {
        return Encoding.UTF8.GetBytes(
            $"Hinge-Cloud-Relay-v1|metadata|{transferId}|{senderRelayDeviceId}|{receiverRelayDeviceId}");
    }

    private static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length)
    {
        byte[] prk = HMACSHA256.HashData(salt, ikm);
        var output = new byte[length];
        byte[] previous = Array.Empty<byte>();
        int offset = 0;
        byte counter = 1;
        while (offset < length)
        {
            byte[] input = new byte[previous.Length + info.Length + 1];
            Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
            Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
            input[^1] = counter++;
            previous = HMACSHA256.HashData(prk, input);
            int copy = Math.Min(previous.Length, length - offset);
            Buffer.BlockCopy(previous, 0, output, offset, copy);
            offset += copy;
        }
        CryptographicOperations.ZeroMemory(prk);
        CryptographicOperations.ZeroMemory(previous);
        return output;
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }
}
