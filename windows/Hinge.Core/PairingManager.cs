using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Hinge.Core;

public class PairRequestMessage
{
    [JsonPropertyName("initiatorDeviceId")]
    public string InitiatorDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("initiatorName")]
    public string InitiatorName { get; set; } = string.Empty;

    [JsonPropertyName("receiverDeviceId")]
    public string ReceiverDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;

    [JsonPropertyName("initiatorNonce")]
    public string InitiatorNonce { get; set; } = string.Empty;

    [JsonPropertyName("initiatorPublicKey")]
    public string InitiatorPublicKey { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public class PairConfirmMessage
{
    [JsonPropertyName("initiatorDeviceId")]
    public string InitiatorDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("receiverDeviceId")]
    public string ReceiverDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("receiverName")]
    public string ReceiverName { get; set; } = string.Empty;

    [JsonPropertyName("receiverNonce")]
    public string ReceiverNonce { get; set; } = string.Empty;

    [JsonPropertyName("receiverPublicKey")]
    public string ReceiverPublicKey { get; set; } = string.Empty;

    [JsonPropertyName("sasCode")]
    public string SasCode { get; set; } = string.Empty;

    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public class PairingManager
{
    private readonly DeviceIdentity _localIdentity;
    private readonly TrustStore _trustStore;

    public PairingManager(DeviceIdentity localIdentity, TrustStore trustStore)
    {
        _localIdentity = localIdentity;
        _trustStore = trustStore;
    }

    /// <summary>
    /// Backward-compatible legacy deterministic PIN derivation (prototype).
    /// </summary>
    public static int DerivePin(string initiatorId, string receiverId, string salt)
    {
        string input = $"{initiatorId}:{receiverId}:{salt}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        uint val = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(0, 4));
        return (int)((val % 900000) + 100000);
    }

    /// <summary>
    /// Deterministic 6-digit Short Authentication String (SAS) derivation:
    /// HMAC_SHA256(Secret, "Hinge-SAS-v1" || NonceA || NonceB || PubKeyA || PubKeyB)
    /// </summary>
    public static string DeriveSasCode(byte[] sharedSecret, byte[] nonceA, byte[] nonceB, string pubKeyA = "", string pubKeyB = "")
    {
        byte[] prefix = Encoding.UTF8.GetBytes("Hinge-SAS-v1");
        byte[] pubKeyABytes = Encoding.UTF8.GetBytes(pubKeyA);
        byte[] pubKeyBBytes = Encoding.UTF8.GetBytes(pubKeyB);

        int totalLen = prefix.Length + nonceA.Length + nonceB.Length + pubKeyABytes.Length + pubKeyBBytes.Length;
        byte[] context = new byte[totalLen];
        int offset = 0;
        Buffer.BlockCopy(prefix, 0, context, offset, prefix.Length);
        offset += prefix.Length;
        Buffer.BlockCopy(nonceA, 0, context, offset, nonceA.Length);
        offset += nonceA.Length;
        Buffer.BlockCopy(nonceB, 0, context, offset, nonceB.Length);
        offset += nonceB.Length;
        Buffer.BlockCopy(pubKeyABytes, 0, context, offset, pubKeyABytes.Length);
        offset += pubKeyABytes.Length;
        Buffer.BlockCopy(pubKeyBBytes, 0, context, offset, pubKeyBBytes.Length);

        using var hmac = new HMACSHA256(sharedSecret);
        byte[] digest = hmac.ComputeHash(context);

        uint truncatedInt = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(0, 4));
        uint code = (truncatedInt % 900000) + 100000;
        return code.ToString("D6");
    }

    public static string DeriveSasCode(string sharedSecretHexOrStr, string nonceAHex, string nonceBHex, string pubKeyA = "", string pubKeyB = "")
    {
        byte[] secret = Encoding.UTF8.GetBytes(sharedSecretHexOrStr);
        byte[] nA = Convert.FromHexString(nonceAHex);
        byte[] nB = Convert.FromHexString(nonceBHex);
        return DeriveSasCode(secret, nA, nB, pubKeyA, pubKeyB);
    }

    public PairRequestMessage CreatePairRequest(string receiverDeviceId, string? publicKey = null)
    {
        byte[] nonceBytes = new byte[16];
        RandomNumberGenerator.Fill(nonceBytes);
        string nonceHex = Convert.ToHexString(nonceBytes).ToLowerInvariant();

        return new PairRequestMessage
        {
            InitiatorDeviceId = _localIdentity.DeviceId,
            InitiatorName = _localIdentity.Name,
            ReceiverDeviceId = receiverDeviceId,
            Salt = nonceHex, // For backwards compatibility
            InitiatorNonce = nonceHex,
            InitiatorPublicKey = publicKey ?? _localIdentity.PublicKey,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    public PairConfirmMessage CreatePairConfirm(PairRequestMessage request, bool accepted, byte[]? sharedSecret = null)
    {
        byte[] receiverNonceBytes = new byte[16];
        RandomNumberGenerator.Fill(receiverNonceBytes);
        string receiverNonceHex = Convert.ToHexString(receiverNonceBytes).ToLowerInvariant();

        string sasCode = string.Empty;
        if (accepted)
        {
            byte[] secret = sharedSecret ?? SHA256.HashData(Encoding.UTF8.GetBytes($"{request.InitiatorDeviceId}:{_localIdentity.DeviceId}"));
            byte[] nA = Convert.FromHexString(string.IsNullOrEmpty(request.InitiatorNonce) ? request.Salt.PadRight(32, '0')[..32] : request.InitiatorNonce);
            sasCode = DeriveSasCode(secret, nA, receiverNonceBytes, request.InitiatorPublicKey, _localIdentity.PublicKey);
        }

        return new PairConfirmMessage
        {
            InitiatorDeviceId = request.InitiatorDeviceId,
            ReceiverDeviceId = _localIdentity.DeviceId,
            ReceiverName = _localIdentity.Name,
            ReceiverNonce = receiverNonceHex,
            ReceiverPublicKey = _localIdentity.PublicKey,
            SasCode = sasCode,
            Accepted = accepted,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    public void SaveTrustedPeer(string peerDeviceId, string peerName, string publicKey = "")
    {
        _trustStore.AddOrUpdate(new TrustedDevice
        {
            DeviceId = peerDeviceId,
            Name = peerName,
            PublicKey = publicKey,
            PairedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TrustState = TrustState.Trusted
        });
    }

    public bool IsPeerTrusted(string peerDeviceId)
    {
        return _trustStore.IsTrusted(peerDeviceId);
    }
}
