using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Core.Structures.Queues
{
    public class DelayedQueue<T> : IDelayedQueue<T>
    {
        private readonly AsyncBlockingQueue queue = new();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new()) =>
            this.queue.GetAsyncEnumerator(cancellationToken);

        public void Enqueue(T item, TimeSpan due) => this.queue.Enqueue(item, due);
        
        private class AsyncBlockingQueue : IAsyncEnumerable<T>
        {
            private readonly AsyncBlockingQueueEnumerator enumerator = new();

            public void Enqueue(T item, TimeSpan due) => this.enumerator.Enqueue(item, due);

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new()) =>
                this.enumerator;
        }

        private class AsyncBlockingQueueEnumerator : IAsyncEnumerator<T>
        {
            private readonly SortedList<TimeSpan, T?> queue = new();

            private TimeSpan? nextItemDue;
            private TaskCompletionSource<T?> nextItemDelayTask = new();
            private CancellationTokenSource? delayTaskCancellationTokenSource;
            private readonly object queueLock = new();

            private void SetDelay()
            {
                if (this.queue.Count > 0)
                {
                    var next = this.queue.FirstOrDefault();

                    // Check if new item needs due reduced
                    if (this.nextItemDue != null && !(next.Key < this.nextItemDue)) return;

                    // Set new delay
                    this.delayTaskCancellationTokenSource?.Cancel();
                    this.delayTaskCancellationTokenSource = new CancellationTokenSource();

                    var previousTask = this.nextItemDelayTask;
                    this.nextItemDelayTask = new TaskCompletionSource<T?>();
                    this.nextItemDue = next.Key;
                    if (!previousTask.Task.IsCompleted)
                        previousTask.SetCanceled();

                    var token = this.delayTaskCancellationTokenSource.Token;
                    Task.Delay(next.Key)
                        .ContinueWith(_ => { this.Dequeue(next, token); });
                }
                else
                {
                    this.nextItemDelayTask = new TaskCompletionSource<T?>();
                }
            }

            public void Enqueue(T item, TimeSpan due)
            {
                lock (this.queueLock)
                {
                    this.queue.Add(due, item);
                    this.SetDelay();
                }
            }

            private void Dequeue(KeyValuePair<TimeSpan, T?> item, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                lock (this.queueLock)
                {
                    this.queue.RemoveAt(0);
                    this.nextItemDelayTask.SetResult(item.Value);
                    this.nextItemDue = null;
                    this.SetDelay();
                }
            }

            public ValueTask DisposeAsync()
            {
                lock (this.queueLock)
                {
                    if (!this.nextItemDelayTask.Task.IsCompleted)
                        this.nextItemDelayTask.SetCanceled();
                    this.delayTaskCancellationTokenSource?.Cancel();
                }

                return ValueTask.CompletedTask;
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                var task = this.nextItemDelayTask.Task;
                while (true)
                {
                    try
                    {
                        this.Current = await task;
                        return true;
                    }
                    catch (TaskCanceledException)
                    {
                        task = this.nextItemDelayTask.Task;
                    }
                }
            }

            public T? Current { get; private set; }
        }
    }
}