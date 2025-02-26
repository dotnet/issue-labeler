// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;
using GitHubClient;

var arguments = Args.Parse(args);
if (arguments is null) return;
(
    string org,
    string repo,
    string githubToken,
    string? issueModelPath,
    List<ulong>? issueNumbers,
    string? pullModelPath,
    List<ulong>? pullNumbers,
    float threshold,
    Func<string, bool> labelPredicate,
    string? defaultLabel,
    bool test
) = arguments.Value;

List<Task<(ModelType Type, ulong Number, bool Success, string[] Output)>> tasks = new();

if (issueModelPath is not null && issueNumbers is not null)
{
    Console.WriteLine("Loading issues model...");
    var issueContext = new MLContext();
    var issueModel = issueContext.Model.Load(issueModelPath, out _);
    var issuePredictor = issueContext.Model.CreatePredictionEngine<Issue, LabelPrediction>(issueModel);
    Console.WriteLine("Issues prediction engine ready.");

    foreach (ulong issueNumber in issueNumbers)
    {
        var result = await GitHubApi.GetIssue(githubToken, org, repo, issueNumber);

        if (result is null)
        {
            Console.WriteLine($"[Issue #{issueNumber}] Issue not found.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            issuePredictor,
            issueNumber,
            new Issue(result),
            labelPredicate,
            defaultLabel,
            ModelType.Issue,
            test
        )));
    }
}

if (pullModelPath is not null && pullNumbers is not null)
{
    Console.WriteLine("Loading pulls model...");
    var pullContext = new MLContext();
    var pullModel = pullContext.Model.Load(pullModelPath, out _);
    var pullPredictor = pullContext.Model.CreatePredictionEngine<PullRequest, LabelPrediction>(pullModel);
    Console.WriteLine("Pulls prediction engine ready.");

    foreach (ulong pullNumber in pullNumbers)
    {
        var result = await GitHubApi.GetPullRequest(githubToken, org, repo, pullNumber);

        if (result is null)
        {
            Console.WriteLine($"[Pull Request #{pullNumber}] Pull request not found.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            pullPredictor,
            pullNumber,
            new PullRequest(result),
            labelPredicate,
            defaultLabel,
            ModelType.PullRequest,
            test
        )));
    }
}

var allTasks = Task.WhenAll(tasks);

try
{
    allTasks.Wait();
}
catch (AggregateException) { }

foreach (var prediction in allTasks.Result)
{
    Console.WriteLine($"""
        [{prediction.Type} #{prediction.Number}{(prediction.Success ? "" : " FAILURE")}]
          {string.Join("\n  ", prediction.Output)}

        """);
}

async Task<(ModelType, ulong, bool, string[])> ProcessPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, ulong number, T issueOrPull, Func<string, bool> labelPredicate, string? defaultLabel, ModelType type, bool test) where T : Issue
{
    List<string> output = new();
    string? error = null;

    if (issueOrPull.HasMoreLabels)
    {
        output.Add($"[{type} #{number}] No action taken. Too many labels applied already; cannot be sure no applicable label is already applied.");
        return (type, number, true, output.ToArray());
    }

    var applicableLabel = issueOrPull.Labels?.FirstOrDefault(labelPredicate);

    bool hasDefaultLabel =
        (defaultLabel is not null) &&
        (issueOrPull.Labels?.Any(l => l.Equals(defaultLabel, StringComparison.OrdinalIgnoreCase)) ?? false);

    if (applicableLabel is not null)
    {
        output.Add($"Applicable label '{applicableLabel}' already exists.");

        if (hasDefaultLabel && defaultLabel is not null)
        {
            if (!test)
            {
                error = await GitHubApi.RemoveLabel(githubToken, org, repo, type.ToString(), number, defaultLabel);
            }

            output.Add(error ?? $"Removed default label '{defaultLabel}'.");
        }

        return (type, number, error is null, output.ToArray());
    }

    var prediction = predictor.Predict(issueOrPull);

    if (prediction.Score is null || prediction.Score.Length == 0)
    {
        output.Add("No prediction was made.");
        return (type, number, true, output.ToArray());
    }

    VBuffer<ReadOnlyMemory<char>> labels = default;
    predictor.OutputSchema[nameof(LabelPrediction.Score)].GetSlotNames(ref labels);

    var predictions = prediction.Score
        .Select((score, index) => new
        {
            Score = score,
            Label = labels.GetItemOrDefault(index).ToString()
        })
        // Ensure predicted labels match the expected predicate
        .Where(prediction => labelPredicate(prediction.Label))
        // Capture the top 3 for including in the output
        .OrderByDescending(p => p.Score)
        .Take(3);

    output.Add("Label predictions:");
    output.AddRange(predictions.Select(p => $"  '{p.Label}' - Score: {p.Score}"));

    var bestScore = predictions.FirstOrDefault(p => p.Score >= threshold);
    output.Add(bestScore is not null ?
        $"Label '{bestScore.Label}' meets threshold of {threshold}." :
        $"No label meets the threshold of {threshold}.");

    if (bestScore is not null)
    {
        if (!test)
        {
            error = await GitHubApi.AddLabel(githubToken, org, repo, type.ToString(), number, bestScore.Label);
        }

        output.Add(error ?? $"Added label '{bestScore.Label}'");

        if (error is not null)
        {
            return (type, number, false, output.ToArray());
        }

        if (hasDefaultLabel && defaultLabel is not null)
        {
            if (!test)
            {
                error = await GitHubApi.RemoveLabel(githubToken, org, repo, type.ToString(), number, defaultLabel);
            }

            output.Add(error ?? $"Removed default label '{defaultLabel}'");
        }

        return (type, number, error is null, output.ToArray());
    }

    if (defaultLabel is not null)
    {
        if (hasDefaultLabel)
        {
            output.Add($"Default label '{defaultLabel}' is already applied.");
        }
        else
        {
            if (!test)
            {
                error = await GitHubApi.AddLabel(githubToken, org, repo, type.ToString(), number, defaultLabel);
            }

            output.Add(error ?? $"Applied default label '{defaultLabel}'.");
        }
    }

    return (type, number, error is null, output.ToArray());
}
