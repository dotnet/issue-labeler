using System;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.ML;
using Azure.Storage.Blobs.Models;
using System.IO;
using System.Threading;
using Microsoft.DotNet.GitHub.IssueLabeler;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public interface IModelHolder
    {
        bool IsPrEngineLoaded { get; }
        bool LoadRequested { get; }
        bool IsIssueEngineLoaded { get; }
        PredictionEngine<IssueModel, GitHubIssuePrediction> IssuePredEngine { get; }
        PredictionEngine<PrModel, GitHubIssuePrediction> PrPredEngine { get; }
        Task LoadEnginesAsync();
        bool UseIssuesForPrsToo { get; }
    }

    // make singleton => bg service and the controller can access.....
    // IModelHolder.... holds the prediction engin.... -> is it loaded yet? then if so return suggestion
    public class ModelHolder : IModelHolder
    {
        private readonly ILogger<ModelHolderFactory> _logger;
        public ModelHolder(ILogger<ModelHolderFactory> logger, IConfiguration configuration, string repo)
        {
            // TODO: imagine there is an array of model holders, prefixes itself with owner/repo info.

            _logger = logger;
            _connectionString = configuration["QConnectionString"];
            _blobContainerName = configuration["BlobContainer"];

            // the following four configuration values are per repo values.
            string configSection = $"IssueModel:{repo}:PathPrefix";
            if (string.IsNullOrEmpty(configuration[configSection]))
            {
                throw new ArgumentNullException($"repo: {repo}, missing config.");
            }
            IssuePath = Path.Combine(Directory.GetCurrentDirectory(), $"{configuration[configSection]}.zip");

            configSection = $"IssueModel:{repo}:BlobName";
            if (string.IsNullOrEmpty(configuration[configSection]))
            {
                throw new ArgumentNullException($"repo: {repo}, missing config..");
            }
            _issueModelBlobName = configuration[configSection];

            configSection = $"PrModel:{repo}:PathPrefix";
            if (!string.IsNullOrEmpty(configuration[configSection]))
            {
                PrPath = Path.Combine(Directory.GetCurrentDirectory(), $"{configuration[configSection]}.zip");

                // has both pr and issue config - allowed
                configSection = $"PrModel:{repo}:BlobName";
                if (string.IsNullOrEmpty(configuration[configSection]))
                {
                    throw new ArgumentNullException($"repo: {repo}, missing config...");
                }
                _prModelBlobName = configuration[configSection];
            }
            else
            {
                // has issue config only - allowed
                UseIssuesForPrsToo = true;

                configSection = $"PrModel:{repo}:BlobName";

                if (!string.IsNullOrEmpty(configuration[configSection]))
                {
                    throw new ArgumentNullException($"repo: {repo}, missing config....");
                }
            }
            _loadRequested = 0;
        }
        private int _loadRequested;
        private bool IsPrModelPathDownloaded => (UseIssuesForPrsToo && IsIssueModelPathDownloaded) || File.Exists(PrPath);
        private bool IsIssueModelPathDownloaded => File.Exists(IssuePath);
        private string PrPath;
        private string IssuePath;

        public bool LoadRequested => _loadRequested != 0;
        public bool IsPrEngineLoaded => (PrPredEngine != null);
        public bool IsIssueEngineLoaded => (IssuePredEngine != null);
        public bool UseIssuesForPrsToo { get; private set; }
        public PredictionEngine<IssueModel, GitHubIssuePrediction> IssuePredEngine { get; private set; } = null;
        public PredictionEngine<PrModel, GitHubIssuePrediction> PrPredEngine { get; private set; } = null;
        public async Task LoadEnginesAsync()
        {
            _logger.LogInformation($"! {nameof(LoadEnginesAsync)} called.");
            Interlocked.Increment(ref _loadRequested);
            if (IsIssueEngineLoaded && (UseIssuesForPrsToo || IsPrEngineLoaded))
            {
                _logger.LogInformation($"! engines were already loaded.");
                return;
            }
            await EnsureModelPathsAvailableAsync();
            if (!IsIssueEngineLoaded)
            {
                _logger.LogInformation($"! loading {nameof(IssuePredEngine)}.");
                MLContext mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(IssuePath, out DataViewSchema _);
                IssuePredEngine = mlContext.Model.CreatePredictionEngine<IssueModel, GitHubIssuePrediction>(mlModel);
                _logger.LogInformation($"! {nameof(IssuePredEngine)} loaded.");
            }
            if (!UseIssuesForPrsToo && !IsPrEngineLoaded)
            {
                _logger.LogInformation($"! loading {nameof(PrPredEngine)}.");
                MLContext mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(PrPath, out DataViewSchema _);
                PrPredEngine = mlContext.Model.CreatePredictionEngine<PrModel, GitHubIssuePrediction>(mlModel);
                _logger.LogInformation($"! {nameof(PrPredEngine)} loaded.");
            }
        }

        private int timesIssueDownloaded = 0;
        private int timesPrDownloaded = 0;

        private async Task EnsureModelPathsAvailableAsync()
        {
            _logger.LogInformation($"! {nameof(EnsureModelPathsAvailableAsync)} called.");
            if (IsIssueModelPathDownloaded && IsPrModelPathDownloaded)
            {
                return;
            }

            _logger.LogInformation($"! calling {nameof(BlobContainerClient)}.");
            BlobContainerClient container = new BlobContainerClient(_connectionString, _blobContainerName);
            container.CreateIfNotExists(PublicAccessType.Blob);

            try
            {
                if (!IsIssueModelPathDownloaded)
                {
                    _logger.LogInformation($"! downloading to {IssuePath}.");
                    await DownloadModelAsync(_logger, container, _issueModelBlobName, /*lastUpdated,*/ IssuePath);
                    Interlocked.Increment(ref timesIssueDownloaded);
                }
                if (!IsPrModelPathDownloaded)
                {
                    _logger.LogInformation($"! downloading to {PrPath}.");
                    await DownloadModelAsync(_logger, container, _prModelBlobName, /*lastUpdated,*/ PrPath);
                    Interlocked.Increment(ref timesPrDownloaded);
                }
                _logger.LogInformation($"! downloaded version of ml model available at {Directory.GetCurrentDirectory()}.");
                _logger.LogInformation($"! {nameof(timesPrDownloaded)}: {timesPrDownloaded}, {nameof(timesIssueDownloaded)}: {timesIssueDownloaded}");
            }
            catch (Exception ex)
            {
                _logger.LogError("error dl of labeler model. " + ex.Message);
            }
        }

        private static async Task DownloadModelAsync(
            ILogger _logger, BlobContainerClient container, string blobName, /*DateTimeOffset lastUpdated,*/
            string localPath
            )
        {
            if (!File.Exists(localPath))
            {
                var condition = new BlobRequestConditions()
                { };// TODO { IfModifiedSince = lastUpdated };
                var blockBlob = container.GetBlobClient(blobName);

                BlobProperties properties = await blockBlob.GetPropertiesAsync();
                // TODO check properties.LastModified
                _logger.LogInformation($"conditionally downloading {blobName}");
                using (var fileStream = System.IO.File.OpenWrite(localPath))
                {
                    await blockBlob.DownloadToAsync(fileStream, condition, new StorageTransferOptions() { });
                }
                // TODO: new FileStream, pass in buffer size 1MB rather than default 1KB
                _logger.LogInformation($"downloaded ml model");
            }
        }

        private readonly string _blobContainerName;
        private readonly string _prModelBlobName;
        private readonly string _issueModelBlobName;
        private readonly string _connectionString;
    }
}