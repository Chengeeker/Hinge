using System.Text;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class CloudRelayTests
{
    [Fact]
    public void CloudRelayCrypto_DerivesStableRelayAndTransferIdentifiers()
    {
        string key = CloudRelayCrypto.GenerateEncryptionKey();
        string first = CloudRelayCrypto.ComputeRelayDeviceId(key, "hinge-device-a");
        string second = CloudRelayCrypto.ComputeRelayDeviceId(key, "hinge-device-a");
        string otherDevice = CloudRelayCrypto.ComputeRelayDeviceId(key, "hinge-device-b");

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherDevice);
        Assert.Equal(43, first.Length);

        byte[] transferKey = CloudRelayCrypto.DeriveTransferKey(
            key,
            "123e4567-e89b-42d3-a456-426614174000",
            first,
            otherDevice);
        Assert.Equal(32, transferKey.Length);
    }

    [Fact]
    public void CloudRelayCrypto_EncryptsPartsAndMetadataWithAuthenticatedData()
    {
        string key = CloudRelayCrypto.GenerateEncryptionKey();
        const string transferId = "123e4567-e89b-42d3-a456-426614174000";
        string sender = CloudRelayCrypto.ComputeRelayDeviceId(key, "hinge-device-a");
        string receiver = CloudRelayCrypto.ComputeRelayDeviceId(key, "hinge-device-b");
        byte[] transferKey = CloudRelayCrypto.DeriveTransferKey(key, transferId, sender, receiver);
        byte[] plaintext = Encoding.UTF8.GetBytes("cloud relay test payload");

        byte[] encrypted = CloudRelayCrypto.EncryptPart(
            plaintext,
            transferKey,
            transferId,
            sender,
            receiver,
            partNumber: 1);
        byte[] decrypted = CloudRelayCrypto.DecryptPart(
            encrypted,
            transferKey,
            transferId,
            sender,
            receiver,
            partNumber: 1,
            plaintext.Length);
        Assert.Equal(plaintext, decrypted);

        var metadata = new CloudRelayFileMetadata
        {
            FileName = "example.txt",
            MimeType = "text/plain",
            FileSize = plaintext.Length,
            ModifiedTime = 1,
            Sha256 = "a".PadLeft(64, 'a')
        };
        var encryptedMetadata = CloudRelayCrypto.EncryptMetadata(
            metadata,
            transferKey,
            transferId,
            sender,
            receiver);
        var manifest = new CloudRelayManifest
        {
            TransferId = transferId,
            SenderRelayDeviceId = sender,
            ReceiverRelayDeviceId = receiver,
            MetadataCiphertext = encryptedMetadata.Ciphertext,
            MetadataNonce = encryptedMetadata.Nonce,
            MetadataTag = encryptedMetadata.Tag
        };

        var restored = CloudRelayCrypto.DecryptMetadata(manifest, key);
        Assert.Equal(metadata.FileName, restored.FileName);
        Assert.Equal(metadata.FileSize, restored.FileSize);
        Assert.Equal(metadata.Sha256, restored.Sha256);
    }
}
