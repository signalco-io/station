using System;

namespace Signal.Beacon.Core.Structures.Queues
{
    public static class DelayedQueueExtensions
    {
        public static void Enqueue<T>(this IDelayedQueue<T> queue, T item, DateTime timeStamp) =>
            queue.Enqueue(item, timeStamp - DateTime.UtcNow);
    }
}
