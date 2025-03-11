// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;
using GitHubClient;

var config = Args.Parse(args);
if (config is not Args argsData) return;

List<Task<(ModelType Type, ulong Number, bool Success, string[] Output)>> tasks = new();

if (argsData.IssueModelPath is not null && argsData.IssueNumbers is not null)
{
    Console.WriteLine("Loading issues model...");
    var issueContext = new MLContext();
    var issueModel = issueContext.Model.Load(argsData.IssueModelPath, out _);
    var issuePredictor = issueContext.Model.CreatePredictionEngine<Issue, LabelPrediction>(issueModel);
    Console.WriteLine("Issues prediction engine ready.");

    foreach (ulong issueNumber in argsData.IssueNumbers)
    {
        var result = await GitHubApi.GetIssue(argsData.GithubToken, argsData.Org, argsData.Repo, issueNumber, argsData.Retries, argsData.Verbose);

        if (result is null)
        {
            Console.WriteLine($"[Issue #{issueNumber}] could not be found or downloaded. Skipped.");
            continue;
        }

        if (argsData.ExcludedAuthors is not null && argsData.ExcludedAuthors.Contains(result.Author.Login, StringComparer.InvariantCultureIgnoreCase))
        {
            Console.WriteLine($"[Issue #{issueNumber}] Author '{result.Author.Login}' is in excluded list. Skipped.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            issuePredictor,
            issueNumber,
            new Issue(result),
            argsData.LabelPredicate,
            argsData.DefaultLabel,
            ModelType.Issue,
            argsData.Retries,
            argsData.Test
        )));

        Console.WriteLine($"[Issue #{issueNumber}] Queued for prediction.");
    }
}

if (argsData.PullModelPath is not null && argsData.PullNumbers is not null)
{
    Console.WriteLine("Loading pulls model...");
    var pullContext = new MLContext();
    var pullModel = pullContext.Model.Load(argsData.PullModelPath, out _);
    var pullPredictor = pullContext.Model.CreatePredictionEngine<PullRequest, LabelPrediction>(pullModel);
    Console.WriteLine("Pulls prediction engine ready.");

    foreach (ulong pullNumber in argsData.PullNumbers)
    {
        var result = await GitHubApi.GetPullRequest(argsData.GithubToken, argsData.Org, argsData.Repo, pullNumber, argsData.Retries, argsData.Verbose);

        if (result is null)
        {
            Console.WriteLine($"[Pull Request #{pullNumber}] could not be found or downloaded. Skipped.");
            continue;
        }

        if (argsData.ExcludedAuthors is not null && argsData.ExcludedAuthors.Contains(result.Author.Login))
        {
            Console.WriteLine($"[Pull Request #{pullNumber}] Author '{result.Author.Login}' is in excluded list. Skipped.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            pullPredictor,
            pullNumber,
            new PullRequest(result),
            argsData.LabelPredicate,
            argsData.DefaultLabel,
            ModelType.PullRequest,
            argsData.Retries,
            argsData.Test
        )));

        Console.WriteLine($"[Pull Request #{pullNumber}] Queued for prediction.");
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

async Task<(ModelType, ulong, bool, string[])> ProcessPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, ulong number, T issueOrPull, Func<string, bool> labelPredicate, string? defaultLabel, ModelType type, int[] retries, bool test) where T : Issue
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
                error = await GitHubApi.RemoveLabel(argsData.GithubToken, argsData.Org, argsData.Repo, type.ToString(), number, defaultLabel, argsData.Retries);
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

    var bestScore = predictions.FirstOrDefault(p => p.Score >= argsData.Threshold);
    output.Add(bestScore is not null ?
        $"Label '{bestScore.Label}' meets threshold of {argsData.Threshold}." :
        $"No label meets the threshold of {argsData.Threshold}.");

    if (bestScore is not null)
    {
        if (!test)
        {
            error = await GitHubApi.AddLabel(argsData.GithubToken, argsData.Org, argsData.Repo, type.ToString(), number, bestScore.Label, retries);
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
                error = await GitHubApi.RemoveLabel(argsData.GithubToken, argsData.Org, argsData.Repo, type.ToString(), number, defaultLabel, retries);
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
                error = await GitHubApi.AddLabel(argsData.GithubToken, argsData.Org, argsData.Repo, type.ToString(), number, defaultLabel, argsData.Retries);
            }

            output.Add(error ?? $"Applied default label '{defaultLabel}'.");
        }
    }

    return (type, number, error is null, output.ToArray());
}
