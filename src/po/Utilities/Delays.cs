using System;
using System.Threading;
using System.Threading.Tasks;

namespace po.Utilities
{
    public sealed class Delays
    {

        public sealed class DelayImpelmentation
        {
            private TaskCompletionSource earlyTaskCompletionSource;

            /// <summary>
            /// Intended to only be called by one location at a time and not in parallel.
            /// </summary>
            public async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                this.earlyTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

                var delayTask = Task.Delay(delay, cancellationToken);

                _ = await Task.WhenAny(delayTask, this.earlyTaskCompletionSource.Task);
                _ = this.earlyTaskCompletionSource.TrySetCanceled();
            }
        }
    }
}
