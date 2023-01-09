﻿using System.Collections.Concurrent;

namespace Olive
{
    public class IOEventBusProvider : IEventBusQueueProvider
    {
        ConcurrentDictionary<string, IOEventBusQueue> Queues = new ConcurrentDictionary<string, IOEventBusQueue>();

        public IEventBusQueue Provide(string queueUrl) => Queues.GetOrAdd(queueUrl, x => new IOEventBusQueue(x));
    }
}
