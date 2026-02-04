using System.Security.Cryptography;
using System.Text;

namespace po.Utilities
{
    public static class SignedUrls
    {
        public static string GenerateSignature(string apiKey, string containerName, string blobName, long expiresUnixSeconds, ILogger logger)
        {
            string message = $"{containerName}/{blobName}/{expiresUnixSeconds}";
            byte[] keyBytes = Encoding.UTF8.GetBytes(apiKey);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmac = new HMACSHA256(keyBytes);
            byte[] hash = hmac.ComputeHash(messageBytes);
            string signature = Convert.ToHexStringLower(hash);

            logger.LogInformation("Generated signature [{signature}] for [{container}/{blob}] expiring at [{expires}]", signature, containerName, blobName, expiresUnixSeconds);

            return signature;
        }

        public static bool ValidateSignature(string apiKey, string containerName, string blobName, long expiresUnixSeconds, string providedSignature, ILogger logger)
        {
            string expectedSignature = GenerateSignature(apiKey, containerName, blobName, expiresUnixSeconds, logger);
            bool isValid = string.Equals(expectedSignature, providedSignature, StringComparison.OrdinalIgnoreCase);

            if (isValid)
            {
                logger.LogInformation("Signature [{signature}] validated for [{container}/{blob}]", providedSignature, containerName, blobName);
            }
            else
            {
                logger.LogWarning("Signature mismatch for [{container}/{blob}]: expected [{expected}], got [{provided}]", containerName, blobName, expectedSignature, providedSignature);
            }

            return isValid;
        }
    }
}
