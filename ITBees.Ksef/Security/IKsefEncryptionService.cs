namespace ITBees.Ksef.Security;

public interface IKsefEncryptionService
{
    // KSeF 2.0: szyfrowanie danych faktury przed wysyłką (CMS/PKCS#7 z kluczem publicznym KSeF)
    Task<byte[]> EncryptAsync(byte[] xmlBytes, CancellationToken ct);
}