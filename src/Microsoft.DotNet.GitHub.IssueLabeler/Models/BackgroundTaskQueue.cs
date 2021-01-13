
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
        private readonly IModelHolder _modelHolder;
        private ConcurrentQueue<Func<CancellationToken, Task>> _workItems =
            new ConcurrentQueue<Func<CancellationToken, Task>>();
        private SemaphoreSlim _signal = new SemaphoreSlim(0);

        public BackgroundTaskQueue(
            IModelHolder modelHolder,
            IConfiguration configuration,
            ILogger<BackgroundTaskQueue> logger)
        {
            _logger = logger;
            _modelHolder = modelHolder;
        }

        public void QueueBackgroundWorkItem(
            Func<CancellationToken, Task> workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            if (!_modelHolder.LoadRequested)
            {
                // TODO perhaps restrict to only try once per application lifetime in the future 
                _workItems.Enqueue((ct) => _modelHolder.LoadEnginesAsync());
                _signal.Release();
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