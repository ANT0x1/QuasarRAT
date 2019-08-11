using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Quasar.Common.Cryptography
{
    public class Aes256
    {
        private const int KEY_LENGTH = 32;
        private const int AUTH_KEY_LENGTH = 64;
        private const int IV_LENGTH = 16;
        private const int HMAC_SHA256_LENGTH = 32;
        private readonly byte[] _key;
        private readonly byte[] _authKey;

        private static readonly byte[] Salt =
        {
            0xBF, 0xEB, 0x1E, 0x56, 0xFB, 0xCD, 0x97, 0x3B, 0xB2, 0x19, 0x2, 0x24, 0x30, 0xA5, 0x78, 0x43, 0x0, 0x3D, 0x56,
            0x44, 0xD2, 0x1E, 0x62, 0xB9, 0xD4, 0xF1, 0x80, 0xE7, 0xE6, 0xC3, 0x39, 0x41
        };

        public Aes256(string masterKey)
        {
            if (string.IsNullOrEmpty(masterKey))
                throw new ArgumentException($"{nameof(masterKey)} can not be null or empty.");

            using (Rfc2898DeriveBytes derive = new Rfc2898DeriveBytes(masterKey, Salt, 50000))
            {
                _key = derive.GetBytes(KEY_LENGTH);
                _authKey = derive.GetBytes(AUTH_KEY_LENGTH);
            }
        }

        public string Encrypt(string input)
        {
            return Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(input)));
        }

        /* FORMAT
         * ----------------------------------------
         * |     HMAC     |   IV   |  CIPHERTEXT  |
         * ----------------------------------------
         *     32 bytes    16 bytes
         */
        public byte[] Encrypt(byte[] input)
        {
            if (input == null)
                throw new ArgumentNullException($"{nameof(input)} can not be null.");

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Position = HMAC_SHA256_LENGTH; // reserve first 32 bytes for HMAC
                using (AesCryptoServiceProvider aesProvider = new AesCryptoServiceProvider())
                {
                    aesProvider.KeySize = 256;
                    aesProvider.BlockSize = 128;
                    aesProvider.Mode = CipherMode.CBC;
                    aesProvider.Padding = PaddingMode.PKCS7;
                    aesProvider.Key = _key;
                    aesProvider.GenerateIV();

                    using (CryptoStream cs = new CryptoStream(ms, aesProvider.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        ms.Write(aesProvider.IV, 0, aesProvider.IV.Length); // write next 16 bytes the IV, followed by ciphertext
                        cs.Write(input, 0, input.Length);
                        cs.FlushFinalBlock();

                        using (HMACSHA256 hmac = new HMACSHA256(_authKey))
                        {
                            byte[] hash = hmac.ComputeHash(ms.ToArray(), HMAC_SHA256_LENGTH, ms.ToArray().Length - HMAC_SHA256_LENGTH); // compute the HMAC of IV and ciphertext
                            ms.Position = 0; // write hash at beginning
                            ms.Write(hash, 0, hash.Length);
                        }
                    }
                }

                return ms.ToArray();
            }
        }

        public string Decrypt(string input)
        {
            return Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(input)));
        }

        public byte[] Decrypt(byte[] input)
        {
            if (input == null)
                throw new ArgumentNullException($"{nameof(input)} can not be null.");

            using (MemoryStream ms = new MemoryStream(input))
            {
                using (AesCryptoServiceProvider aesProvider = new AesCryptoServiceProvider())
                {
                    aesProvider.KeySize = 256;
                    aesProvider.BlockSize = 128;
                    aesProvider.Mode = CipherMode.CBC;
                    aesProvider.Padding = PaddingMode.PKCS7;
                    aesProvider.Key = _key;

                    // read first 32 bytes for HMAC
                    using (HMACSHA256 hmac = new HMACSHA256(_authKey))
                    {
                        var hash = hmac.ComputeHash(ms.ToArray(), HMAC_SHA256_LENGTH, ms.ToArray().Length - HMAC_SHA256_LENGTH);
                        byte[] receivedHash = new byte[HMAC_SHA256_LENGTH];
                        ms.Read(receivedHash, 0, receivedHash.Length);

                        if (!AreEqual(hash, receivedHash))
                            throw new CryptographicException("Invalid message authentication code (MAC).");
                    }

                    byte[] iv = new byte[IV_LENGTH];
                    ms.Read(iv, 0, IV_LENGTH); // read next 16 bytes for IV, followed by ciphertext
                    aesProvider.IV = iv;

                    using (CryptoStream cs = new CryptoStream(ms, aesProvider.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        byte[] temp = new byte[ms.Length - IV_LENGTH + 1];
                        byte[] data = new byte[cs.Read(temp, 0, temp.Length)];
                        Buffer.BlockCopy(temp, 0, data, 0, data.Length);
                        return data;
                    }
                }
            }
        }

        /// <summary>
        /// Compares two byte arrays for equality.
        /// </summary>
        /// <param name="a1">Byte array to compare</param>
        /// <param name="a2">Byte array to compare</param>
        /// <returns>True if equal, else false</returns>
        /// <remarks>
        /// Assumes that the byte arrays have the same length.
        /// This method is safe against timing attacks.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static bool AreEqual(byte[] a1, byte[] a2)
        {
            bool result = true;
            for (int i = 0; i < a1.Length; ++i)
            {
                if (a1[i] != a2[i])
                    result = false;
            }
            return result;
        }
    }
}
