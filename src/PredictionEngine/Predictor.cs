// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Octokit;
using System.Text.RegularExpressions;

namespace PredictionEngine;

public class Predictor
{
    private static Regex _userMentionRegex = new Regex(@"@[a-zA-Z0-9_//-]+");

    private GitHubClientWrapper _gitHubClientWrapper;
    private IModelHolder _modelHolder;
    private ILogger? _logger;

    public Predictor(GitHubClientWrapper gitHubClientWrapper, IModelHolder modelHolder, ILogger? logger = null)
    {
        _gitHubClientWrapper = gitHubClientWrapper;
        _modelHolder = modelHolder;
        _logger = logger;
    }

    public async Task<LabelSuggestion> Predict(string owner, string repo, int number)
    {
        var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
        bool isPr = iop.PullRequest != null;

        string body = iop.Body ?? string.Empty;
        var userMentions = _userMentionRegex.Matches(body).Select(x => x.Value).ToArray();

        LabelSuggestion labelSuggestion;

        if (isPr && !_modelHolder.UseIssuesForPrsToo)
        {
            var prModel = await LoadPullRequestModel(owner, repo, iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
            labelSuggestion = Predictor.Predict(prModel, _logger, _modelHolder);

            if (_logger is not null)
            {
                _logger.LogInformation("PR label predictions: " + string.Join(",", labelSuggestion.LabelScores.Select(x => x.LabelName)));
            }

            return labelSuggestion;
        }

        var issueModel = LoadIssueModel(iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
        labelSuggestion = Predictor.Predict(issueModel, _logger, _modelHolder);

        if (_logger is not null)
        {
            _logger.LogInformation("Issue label predictions: " + string.Join(",", labelSuggestion.LabelScores.Select(x => x.LabelName)));
        }

        return labelSuggestion;
    }

    public static LabelSuggestion Predict(GitHubIssue issue, ILogger? logger, IModelHolder modelHolder)
    {
        return Predict(issue, modelHolder.IssuePredEngine, logger);
    }

    public static LabelSuggestion Predict(GitHubPullRequest issue, ILogger? logger, IModelHolder modelHolder)
    {
        if (modelHolder.UseIssuesForPrsToo)
        {
            return Predict(issue, modelHolder.IssuePredEngine, logger);
        }

        return Predict(issue, modelHolder.PrPredEngine, logger);
    }

    private static LabelSuggestion Predict<T>(T issueOrPr, PredictionEngine<T, GitHubIssuePrediction> predEngine, ILogger? logger) where T : GitHubIssue
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

        if (logger is not null)
        {
            logger.LogInformation($"# {maxProbability} {prediction.Area} for #{issueOrPr.Number} {issueOrPr.Title}");
        }

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

    private GitHubIssue LoadIssueModel(int number, string title, string? body, string[] userMentions, string author)
    {
        return new GitHubIssue()
        {
            Number = number,
            Title = title,
            Description = body ?? "",
            IsPR = 0,
            Author = author,
            UserMentions = string.Join(' ', userMentions),
            NumMentions = userMentions.Length
        };
    }

    private async Task<GitHubPullRequest> LoadPullRequestModel(string owner, string repo, int number, string title, string? body, string[] userMentions, string author)
    {
        var pr = new GitHubPullRequest()
        {
            Number = number,
            Title = title,
            Description = body ?? "",
            IsPR = 1,
            Author = author,
            UserMentions = string.Join(' ', userMentions),
            NumMentions = userMentions.Length,
        };

        IReadOnlyList<PullRequestFile> prFiles = await _gitHubClientWrapper.GetPullRequestFiles(owner, repo, number);
        if (prFiles.Count != 0)
        {
            string[] filePaths = prFiles.Select(x => x.FileName).ToArray();
            var segmentedDiff = DiffHelper.SegmentDiff(filePaths);
            pr.Files = string.Join(' ', segmentedDiff.FileDiffs);
            pr.Filenames = string.Join(' ', segmentedDiff.Filenames);
            pr.FileExtensions = string.Join(' ', segmentedDiff.Extensions);
            pr.Folders = DiffHelper.FlattenWithWhitespace(segmentedDiff.Folders);
            pr.FolderNames = DiffHelper.FlattenWithWhitespace(segmentedDiff.FolderNames);
        }
        pr.FileCount = prFiles.Count;
        return pr;
    }

    private struct IndexedScore
    {
        public IndexedScore(int index, float score) => (Index, Score) = (index, score);

        public int Index { get; }
        public float Score { get; }
    }
}
