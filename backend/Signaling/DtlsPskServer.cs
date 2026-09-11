using System.Security.Cryptography;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace ReVerse.Capture.Signaling;


internal sealed class DtlsPskServer : PskTlsServer
{
    private readonly int handshakeTimeoutMillis;

    internal DtlsPskServer(DtlsPskIdentityLookup lookup, int handshakeTimeoutMillis)
        : base(new BcTlsCrypto(), lookup)
    {
        this.handshakeTimeoutMillis = handshakeTimeoutMillis;
    }

    protected override ProtocolVersion[] GetSupportedVersions() => ProtocolVersion.DTLSv12.Only();

    protected override int[] GetSupportedCipherSuites() =>
    [
        CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256,
        CipherSuite.TLS_PSK_WITH_AES_128_CCM_8
    ];

    public override int GetHandshakeTimeoutMillis() => handshakeTimeoutMillis;


    public override int GetMaxHandshakeMessageSize() => 4096;
}

internal sealed class DtlsPskIdentityLookup(Func<byte[], byte[]?> lookupPsk) : TlsPskIdentityManager, IDisposable
{
    private byte[]? keyCopy;
    internal byte[]? Identity { get; private set; }
    internal DtlsHostException? CallbackFailure { get; private set; }

    public byte[] GetHint() => [];

    public byte[] GetPsk(byte[] identity)
    {

        if (identity.Length is < 1 or > 39 || identity.Any(value => value > 0x7f))
            throw new TlsFatalAlert(AlertDescription.unknown_psk_identity);

        if (Identity is not null)
            throw new TlsFatalAlert(AlertDescription.unexpected_message);


        Identity = identity.ToArray();
        byte[]? key;
        try
        {
            key = lookupPsk(identity.ToArray());
        }
        catch (Exception)
        {

            CallbackFailure = new DtlsHostException("DTLS PSK lookup callback failed.");
            throw CallbackFailure;
        }

        if (key is null)
            throw new TlsFatalAlert(AlertDescription.unknown_psk_identity);
        if (key.Length is < 1 or > 65535)
        {
            CallbackFailure = new DtlsHostException("DTLS PSK lookup returned an invalid key length.");
            throw CallbackFailure;
        }


        keyCopy = key.ToArray();
        return keyCopy;
    }

    public void Dispose()
    {
        if (keyCopy is not null)
        {
            CryptographicOperations.ZeroMemory(keyCopy);
            keyCopy = null;
        }
        Identity = null;
    }
}
