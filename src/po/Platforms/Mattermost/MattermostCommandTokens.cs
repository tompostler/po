using System.Security.Cryptography;
using System.Text;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// The token Mattermost sends with each of po's slash commands, by trigger, so the command endpoint can tell real requests from forged ones.
    /// The registrar replaces the whole set after each reconcile; until then, every request is rejected.
    /// </summary>
    public sealed class MattermostCommandTokens
    {
        private volatile IReadOnlyDictionary<string, string> tokensByTrigger = new Dictionary<string, string>();

        public void Replace(IReadOnlyDictionary<string, string> tokensByTrigger)
            => this.tokensByTrigger = new Dictionary<string, string>(tokensByTrigger, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Compares in constant time, so response timing doesn't reveal how much of a guessed token was right.
        /// </summary>
        public bool IsValid(string trigger, string token)
            => trigger != null
                && token != null
                && this.tokensByTrigger.TryGetValue(trigger, out string expected)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token));
    }
}
