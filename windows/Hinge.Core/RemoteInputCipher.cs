using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Hinge.Core;

// Each TCP connection gets a fresh server challenge and independent directional keys.
public sealed class RemoteInputCipher : IDisposable
{
    private readonly byte[] _challenge;
    private readonly byte[] _sendKey;
    private readonly byte[] _receiveKey;
    private ulong _sent, _received;

    public RemoteInputCipher(byte[] secret, byte[] challenge, bool server)
    {
        if (secret.Length != 32 || challenge.Length != 32) throw new ArgumentException("Invalid control key/challenge.");
        _challenge = challenge.ToArray();
        byte[] Derive(string direction) => HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, challenge,
            Encoding.UTF8.GetBytes("HingeRemoteInput/v1/" + direction));
        _sendKey = Derive(server ? "server" : "client");
        _receiveKey = Derive(server ? "client" : "server");
    }

    public byte[] Encrypt(byte[] plain)
    {
        var output = new byte[9 + plain.Length + 16];
        output[0] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(output.AsSpan(1), checked(++_sent));
        var nonce = new byte[12];
        output.AsSpan(1, 8).CopyTo(nonce.AsSpan(4));
        using var aes = new AesGcm(_sendKey, 16);
        aes.Encrypt(nonce, plain, output.AsSpan(9, plain.Length), output.AsSpan(9 + plain.Length), Aad(output));
        return output;
    }

    public bool TryDecrypt(byte[] envelope, out byte[] plain)
    {
        plain = Array.Empty<byte>();
        if (envelope.Length < 41 || envelope.Length > 16_425 || envelope[0] != 1) return false;
        ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(envelope.AsSpan(1));
        if (sequence <= _received) return false;
        var nonce = new byte[12];
        envelope.AsSpan(1, 8).CopyTo(nonce.AsSpan(4));
        var result = new byte[envelope.Length - 25];
        try
        {
            using var aes = new AesGcm(_receiveKey, 16);
            aes.Decrypt(nonce, envelope.AsSpan(9, result.Length), envelope.AsSpan(envelope.Length - 16), result, Aad(envelope));
            _received = sequence;
            plain = result;
            return true;
        }
        catch (CryptographicException) { return false; }
    }

    private byte[] Aad(byte[] envelope) => envelope.AsSpan(0, 9).ToArray().Concat(_challenge).ToArray();

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_sendKey);
        CryptographicOperations.ZeroMemory(_receiveKey);
    }
}
