using Microsoft.DotNet.GitHub.IssueLabeler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public class LocalFileModelHolderFactory : IModelHolderFactory
    {
        private readonly ConcurrentDictionary<(string, string), IModelHolder> _models = new ConcurrentDictionary<(string, string), IModelHolder>();
        private readonly ILogger<LocalFileModelHolderFactory> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public LocalFileModelHolderFactory(
            ILogger<LocalFileModelHolderFactory> logger,
            IConfiguration configuration,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _backgroundTaskQueue = backgroundTaskQueue;
            _configuration = configuration;
            _logger = logger;
        }

        public IModelHolder CreateModelHolder(string owner, string repo)
        {
            if (!IsConfigured(owner, repo))
                return null;
            return _models.TryGetValue((owner, repo), out IModelHolder modelHolder) ?
                modelHolder :
               _models.GetOrAdd((owner, repo), InitFor(owner, repo));
        }

        private bool IsConfigured(string owner, string repo)
        {
            return File.Exists(GetModelPath(owner, repo, "issues"));
        }

        private IModelHolder InitFor(string owner, string repo)
        {
            var mh = new LocalFileModelHolder(_logger, _configuration, owner, repo);
            if (!mh.LoadRequested)
            {
                _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => mh.LoadEnginesAsync());
            }
            return mh;
        }

        /// <summary>
        /// </summary>
        /// <param name="owner"></param>
        /// <param name="repo"></param>
        /// <param name="modelType">Use 'issues' or 'prs' only.</param>
        /// <returns></returns>
        internal static string GetModelPath(string owner, string repo, string modelType)
        {
            var modelRootFolder = System.IO.Directory.GetCurrentDirectory();

            // Filename looks like this: dotnet-maui-only-issues-final-model.zip

            return Path.Combine(modelRootFolder, $"{owner}-{repo}-only-{modelType}-final-model.zip");
        }
    }
}
