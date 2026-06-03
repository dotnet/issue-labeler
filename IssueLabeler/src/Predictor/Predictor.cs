// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Extensions;
using Actions.Core.Services;
using Actions.Core.Summaries;
using GitHubClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;

using var provider = new ServiceCollection()
    .AddGitHubActionsCore()
    .BuildServiceProvider();

var action = provider.GetRequiredService<ICoreService>();
if (Args.Parse(args, action) is not Args argsData) return 1;

List<Task<(ulong Number, string ResultMessage, bool Success)>> tasks = new();

if (argsData.IssuesModelPath is not null && argsData.Issues is not null)
{
    await action.WriteStatusAsync($"Loading prediction engine for issues model...");
    var issueContext = new MLContext();
    var issueModel = issueContext.Model.Load(argsData.IssuesModelPath, out _);
    var issuePredictor = issueContext.Model.CreatePredictionEngine<Issue, LabelPrediction>(issueModel);
    await action.WriteStatusAsync($"Issues prediction engine ready.");

    foreach (ulong issueNumber in argsData.Issues)
    {
        var result = await GitHubApi.GetIssue(argsData.GitHubToken, argsData.Org, argsData.Repo, issueNumber, argsData.Retries, action, argsData.Verbose);

        if (result is null)
        {
            action.WriteNotice($"[Issue {argsData.Org}/{argsData.Repo}#{issueNumber}] could not be found or downloaded. Skipped.");
            continue;
        }

        if (argsData.ExcludedAuthors is not null && result.Author?.Login is not null && argsData.ExcludedAuthors.Contains(result.Author.Login, StringComparer.InvariantCultureIgnoreCase))
        {
            action.WriteNotice($"[Issue {argsData.Org}/{argsData.Repo}#{issueNumber}] Author '{result.Author.Login}' is in excluded list. Skipped.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            issuePredictor,
            issueNumber,
            new Issue(result),
            argsData.LabelPredicate,
            argsData.DefaultLabel,
            argsData.MaxLabels,
            ModelType.Issue,
            argsData.Retries,
            argsData.Test
        )));

        action.WriteInfo($"[Issue {argsData.Org}/{argsData.Repo}#{issueNumber}] Queued for prediction.");
    }
}

if (argsData.PullsModelPath is not null && argsData.Pulls is not null)
{
    await action.WriteStatusAsync($"Loading prediction engine for pulls model...");
    var pullContext = new MLContext();
    var pullModel = pullContext.Model.Load(argsData.PullsModelPath, out _);
    var pullPredictor = pullContext.Model.CreatePredictionEngine<PullRequest, LabelPrediction>(pullModel);
    await action.WriteStatusAsync($"Pulls prediction engine ready.");

    foreach (ulong pullNumber in argsData.Pulls)
    {
        var result = await GitHubApi.GetPullRequest(argsData.GitHubToken, argsData.Org, argsData.Repo, pullNumber, argsData.Retries, action, argsData.Verbose);

        if (result is null)
        {
            action.WriteNotice($"[Pull Request {argsData.Org}/{argsData.Repo}#{pullNumber}] could not be found or downloaded. Skipped.");
            continue;
        }

        if (argsData.ExcludedAuthors is not null && result.Author?.Login is not null && argsData.ExcludedAuthors.Contains(result.Author.Login))
        {
            action.WriteNotice($"[Pull Request {argsData.Org}/{argsData.Repo}#{pullNumber}] Author '{result.Author.Login}' is in excluded list. Skipped.");
            continue;
        }

        tasks.Add(Task.Run(() => ProcessPrediction(
            pullPredictor,
            pullNumber,
            new PullRequest(result),
            argsData.LabelPredicate,
            argsData.DefaultLabel,
            argsData.MaxLabels,
            ModelType.PullRequest,
            argsData.Retries,
            argsData.Test
        )));

        action.WriteInfo($"[Pull Request {argsData.Org}/{argsData.Repo}#{pullNumber}] Queued for prediction.");
    }
}

var (predictionResults, success) = await App.RunTasks(tasks, action);

foreach (var prediction in predictionResults.OrderBy(p => p.Number))
{
    action.WriteInfo(prediction.ResultMessage);
}

await action.Summary.WritePersistentAsync();
return success ? 0 : 1;

async Task<(ulong Number, string ResultMessage, bool Success)> ProcessPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, ulong number, T issueOrPull, Func<string, bool> labelPredicate, string? defaultLabel, int maxLabels, ModelType type, int[] retries, bool test) where T : Issue
{
    List<Action<Summary>> predictionResults = [];
    string typeName = type == ModelType.PullRequest ? "Pull Request" : "Issue";
    List<string> resultMessageParts = [];
    string? error = null;

    (ulong, string, bool) GetResult(bool success)
    {
        foreach (var summaryWrite in predictionResults)
        {
            action.Summary.AddPersistent(summaryWrite);
        }

        return (number, $"[{typeName} {argsData.Org}/{argsData.Repo}#{number}] {string.Join(' ', resultMessageParts)}", success);
    }

    (ulong, string, bool) Success() => GetResult(true);
    (ulong, string, bool) Failure() => GetResult(false);

    predictionResults.Add(summary => summary.AddRawMarkdown($"- **{argsData.Org}/{argsData.Repo}#{number}**", true));

    if (issueOrPull.HasMoreLabels)
    {
        predictionResults.Add(summary => summary.AddRawMarkdown($"    - Skipping prediction. Too many labels applied already; cannot be sure no applicable label is already applied.", true));
        resultMessageParts.Add("Too many labels applied already.");

        return Success();
    }

    var applicableLabel = issueOrPull.Labels?.FirstOrDefault(labelPredicate);

    bool hasDefaultLabel =
        (defaultLabel is not null) &&
        (issueOrPull.Labels?.Any(l => l.Equals(defaultLabel, StringComparison.OrdinalIgnoreCase)) ?? false);

    if (applicableLabel is not null)
    {
        predictionResults.Add(summary => summary.AddRawMarkdown($"    - No prediction needed. Applicable label `{applicableLabel}` already exists.", true));

        if (hasDefaultLabel && defaultLabel is not null)
        {
            if (!test)
            {
                error = await GitHubApi.RemoveLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, defaultLabel, retries, action);
            }

            if (error is null)
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - Removed default label `{defaultLabel}`.", true));
                resultMessageParts.Add($"Default label '{defaultLabel}' removed.");
                return Success();
            }
            else
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **Error removing default label `{defaultLabel}`**: {error}", true));
                resultMessageParts.Add($"Error occurred removing default label '{defaultLabel}': {error}");
                return Failure();
            }
        }

        resultMessageParts.Add($"No prediction needed. Applicable label '{applicableLabel}' already exists.");
        return Success();
    }

    var prediction = predictor.Predict(issueOrPull);

    if (prediction.Score is null || prediction.Score.Length == 0)
    {
        predictionResults.Add(summary => summary.AddRawMarkdown($"    - No prediction was made. The prediction engine did not return any possible predictions.", true));
        resultMessageParts.Add("No prediction was made. The prediction engine did not return any possible predictions.");
        return Success();
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
        // Capture the top 3 predictions for output.
        .OrderByDescending(p => p.Score)
        .Take(3)
        .ToList();

    var topLabels = predictions.Where(p => p.Score >= argsData.Threshold).Take(maxLabels).ToList();

    if (topLabels.Count > 0)
    {
        predictionResults.Add(summary => summary.AddRawMarkdown($"    - {topLabels.Count} label(s) meet the threshold of {argsData.Threshold}.", true));
    }
    else
    {
        predictionResults.Add(summary => summary.AddRawMarkdown($"    - No label prediction met the threshold of {argsData.Threshold}.", true));
    }

    foreach (var labelPrediction in predictions)
    {
        predictionResults.Add(summary => summary.AddRawMarkdown($"        - `{labelPrediction.Label}` - Score: {labelPrediction.Score}", true));
    }

    if (topLabels.Count > 0)
    {
        foreach (var labelToApply in topLabels)
        {
            error = null;
            if (!test)
            {
                error = await GitHubApi.AddLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, labelToApply.Label, retries, action);
            }

            if (error is null)
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **`{labelToApply.Label}` applied**", true));
                resultMessageParts.Add($"Label '{labelToApply.Label}' applied.");
            }
            else
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **Error applying label `{labelToApply.Label}`**: {error}", true));
                resultMessageParts.Add($"Error occurred applying label '{labelToApply.Label}': {error}");
                return Failure();
            }
        }

        if (hasDefaultLabel && defaultLabel is not null)
        {
            error = null;
            if (!test)
            {
                error = await GitHubApi.RemoveLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, defaultLabel, retries, action);
            }

            if (error is null)
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **Removed default label `{defaultLabel}`**", true));
                resultMessageParts.Add($"Default label '{defaultLabel}' removed.");
            }
            else
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **Error removing default label `{defaultLabel}`**: {error}", true));
                resultMessageParts.Add($"Error occurred removing default label '{defaultLabel}': {error}");
                return Failure();
            }
        }

        return Success();
    }

    if (defaultLabel is not null)
    {
        if (hasDefaultLabel)
        {
            predictionResults.Add(summary => summary.AddRawMarkdown($"    - Default label `{defaultLabel}` is already applied.", true));
            resultMessageParts.Add($"No prediction made. Default label '{defaultLabel}' is already applied.");
            return Success();
        }
        else
        {
            if (!test)
            {
                error = await GitHubApi.AddLabel(argsData.GitHubToken, argsData.Org, argsData.Repo, typeName, number, defaultLabel, retries, action);
            }

            if (error is null)
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **Default label `{defaultLabel}` applied.**", true));
                resultMessageParts.Add($"No prediction made. Default label '{defaultLabel}' applied.");
                return Success();
            }
            else
            {
                predictionResults.Add(summary => summary.AddRawMarkdown($"    - **Error applying default label `{defaultLabel}`**: {error}", true));
                resultMessageParts.Add($"Error occurred applying default label '{defaultLabel}': {error}");
                return Failure();
            }
        }
    }

    resultMessageParts.Add("No prediction made. No applicable label found. No action taken.");
    return GetResult(error is null);
}
