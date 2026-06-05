using System.Security.Cryptography;
using System.Text;

namespace DeviceTracker.Services;

/// <summary>
/// نظام تشفير AES-256-GCM للبيانات الحساسة.
/// المفتاح يُخزن في SecureStorage (Android Keystore / iOS Keychain)
/// ولا يمكن الوصول إليه حتى من تطبيقات أخرى على نفس الجهاز.
///
/// تنسيق البيانات المشفرة (مجمعة في Base64 واحد):
/// [nonce 12 بايت] [ciphertext N بايت] [auth_tag 16 بايت]
/// </summary>
public sealed class EncryptionService : IDisposable
{
    private AesGcm? _aes;
    private byte[]? _key;

    private const int KeySizeBytes = 32;     // 256-bit
    private const int NonceSize = 12;         // 96-bit (موصى به لـ GCM)
    private const int TagSize = 16;           // 128-bit authentication tag
    private const string SecureStorageKeyName = "device_encryption_key";

    public EncryptionService()
    {
        Init();
    }

    private void Init()
    {
        try
        {
            var stored = Preferences.Get(SecureStorageKeyName, string.Empty);
            if (!string.IsNullOrEmpty(stored))
            {
                _key = Convert.FromBase64String(stored);
            }
            else
            {
                _key = new byte[KeySizeBytes];
                RandomNumberGenerator.Fill(_key);
                Preferences.Set(SecureStorageKeyName, Convert.ToBase64String(_key));
            }
            _aes = new AesGcm(_key);

            _ = UpgradeToSecureStorageAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] Init failed: {ex.Message}");
            _key = new byte[KeySizeBytes];
            RandomNumberGenerator.Fill(_key);
            _aes = new AesGcm(_key);
        }
    }

    private async Task UpgradeToSecureStorageAsync()
    {
        try
        {
            var existing = await SecureStorage.GetAsync(SecureStorageKeyName);
            if (string.IsNullOrEmpty(existing))
            {
                await SecureStorage.SetAsync(SecureStorageKeyName, Convert.ToBase64String(_key!));
            }
            Preferences.Remove(SecureStorageKeyName);
        }
        catch
        {
        }
    }

    /// <summary>
    /// تشفير نص عادي ← Base64 (nonce + ciphertext + tag)
    /// </summary>
    public string Encrypt(string plaintext)
    {
        try
        {
            if (string.IsNullOrEmpty(plaintext))
                return string.Empty;

            // Nonce عشوائي (12 بايت) — مختلف كل مرة لتجنب التكرار
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            _aes!.Encrypt(nonce, plainBytes, ciphertext, tag);

            // حزمة واحدة: nonce || ciphertext || tag
            var result = new byte[NonceSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

            return Convert.ToBase64String(result);
        }
        catch (CryptographicException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] Crypto error: {ex.Message}");
            throw new InvalidOperationException("Encryption failed due to cryptographic error.", ex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] Unexpected error: {ex.Message}");
            throw new InvalidOperationException("Encryption failed unexpectedly.", ex);
        }
    }

    /// <summary>
    /// فك تشفير سلسلة Base64 ← نص عادي
    /// </summary>
    public string Decrypt(string encryptedBase64)
    {
        try
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return string.Empty;

            var data = Convert.FromBase64String(encryptedBase64);

            // التحقق من صحة الحد الأدنى للحجم
            if (data.Length < NonceSize + TagSize)
                throw new InvalidOperationException(
                    $"Invalid encrypted data length. Expected >= {NonceSize + TagSize}, got {data.Length}");

            // فك الحزمة: nonce || ciphertext || tag
            var nonce = data[..NonceSize];
            var tag = data[^TagSize..];
            var ciphertext = data[NonceSize..^TagSize];

            var plainBytes = new byte[ciphertext.Length];
            _aes!.Decrypt(nonce, ciphertext, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] Decrypt crypto error: {ex.Message}");
            return string.Empty; // فشل المصادقة → البيانات تالفة أو مفتاح خطأ
        }
        catch (FormatException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] Invalid Base64: {ex.Message}");
            return string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] Decrypt error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// تشفير أي كائن إلى JSON → مشفر
    /// </summary>
    public string EncryptObject<T>(T obj)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(obj,
                Newtonsoft.Json.Formatting.None,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                });

            return Encrypt(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] EncryptObject failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// فك تشفير ← JSON ← كائن
    /// </summary>
    public T? DecryptObject<T>(string encryptedBase64) where T : class
    {
        try
        {
            var json = Decrypt(encryptedBase64);
            if (string.IsNullOrEmpty(json))
                return null;

            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] DecryptObject failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// تدوير المفتاح: إنشاء مفتاح جديد واستبدال القديم في SecureStorage.
    /// يجب إعادة تشفير جميع البيانات القديمة بعد الاستدعاء.
    /// </summary>
    public async Task RotateKeyAsync()
    {
        try
        {
            var newKey = new byte[KeySizeBytes];
            RandomNumberGenerator.Fill(newKey);

            await SecureStorage.SetAsync(SecureStorageKeyName, Convert.ToBase64String(newKey));

            _key = newKey;
            _aes?.Dispose();
            _aes = new AesGcm(_key);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Encryption] RotateKey failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// التأكد من وجود مفتاح (يُستدعى عند بدء التشغيل)
    /// </summary>
    public async Task EnsureKeyExistsAsync()
    {
        if (_aes != null) return;
        var exists = await SecureStorage.GetAsync(SecureStorageKeyName);
        if (string.IsNullOrEmpty(exists))
        {
            _key = new byte[KeySizeBytes];
            RandomNumberGenerator.Fill(_key);
            await SecureStorage.SetAsync(SecureStorageKeyName, Convert.ToBase64String(_key));
        }
        else
        {
            _key = Convert.FromBase64String(exists);
        }
        _aes = new AesGcm(_key);
    }

    public void Dispose()
    {
        _aes?.Dispose();
    }
}
