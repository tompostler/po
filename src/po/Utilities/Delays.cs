namespace po.Utilities
{
    public sealed class Delays
    {
        public DelayImpelmentation RandomMessage { get; } = new();
        public DelayImpelmentation ScheduledBlob { get; } = new();
        public DelayImpelmentation ScheduledMessage { get; } = new();

        public sealed class DelayImpelmentation
        {
            private TaskCompletionSource earlyTaskCompletionSource;

            /// <summary>
            /// Gracefully cancel the delay.
            /// </summary>
            public void CancelDelay() => this.earlyTaskCompletionSource?.TrySetResult();

            /// <summary>
            /// Intended to only be called by one location at a time and not in parallel.
            /// </summary>
            public async Task Delay(TimeSpan delay, ILogger logger, CancellationToken cancellationToken)
            {
                this.earlyTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

                var cts = new CancellationTokenSource();
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

                var delayTask = Task.Delay(delay, linkedCts.Token);

                // Whichever one returned, cancel the other
                logger.LogInformation($"Sleeping {delay}");
                Task returnedTask = await Task.WhenAny(delayTask, this.earlyTaskCompletionSource.Task);
                if (returnedTask == delayTask)
                {
                    logger.LogInformation("Delay task completed. Cancelling early completion task.");
                    _ = this.earlyTaskCompletionSource.TrySetCanceled(CancellationToken.None);
                    this.earlyTaskCompletionSource = null;
                    await delayTask;
                }
                else
                {
                    logger.LogInformation("Early task completed. Cancelling delay task.");
                    cts.Cancel();
                }
            }
        }
    }
}
