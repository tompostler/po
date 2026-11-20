using Microsoft.EntityFrameworkCore;
using po.DataAccess;
using System.Text;

namespace po.Services.Po
{
    public sealed class PoStatusReport
    {
        private readonly IServiceProvider serviceProvider;

        public PoStatusReport(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Builds a table of seen and unseen image counts per category in <paramref name="containerName"/>, as a markdown code block.
        /// </summary>
        public async Task<string> BuildAsync(string containerName)
        {
            using IServiceScope scope = this.serviceProvider.CreateScope();
            using PoContext poContext = scope.ServiceProvider.GetRequiredService<PoContext>();

            var statuses = await poContext.Blobs
                .Where(b => b.ContainerName == containerName)
                .GroupBy(b => b.Category)
                .Select(g => new
                {
                    g.Key,
                    CountSeen = g.Count(b => b.Seen),
                    CountUnseen = g.Count(b => !b.Seen)
                })
                .OrderBy(g => g.Key)
                .ToListAsync();

            if (statuses.Count == 0)
            {
                return "No images known.";
            }

            StringBuilder response = new();
            int catLen = Math.Max("category".Length, statuses.Max(x => x.Key.Length));
            int numLen = 8;
            _ = response.AppendLine("```");
            _ = response.AppendLine($"{"CATEGORY".PadRight(catLen)}  {"SEEN".PadLeft(numLen)}  {"UNSEEN".PadLeft(numLen)}  {"TOTAL".PadLeft(numLen)}  {"PERCENT VIEWED".PadLeft(numLen)}");
            foreach (var status in statuses)
            {
                _ = response.Append(status.Key.PadRight(catLen));
                _ = response.Append("  ");
                _ = response.Append(status.CountSeen.ToString().PadLeft(numLen));
                _ = response.Append("  ");
                _ = response.Append(status.CountUnseen.ToString().PadLeft(numLen));
                _ = response.Append("  ");
                int loctot = status.CountSeen + status.CountUnseen;
                _ = response.Append(loctot.ToString().PadLeft(numLen));
                _ = response.Append("  ");
                _ = response.Append((1d * status.CountSeen / loctot).ToString("P2").PadLeft(numLen));
                _ = response.AppendLine();
            }
            _ = response.Append("TOTAL".PadRight(catLen));
            _ = response.Append("  ");
            _ = response.Append(statuses.Sum(x => x.CountSeen).ToString().PadLeft(numLen));
            _ = response.Append("  ");
            _ = response.Append(statuses.Sum(x => x.CountUnseen).ToString().PadLeft(numLen));
            _ = response.Append("  ");
            int ovetot = statuses.Sum(x => x.CountSeen + x.CountUnseen);
            _ = response.Append(ovetot.ToString().PadLeft(numLen));
            _ = response.Append("  ");
            _ = response.Append((1d * statuses.Sum(x => x.CountSeen) / ovetot).ToString("P2").PadLeft(numLen));
            _ = response.AppendLine("```");

            return response.ToString();
        }
    }
}
