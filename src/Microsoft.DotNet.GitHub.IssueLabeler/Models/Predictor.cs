// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.IO;
using System.Linq;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    internal static class Predictor
    {
        private static string PrModelPath(string repoOwner, string repoName) => Path.Combine("model", repoOwner, repoName, "GitHubPrLabelerModel.zip");
        private static string IssueModelPath(string repoOwner, string repoName) => Path.Combine("model", repoOwner, repoName, "GitHubLabelerModel.zip");

        private static PredictionEngine<IssueModel, GitHubIssuePrediction> issuePredEngine;
        private static PredictionEngine<PrModel, GitHubIssuePrediction> prPredEngine;

        public static LabelSuggestion Predict(string repoOwner, string repoName, IssueModel issue, ILogger logger, double threshold)
        {
            return Predict(issue, ref issuePredEngine, IssueModelPath(repoOwner, repoName), logger, threshold);
        }

        public static LabelSuggestion Predict(string repoOwner, string repoName, PrModel issue, ILogger logger, double threshold)
        {
            return Predict(issue, ref prPredEngine, PrModelPath(repoOwner, repoName), logger, threshold);
        }

        public static LabelSuggestion Predict<T>(
            T issueOrPr,
            ref PredictionEngine<T, GitHubIssuePrediction> predEngine,
            string modelPath,
            ILogger logger,
            double threshold)
            where T : IssueModel
        {
            if (predEngine == null)
            {
                MLContext mlContext = new MLContext();
                ITransformer mlModel = mlContext.Model.Load(modelPath, out DataViewSchema _);
                predEngine = mlContext.Model.CreatePredictionEngine<T, GitHubIssuePrediction>(mlModel);
            }

            GitHubIssuePrediction prediction = predEngine.Predict(issueOrPr);

            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            predEngine.OutputSchema[nameof(GitHubIssuePrediction.Score)].GetSlotNames(ref slotNames);

            float[] probabilities = prediction.Score;
            var labelPredictions = MikLabelerPredictor.GetBestThreePredictions(probabilities, slotNames);

            float maxProbability = probabilities.Max();
            logger.LogInformation($"# {maxProbability} {prediction.Area} for #{issueOrPr.Number} {issueOrPr.Title}");
            return new LabelSuggestion
            {
                LabelScores = labelPredictions,
            };
        }
    }
}
