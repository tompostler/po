using System.Threading;
using System.Threading.Tasks;

namespace po.Utilities
{
    public sealed class Sentinals
    {
        public SentinalImplementation DBMigration { get; } = new();
        public SentinalImplementation<Discord.WebSocket.DiscordSocketClient> DiscordClient { get; } = new();

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
            /// A non-async way to check if we're ready before deciding to wait for completion.
            /// </summary>
            public bool IsReady => this.semaphore.CurrentCount > 0;
            /// <summary>
            /// Await this if you need to wait for the sentinal.
            /// If the cancellation token is cancelled, then what you were waiting for will not be ready.
            /// </summary>
            public async Task WaitForCompletionAsync(CancellationToken cancellationToken)
            {
                // Since we can't await a cancellation token directly, this will allow us to create a task that cancels if the cancellation token cancels
                TaskCompletionSource waitTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = cancellationToken.Register(() => waitTaskCompletionSource.TrySetCanceled(cancellationToken));

                _ = await Task.WhenAny(this.task, waitTaskCompletionSource.Task);
            }
        }

        public sealed class SentinalImplementation<T>
        {
            private readonly SemaphoreSlim semaphore;
            private readonly Task task;
            private T waitingFor;
            public SentinalImplementation()
            {
                this.semaphore = new SemaphoreSlim(0, 1);
                this.task = this.semaphore.WaitAsync();
            }
            /// <summary>
            /// Should only be called once per sentinal.
            /// Used to release any waiting to let the startup process continue.
            /// </summary>
            public void SignalCompletion(T thingThatIsNowReady)
            {
                _ = this.semaphore.Release(1);
                this.waitingFor = thingThatIsNowReady;
            }
            /// <summary>
            /// A non-async way to check if we're ready before deciding to wait for completion.
            /// </summary>
            public bool IsReady => this.semaphore.CurrentCount > 0;
            /// <summary>
            /// Await this if you need to wait for the sentinal and need something from it.
            /// </summary>
            public async Task<T> WaitForCompletionAsync(CancellationToken cancellationToken)
            {
                // Since we can't await a cancellation token directly, this will allow us to create a task that cancels if the cancellation token cancels
                TaskCompletionSource waitTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = cancellationToken.Register(() => waitTaskCompletionSource.TrySetCanceled(cancellationToken));

                if (this.task == await Task.WhenAny(this.task, waitTaskCompletionSource.Task))
                {
                    return this.waitingFor;
                }
                else
                {
                    return default;
                }
            }
        }
    }
}
