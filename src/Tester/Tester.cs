// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;
using GitHubClient;

var config = Args.Parse(args);
if (config is not Args argsData) return;

List<Task> tasks = [];

if (argsData.IssueModelPath is not null)
{
    tasks.Add(Task.Run(() => TestIssues()));
}

if (argsData.PullModelPath is not null)
{
    tasks.Add(Task.Run(() => TestPullRequests()));
}

await Task.WhenAll(tasks);

async IAsyncEnumerable<T> ReadData<T>(string dataPath, Func<ulong, string[], T> readLine, int? rowLimit)
{
    var allLines = File.ReadLinesAsync(dataPath);
    ulong rowNum = 0;
    rowLimit ??= 50000;

    await foreach (var line in allLines)
    {
        // Skip the header row
        if (rowNum == 0)
        {
            rowNum++;
            continue;
        }

        string[] columns = line.Split('\t');
        yield return readLine(rowNum, columns);

        if ((int)rowNum++ >= rowLimit)
        {
            break;
        }
    }
}

async IAsyncEnumerable<Issue> DownloadIssues(string githubToken, string org, string repo)
{
    await foreach (var result in GitHubApi.DownloadIssues(githubToken, org, repo, argsData.LabelPredicate, argsData.IssueLimit, 100, 1000, [30, 30, 30], argsData.ExcludedAuthors ?? []))
    {
        yield return new(result.Issue, argsData.LabelPredicate);
    }
}

async Task TestIssues()
{
    if (argsData.IssueDataPath is not null)
    {
        var issueList = ReadData(argsData.IssueDataPath, (num, columns) => new Issue()
        {
            Number = num,
            Label = columns[0],
            Title = columns[1],
            Body = columns[2]
        }, argsData.IssueLimit);

        await TestPredictions(issueList, argsData.IssueModelPath);
        return;
    }

    if (argsData.GithubToken is not null && argsData.Org is not null && argsData.Repos is not null)
    {
        foreach (var repo in argsData.Repos)
        {
            Console.WriteLine($"Downloading and testing issues from {argsData.Org}/{repo}.");

            var issueList = DownloadIssues(argsData.GithubToken, argsData.Org, repo);
            await TestPredictions(issueList, argsData.IssueModelPath);
        }
    }
}

async IAsyncEnumerable<PullRequest> DownloadPullRequests(string githubToken, string org, string repo)
{
    await foreach (var result in GitHubApi.DownloadPullRequests(githubToken, org, repo, argsData.LabelPredicate, argsData.PullLimit, 25, 4000, [30, 30, 30], argsData.ExcludedAuthors ?? []))
    {
        yield return new(result.PullRequest, argsData.LabelPredicate);
    }
}

async Task TestPullRequests()
{
    if (argsData.PullDataPath is not null)
    {
        var pullList = ReadData(argsData.PullDataPath, (num, columns) => new PullRequest()
        {
            Number = num,
            Label = columns[0],
            Title = columns[1],
            Body = columns[2],
            FileNames = columns[3],
            FolderNames = columns[4]
        }, argsData.PullLimit);

        await TestPredictions(pullList, argsData.PullModelPath);
        return;
    }

    if (argsData.GithubToken is not null && argsData.Org is not null && argsData.Repos is not null)
    {
        foreach (var repo in argsData.Repos)
        {
            Console.WriteLine($"Downloading and testing pull requests from {argsData.Org}/{repo}.");

            var pullList = DownloadPullRequests(argsData.GithubToken, argsData.Org, repo);
            await TestPredictions(pullList, argsData.PullModelPath);
        }
    }
}

static string GetStats(List<float> values)
{
    if (values.Count == 0)
    {
        return "N/A";
    }

    float min = values.Min();
    float average = values.Average();
    float max = values.Max();
    double deviation = Math.Sqrt(values.Average(v => Math.Pow(v - average, 2)));

    return $"{min} | {average} | {max} | {deviation}";
}

async Task TestPredictions<T>(IAsyncEnumerable<T> results, string modelPath) where T : Issue
{
    var context = new MLContext();
    var model = context.Model.Load(modelPath, out _);
    var predictor = context.Model.CreatePredictionEngine<T, LabelPrediction>(model);
    var itemType = typeof(T) == typeof(PullRequest) ? "Pull Request" : "Issue";

    int matches = 0;
    int mismatches = 0;
    int noPrediction = 0;
    int noExisting = 0;

    List<float> matchScores = [];
    List<float> mismatchScores = [];

    await foreach (var result in results)
    {
        (string? predictedLabel, float? score) = GetPrediction(
            predictor,
            result,
            argsData.Threshold);

        if (predictedLabel is null && result.Label is not null)
        {
            noPrediction++;
        }
        else if (predictedLabel is not null && result.Label is null)
        {
            noExisting++;
        }
        else if (predictedLabel?.ToLower() == result.Label?.ToLower())
        {
            matches++;

            if (score.HasValue)
            {
                matchScores.Add(score.Value);
            }
        }
        else
        {
            mismatches++;

            if (score.HasValue)
            {
                mismatchScores.Add(score.Value);
            }
        }

        float total = matches + mismatches + noPrediction + noExisting;
        Console.WriteLine($"{itemType} #{result.Number} - Predicted: {(predictedLabel ?? "<NONE>")} - Existing: {(result.Label ?? "<NONE>")}");
        Console.WriteLine($"  Matches      : {matches} ({(float)matches / total:P2}) - Min | Avg | Max | StdDev: {GetStats(matchScores)}");
        Console.WriteLine($"  Mismatches   : {mismatches} ({(float)mismatches / total:P2}) - Min | Avg | Max | StdDev: {GetStats(mismatchScores)}");
        Console.WriteLine($"  No Prediction: {noPrediction} ({(float)noPrediction / total:P2})");
        Console.WriteLine($"  No Existing  : {noExisting} ({(float)noExisting / total:P2})");
    }

    Console.WriteLine("Test Complete");
}

(string? PredictedLabel, float? PredictionScore) GetPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, T issueOrPull, float? threshold) where T : Issue
{
    var prediction = predictor.Predict(issueOrPull);
    var itemType = typeof(T) == typeof(PullRequest) ? "Pull Request" : "Issue";

    if (prediction.Score is null || prediction.Score.Length == 0)
    {
        Console.WriteLine($"No prediction was made for {itemType} #{issueOrPull.Number}.");
        return (null, null);
    }

    VBuffer<ReadOnlyMemory<char>> labels = default;
    predictor.OutputSchema[nameof(LabelPrediction.Score)].GetSlotNames(ref labels);

    var bestScore = prediction.Score
        .Select((score, index) => new
        {
            Score = score,
            Label = labels.GetItemOrDefault(index).ToString()
        })
        .OrderByDescending(p => p.Score)
        .FirstOrDefault(p => threshold is null || p.Score >= threshold);

    return bestScore is not null ? (bestScore.Label, bestScore.Score) : ((string?)null, (float?)null);
}
