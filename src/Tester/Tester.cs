// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;
using GitHubClient;

var arguments = Args.Parse(args);
if (arguments is null) return;

(
    string? org,
    string[]? repos,
    string? githubToken,
    string? issueDataPath,
    string? issueModelPath,
    int? issueLimit,
    string? pullDataPath,
    string? pullModelPath,
    int? pullLimit,
    float? threshold,
    Predicate<string> labelPredicate
) = arguments.Value;

List<Task> tasks = new();

if (issueModelPath is not null)
{
    tasks.Add(Task.Run(() => TestIssues()));
}

if (pullModelPath is not null)
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
    await foreach (var result in GitHubApi.DownloadIssues(githubToken, org, repo, labelPredicate, issueLimit, 100, 1000, [30, 30, 30]))
    {
        yield return new(result.Issue, labelPredicate);
    }
}

async Task TestIssues()
{
    if (issueDataPath is not null)
    {
        var issueList = ReadData(issueDataPath, (num, columns) => new Issue()
        {
            Number = num,
            Label = columns[0],
            Title = columns[1],
            Body = columns[2]
        }, issueLimit);

        await TestPredictions(issueList, issueModelPath);
        return;
    }

    if (githubToken is not null && org is not null && repos is not null)
    {
        foreach (var repo in repos)
        {
            Console.WriteLine($"Downloading and testing issues from {org}/{repo}.");

            var issueList = DownloadIssues(githubToken, org, repo);
            await TestPredictions(issueList, issueModelPath);
        }
    }
}

async IAsyncEnumerable<PullRequest> DownloadPullRequests(string githubToken, string org, string repo)
{
    await foreach (var result in GitHubApi.DownloadPullRequests(githubToken, org, repo, labelPredicate, pullLimit, 25, 4000, [30, 30, 30]))
    {
        yield return new(result.PullRequest, labelPredicate);
    }
}

async Task TestPullRequests()
{
    if (pullDataPath is not null)
    {
        var pullList = ReadData(pullDataPath, (num, columns) => new PullRequest()
        {
            Number = num,
            Label = columns[0],
            Title = columns[1],
            Body = columns[2],
            FileNames = columns[3],
            FolderNames = columns[4]
        }, pullLimit);

        await TestPredictions(pullList, pullModelPath);
        return;
    }

    if (githubToken is not null && org is not null && repos is not null)
    {
        foreach (var repo in repos)
        {
            Console.WriteLine($"Downloading and testing pull requests from {org}/{repo}.");

            var pullList = DownloadPullRequests(githubToken, org, repo);
            await TestPredictions(pullList, pullModelPath);
        }
    }
}

static string GetStats(List<float> values)
{
    if (!values.Any())
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

    int matches = 0;
    int mismatches = 0;
    int noPrediction = 0;
    int noExisting = 0;

    List<float> matchScores = new();
    List<float> mismatchScores = new();

    await foreach (var result in results)
    {
        (string? predictedLabel, float? score) = GetPrediction(
            predictor,
            result,
            threshold,
            "Issue");

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
        Console.WriteLine($"Issue #{result.Number} - Predicted: {(predictedLabel ?? "<NONE>")} - Existing: {(result.Label ?? "<NONE>")}");
        Console.WriteLine($"  Matches      : {matches} ({(float)matches / total:P2}) - Min | Avg | Max | StdDev: {GetStats(matchScores)}");
        Console.WriteLine($"  Mismatches   : {mismatches} ({(float)mismatches / total:P2}) - Min | Avg | Max | StdDev: {GetStats(mismatchScores)}");
        Console.WriteLine($"  No Prediction: {noPrediction} ({(float)noPrediction / total:P2})");
        Console.WriteLine($"  No Existing  : {noExisting} ({(float)noExisting / total:P2})");
    }

    Console.WriteLine("Test Complete");
}

(string? PredictedLabel, float? PredictionScore) GetPrediction<T>(PredictionEngine<T, LabelPrediction> predictor, T issueOrPull, float? threshold, string itemType) where T : Issue
{
    var prediction = predictor.Predict(issueOrPull);

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
