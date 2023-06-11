// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML;
using Microsoft.ML.Data;

namespace PredictionService.Models;

public static class Predictor
{
    public static LabelSuggestion Predict(GitHubIssue issue, ILogger logger, IModelHolder modelHolder)
    {
        return Predict(issue, modelHolder.IssuePredEngine, logger);
    }

    public static LabelSuggestion Predict(GitHubPullRequest issue, ILogger logger, IModelHolder modelHolder)
    {
        if (modelHolder.UseIssuesForPrsToo)
        {
            return Predict(issue, modelHolder.IssuePredEngine, logger);
        }
        return Predict(issue, modelHolder.PrPredEngine, logger);
    }

    private static LabelSuggestion Predict<T>(
        T issueOrPr,
        PredictionEngine<T, GitHubIssuePrediction> predEngine,
        ILogger logger)
        where T : GitHubIssue
    {
        if (predEngine == null)
        {
            throw new InvalidOperationException("expected prediction engine loaded.");
        }

        GitHubIssuePrediction prediction = predEngine.Predict(issueOrPr);

        VBuffer<ReadOnlyMemory<char>> slotNames = default;
        predEngine.OutputSchema[nameof(GitHubIssuePrediction.Score)].GetSlotNames(ref slotNames);

        float[] probabilities = prediction.Score;
        var labelPredictions = GetBestThreePredictions(probabilities, slotNames);

        float maxProbability = probabilities.Max();
        logger.LogInformation($"# {maxProbability} {prediction.Area} for #{issueOrPr.Number} {issueOrPr.Title}");
        return new LabelSuggestion
        {
            LabelScores = labelPredictions,
        };
    }

    private static List<LabelAreaScore> GetBestThreePredictions(float[] scores, VBuffer<ReadOnlyMemory<char>> slotNames)
    {
        var topThreeScores = GetIndexesOfTopScores(scores, 3);

        return new List<LabelAreaScore>
            {
                new LabelAreaScore {LabelName=slotNames.GetItemOrDefault(topThreeScores[0]).ToString(), Score = scores[topThreeScores[0]] },
                new LabelAreaScore {LabelName=slotNames.GetItemOrDefault(topThreeScores[1]).ToString(), Score = scores[topThreeScores[1]] },
                new LabelAreaScore {LabelName=slotNames.GetItemOrDefault(topThreeScores[2]).ToString(), Score = scores[topThreeScores[2]] },
            };
    }

    private static IReadOnlyList<int> GetIndexesOfTopScores(float[] scores, int n)
    {
        var indexedScores = scores
            .Zip(Enumerable.Range(0, scores.Length), (score, index) => new IndexedScore(index, score));

        var indexedScoresSortedByScore = indexedScores
            .OrderByDescending(indexedScore => indexedScore.Score);

        return indexedScoresSortedByScore
            .Take(n)
            .Select(indexedScore => indexedScore.Index)
            .ToList()
            .AsReadOnly();
    }

    private struct IndexedScore
    {
        public IndexedScore(int index, float score) => (Index, Score) = (index, score);

        public int Index { get; }
        public float Score { get; }
    }
}
