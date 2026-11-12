using System;
using System.Globalization;
using System.Security.Cryptography;

namespace DarumaPPAP.PlateauWorldFlow
{
    internal sealed class Digests
    {
        internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        internal static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static string Timestamp(DateTimeOffset value)
        {
            return value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }
    }
}
