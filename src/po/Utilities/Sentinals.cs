using System.Threading;
using System.Threading.Tasks;

namespace po.Utilities
{
    public sealed class Sentinals
    {
        public SentinalImplementation DBMigration { get; } = new();

        public sealed class SentinalImplementation
        {
            private readonly SemaphoreSlim semaphore;
            private readonly Task task;
            public SentinalImplementation()
            {
                this.semaphore = new SemaphoreSlim(0, 1);
                this.task = this.semaphore.WaitAsync();
            }
            /// <summary>
            /// Should only be called once per sentinal.
            /// Used to release any waiting to let the startup process continue.
            /// </summary>
            public void SignalCompletion() => _ = this.semaphore.Release(1);
            /// <summary>
            /// Await this if you need to wait for the sentinal.
            /// </summary>
            public async Task WaitForCompletionAsync(CancellationToken cancellationToken)
            {
                using CancellationTokenRegistration _ = cancellationToken.Register(() => this.semaphore.Release(1));
                await this.task;
            }
        }
    }
}
