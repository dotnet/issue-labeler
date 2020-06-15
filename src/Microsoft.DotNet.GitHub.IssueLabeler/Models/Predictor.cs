// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.ML;
using System.Linq;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    internal static class Predictor
    {
        private static string PrModelPath => @"model\GitHubPrLabelerModel.zip";
        private static string IssueModelPath => @"model\GitHubIssueLabelerModel.zip";
        private static string GeneralizedPrModelPath => @"model\GeneralizedGitHubPrLabelerModel.zip";
        private static string GeneralizedIssueModelPath => @"model\GeneralizedGitHubIssueLabelerModel.zip";
        private static PredictionEngine<IssueModel, GitHubIssuePrediction> issuePredEngine;
        private static PredictionEngine<PrModel, GitHubIssuePrediction> prPredEngine;
        private static PredictionEngine<IssueModel, GitHubIssuePrediction> generalizedIssuePredEngine;
        private static PredictionEngine<PrModel, GitHubIssuePrediction> generalizedPrPredEngine;

        public static string Predict(IssueModel issue, ILogger logger, double threshold)
        {
            return Predict(issue, ref issuePredEngine, IssueModelPath, ref generalizedIssuePredEngine, GeneralizedIssueModelPath, logger, threshold);
        }

        public static string Predict(PrModel issue, ILogger logger, double threshold)
        {
            return Predict(issue, ref prPredEngine, PrModelPath, ref generalizedPrPredEngine, GeneralizedPrModelPath, logger, threshold);
        }

        public static string Predict<T>(
            T issueOrPr, 
            ref PredictionEngine<T, GitHubIssuePrediction> predEngine, 
            string modelPath, 
            ref PredictionEngine<T, GitHubIssuePrediction> generalizedPredEngine, 
            string generalizedModelPath,
            ILogger logger, 
            double threshold) 
            where T : IssueModel
        {
            if (predEngine == null)
            {
                MLContext mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(modelPath, out DataViewSchema inputSchema);
                predEngine = mlContext.Model.CreatePredictionEngine<T, GitHubIssuePrediction>(mlModel);
            }

            GitHubIssuePrediction prediction = predEngine.Predict(issueOrPr);
            float[] probabilities = prediction.Score;
            float maxProbability = probabilities.Max();
            logger.LogInformation($"# {maxProbability} {prediction.Area} for #{issueOrPr.Number} {issueOrPr.Title}");
            if (maxProbability > threshold)
            {
                return prediction.Area;
            }

            if (generalizedPredEngine == null)
            {
                MLContext mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(generalizedModelPath, out DataViewSchema inputSchema);
                generalizedPredEngine = mlContext.Model.CreatePredictionEngine<T, GitHubIssuePrediction>(mlModel);
            }
            prediction = generalizedPredEngine.Predict(issueOrPr);
            probabilities = prediction.Score;
            maxProbability = probabilities.Max();
            logger.LogInformation($"# generalized: {maxProbability} {prediction.Area} for #{issueOrPr.Number} {issueOrPr.Title}");
            return maxProbability > threshold ? prediction.Area : null;
        }
    }
}
