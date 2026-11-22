namespace po.Utilities
{
    public sealed class Sentinels
    {
        public SentinelImplementation DBMigration { get; } = new();

        public sealed class SentinelImplementation
        {
            private readonly SemaphoreSlim semaphore;
            private readonly Task task;

            public SentinelImplementation()
            {
                this.semaphore = new SemaphoreSlim(0, 1);
                this.task = this.semaphore.WaitAsync();
            }

            /// <summary>
            /// Should only be called once per sentinel.
            /// Used to release any waiting to let the startup process continue.
            /// </summary>
            public void SignalCompletion()
            {
                // When used in a handler, it's possible that this could be called more than once if the handler fires multiple times
                // So only release the first time
                if (this.semaphore.CurrentCount == 0)
                {
                    _ = this.semaphore.Release(1);
                }
            }

            /// <summary>
            /// A non-async way to check if we're ready before deciding to wait for completion.
            /// </summary>
            public bool IsReady => this.semaphore.CurrentCount > 0;

            /// <summary>
            /// Await this if you need to wait for the sentinel.
            /// If the cancellation token is cancelled, then what you were waiting for will not be ready.
            /// </summary>
            public async Task WaitForCompletionAsync(CancellationToken cancellationToken)
            {
                // Since we can't await a cancellation token directly, this will allow us to create a task that cancels if the cancellation token cancels
                TaskCompletionSource waitTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = cancellationToken.Register(() => waitTaskCompletionSource.TrySetCanceled(cancellationToken));

                if (waitTaskCompletionSource.Task == await Task.WhenAny(this.task, waitTaskCompletionSource.Task))
                {
                    // The cancellation token fired
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }
    }
}
