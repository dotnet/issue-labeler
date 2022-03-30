using Microsoft.DotNet.GitHub.IssueLabeler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    // make singleton => bg service and the controller can access.....
    // IModelHolder.... holds the prediction engin.... -> is it loaded yet? then if so return suggestion
    public class LocalFileModelHolder : IModelHolder
    {
        private readonly ILogger<LocalFileModelHolderFactory> _logger;

        public LocalFileModelHolder(ILogger<LocalFileModelHolderFactory> logger, IConfiguration configuration, string owner, string repo)
        {
            // TODO: imagine there is an array of model holders, prefixes itself with owner/repo info.
            _logger = logger;

            // the following four configuration values are per repo values.
            _issuePath = LocalFileModelHolderFactory.GetModelPath(owner, repo, "issues");

            _prPath = LocalFileModelHolderFactory.GetModelPath(owner, repo, "prs");
            if (!File.Exists(_prPath))
            {
                // has issue config only - allowed
                UseIssuesForPrsToo = true;
            }

            _loadRequested = 0;
        }

        private int _loadRequested;
        private bool IsPrModelPathDownloaded => UseIssuesForPrsToo || File.Exists(_prPath);
        private readonly string _prPath;
        private readonly string _issuePath;

        public bool LoadRequested => _loadRequested != 0;
        public bool IsPrEngineLoaded => (PrPredEngine != null);
        public bool IsIssueEngineLoaded => (IssuePredEngine != null);
        public bool UseIssuesForPrsToo { get; private set; }
        public PredictionEngine<IssueModel, GitHubIssuePrediction> IssuePredEngine { get; private set; } = null;
        public PredictionEngine<PrModel, GitHubIssuePrediction> PrPredEngine { get; private set; } = null;
        public Task LoadEnginesAsync()
        {
            _logger.LogInformation($"! {nameof(LoadEnginesAsync)} called.");
            Interlocked.Increment(ref _loadRequested);
            if (IsIssueEngineLoaded && (UseIssuesForPrsToo || IsPrEngineLoaded))
            {
                _logger.LogInformation($"! engines were already loaded.");
                return Task.CompletedTask;
            }

            if (!IsIssueEngineLoaded)
            {
                _logger.LogInformation($"! loading {nameof(IssuePredEngine)}.");
                var mlContext = new MLContext();
                var mlModel = mlContext.Model.Load(_issuePath, out DataViewSchema _);
                IssuePredEngine = mlContext.Model.CreatePredictionEngine<IssueModel, GitHubIssuePrediction>(mlModel);
                _logger.LogInformation($"! {nameof(IssuePredEngine)} loaded.");
            }
            if (!UseIssuesForPrsToo && !IsPrEngineLoaded)
            {
                _logger.LogInformation($"! loading {nameof(PrPredEngine)}.");
                var mlContext = new MLContext();
                var mlModel = mlContext.Model.Load(_prPath, out DataViewSchema _);
                PrPredEngine = mlContext.Model.CreatePredictionEngine<PrModel, GitHubIssuePrediction>(mlModel);
                _logger.LogInformation($"! {nameof(PrPredEngine)} loaded.");
            }

            return Task.CompletedTask;
        }
    }
}
