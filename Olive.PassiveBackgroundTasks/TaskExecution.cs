﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Olive.PassiveBackgroundTasks
{
    class TaskExecution : IDisposable
    {
        IBackgourndTask BackgroundTask;
        CancellationTokenSource CancellationTokenSource;
        CancellationToken CancellationToken;
        Task HeartbeatTask;
        bool Cancelled;
        TaskExecution()
        {

        }

        TaskExecution(IBackgourndTask backgroundTask)
        {
            BackgroundTask = backgroundTask;
            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;
            HeartbeatTask = CreateHeartbeatTask();
        }

        internal static async Task Run(IBackgourndTask task)
        {
            using (var runner = new TaskExecution(task))
            {
                await runner.DoRun().ConfigureAwait(false);
            }
        }

        async Task DoRun()
        {
            var start = LocalTime.Now;
            Log.For(this).Info($"Execution started for {BackgroundTask.Name} at {start}");

            if (await EnsureIAmTheOnlyRunningInstance().ConfigureAwait(false) == false) return;

            await Task.Run(() => HeartbeatTask).ConfigureAwait(false);

            await BackgroundTask.GetActionTask().ConfigureAwait(false);

            await BackgroundTask.RecordExecution().ConfigureAwait(false);

            Log.For(this).Info($"Execution Finished for {BackgroundTask.Name} at {LocalTime.Now}. Took : {LocalTime.Now.Subtract(start).ToNaturalTime()}");
        }

        async Task<bool> EnsureIAmTheOnlyRunningInstance()
        {
            return (await Context.Current.Database().Reload(BackgroundTask).ConfigureAwait(false)).ExecutingInstance == ExecutionEngine.Id;
        }

        const int HEARTBEAT_DELAY = 1000;
        Task CreateHeartbeatTask()
        {
            return Task.Run(async () =>
            {
                while (!CancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        BackgroundTask = await BackgroundTask.SendHeartbeat().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.For(this).Error(ex, "Failed to log heartbeat because : " + ex.ToFullMessage());
                    }

                    await Task.Delay(HEARTBEAT_DELAY).ConfigureAwait(false);
                }
            }, CancellationToken);
        }

        public void Dispose()
        {
            CancellationTokenSource.Cancel();
            HeartbeatTask.Wait();

            CancellationTokenSource.Dispose();
            HeartbeatTask.Dispose();
        }
    }
}
