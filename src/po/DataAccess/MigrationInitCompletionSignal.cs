using System.Threading;
using System.Threading.Tasks;

namespace po.DataAccess
{
    public sealed class MigrationInitCompletionSignal
    {
        private readonly SemaphoreSlim semaphore;
        private readonly Task task;

        public MigrationInitCompletionSignal()
        {
            this.semaphore = new SemaphoreSlim(0, 1);
            this.task = this.semaphore.WaitAsync();
        }

        public void SignalCompletion()
        {
            this.semaphore.Release(1);
        }

        public async Task WaitForCompletionAsync(CancellationToken cancellationToken)
        {
            using var _ = cancellationToken.Register(() => this.semaphore.Release(1));
            await this.task;
        }
    }
}
