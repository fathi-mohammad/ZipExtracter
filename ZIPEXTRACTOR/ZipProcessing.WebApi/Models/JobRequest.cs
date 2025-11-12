using System.Security.Cryptography;
using System.Text;

namespace ZipProcessing.WebApi.Models
{
    public class JobRequest
    {
        public string RequestId { get; set; } = default!;
        public string ZipPath { get; set; } = default!;
    }

    public class ApiRequest
    {
        public string RequestId { get; set; } = default!;
        public string FileName { get; set; } = default!;
    }
    public static class RandomHelpers
    {
        private const string AlphanumericChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        
        public static int GenerateFiveDigitNumber(bool allowLeadingZeros = false)
        {
            return allowLeadingZeros
                ? RandomNumberGenerator.GetInt32(0, 100_000)
                : RandomNumberGenerator.GetInt32(10_000, 100_000);
        }

        // Always returns exactly 5 characters (pads with leading zeros if needed).
        public static string GenerateFiveDigitString(bool allowLeadingZeros = false)
        {
            var value = GenerateFiveDigitNumber(allowLeadingZeros);
            return value.ToString("D5");
        }

        // Generate an alphanumeric string of given length.
        // useCrypto = true uses System.Security.Cryptography for stronger randomness.
        public static string GenerateAlphanumeric(int length = 5, bool useCrypto = true)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            var sb = new StringBuilder(length);

            if (useCrypto)
            {
                for (int i = 0; i < length; i++)
                {
                    int idx = RandomNumberGenerator.GetInt32(AlphanumericChars.Length);
                    sb.Append(AlphanumericChars[idx]);
                }
            }
            else
            {
                var rnd = Random.Shared; 
                for (int i = 0; i < length; i++)
                    sb.Append(AlphanumericChars[rnd.Next(AlphanumericChars.Length)]);
            }

            return sb.ToString();
        }
    }
}
