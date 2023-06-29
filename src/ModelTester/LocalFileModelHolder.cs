// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML;
using PredictionEngine;
using System.Diagnostics.CodeAnalysis;

namespace ModelTester
{
    // make singleton => bg service and the controller can access.....
    // IModelHolder.... holds the prediction engine.... -> is it loaded yet? then if so return suggestion
    internal class LocalFileModelHolder : IModelHolder
    {
        private readonly string _prPath;
        private readonly string _issuePath;

        public LocalFileModelHolder(string owner, string repo, string issuePath, string prPath)
        {
            _issuePath = issuePath;
            _prPath = prPath;

            if (!File.Exists(_prPath))
            {
                UseIssuesForPrsToo = true;
            }

            LoadEngines();
        }

        bool IModelHolder.LoadRequested => throw new NotImplementedException();
        bool IModelHolder.IsPrEngineLoaded => (PrPredEngine != null);
        bool IModelHolder.IsIssueEngineLoaded => (IssuePredEngine != null);
        
        public bool UseIssuesForPrsToo { get; private set; }

        public PredictionEngine<GitHubIssue, GitHubIssuePrediction> IssuePredEngine { get; private set; }
        public PredictionEngine<GitHubPullRequest, GitHubIssuePrediction> PrPredEngine { get; private set; }


        [MemberNotNull("IssuePredEngine", "PrPredEngine")]
        public void LoadEngines()
        {
            var issueContext = new MLContext();
            var issueModel = issueContext.Model.Load(_issuePath, out DataViewSchema _);
            IssuePredEngine = issueContext.Model.CreatePredictionEngine<GitHubIssue, GitHubIssuePrediction>(issueModel);

            if (!UseIssuesForPrsToo)
            {
                var prContext = new MLContext();
                var prModel = prContext.Model.Load(_prPath, out DataViewSchema _);
                PrPredEngine = prContext.Model.CreatePredictionEngine<GitHubPullRequest, GitHubIssuePrediction>(prModel);
            }
            else
            {
                PrPredEngine = issueContext.Model.CreatePredictionEngine<GitHubPullRequest, GitHubIssuePrediction>(issueModel);
            }
        }
    }
}
