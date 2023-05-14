using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace po.Extensions
{
    public static class StringExtensions
    {
        private static readonly JsonSerializerOptions options;
        static StringExtensions()
        {
            options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
        }

        public static string ComputeSHA256(this string value)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));

            // BitConverter averages 50% faster than using a StringBuilder with every byte.ToString("x2")
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        public static T FromBase64JsonString<T>(this string value)
        {
            return JsonSerializer.Deserialize<T>(new ReadOnlySpan<byte>(Convert.FromBase64String(value)), options);
        }

        public static T FromJsonString<T>(this string value)
        {
            return JsonSerializer.Deserialize<T>(value, options);
        }
    }
}
