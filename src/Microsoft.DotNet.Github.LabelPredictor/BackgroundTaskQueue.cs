// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Github.IssueLabeler.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    public interface IBackgroundTaskQueue
    {
        void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);

        Task<Func<CancellationToken, Task>> DequeueAsync(
            CancellationToken cancellationToken);
    }

    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly ILogger<BackgroundTaskQueue> _logger;
        private ConcurrentQueue<Func<CancellationToken, Task>> _workItems =
            new ConcurrentQueue<Func<CancellationToken, Task>>();
        private SemaphoreSlim _signal = new SemaphoreSlim(0);

        public BackgroundTaskQueue(
            IConfiguration configuration,
            ILogger<BackgroundTaskQueue> logger)
        {
            _logger = logger;
        }

        public void QueueBackgroundWorkItem(
            Func<CancellationToken, Task> workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        public async Task<Func<CancellationToken, Task>> DequeueAsync(
            CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            _logger.LogInformation("dequeued work item");

            return workItem;
        }
    }
}