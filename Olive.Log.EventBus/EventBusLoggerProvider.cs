﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Olive.Log.EventBus
{
    public class EventBusLoggerProvider : ILoggerProvider
    {
        readonly List<LogMessage> CurrentBatch = new List<LogMessage>();
        readonly TimeSpan Interval;
        int? QueueSize, BatchSize;
        string QueueUrl;

        BlockingCollection<LogMessage> MessageQueue;
        Task OutputTask;
        CancellationTokenSource CancellationTokenSource;

        protected EventBusLoggerProvider(IOptions<EventBusLoggerOptions> options)
        {
            // NOTE: Only IsEnabled is monitored

            var loggerOptions = options.Value;
            if (loggerOptions.BatchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(loggerOptions.BatchSize));

            if (loggerOptions.FlushPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(loggerOptions.FlushPeriod));

            Interval = loggerOptions.FlushPeriod;
            BatchSize = loggerOptions.BatchSize;
            QueueSize = loggerOptions.BackgroundQueueSize;
            QueueUrl = loggerOptions.QueueUrl;
            Start();
        }

        internal Task WriteMessagesAsync(IEnumerable<LogMessage> messages, CancellationToken token)
        {
            return Olive.EventBus.Queue(QueueUrl).Publish(new EventBusLoggerMessage() { LogMessages = messages, PublishDateTime = DateTime.Now });
        }

        async Task ProcessLogQueue(object _)
        {
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                var limit = BatchSize ?? int.MaxValue;

                while (limit > 0 && MessageQueue.TryTake(out var message))
                {
                    CurrentBatch.Add(message);
                    limit--;
                }

                if (CurrentBatch.Any())
                {
                    try { await WriteMessagesAsync(CurrentBatch, CancellationTokenSource.Token); }
                    catch
                    {
                        // ignored
                    }

                    CurrentBatch.Clear();
                }

                await Task.Delay(Interval, CancellationTokenSource.Token);
            }
        }

        internal void AddMessage(DateTimeOffset timestamp, string message)
        {
            if (!MessageQueue.IsAddingCompleted)
            {
                try
                {
                    MessageQueue.Add(new LogMessage { Message = message, Timestamp = timestamp }, CancellationTokenSource.Token);
                }
                catch
                {
                    // cancellation token canceled or CompleteAdding called
                }
            }
        }

        void Start()
        {
            if (QueueSize == null)
                MessageQueue = new BlockingCollection<LogMessage>(new ConcurrentQueue<LogMessage>());
            else MessageQueue = new BlockingCollection<LogMessage>(new ConcurrentQueue<LogMessage>(), QueueSize.Value);

            CancellationTokenSource = new CancellationTokenSource();
            OutputTask = Task.Factory.StartNew(ProcessLogQueue, null, TaskCreationOptions.LongRunning);
        }

        public ILogger CreateLogger(string categoryName) => new EventBusLogger(this, categoryName);

        public void Dispose()
        {
            CancellationTokenSource.Cancel();
            MessageQueue.CompleteAdding();

            try { OutputTask.Wait(Interval); }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.IsSingle() && ex.InnerExceptions[0] is TaskCanceledException) { }
        }
    }
}