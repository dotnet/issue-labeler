// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using static DataFileUtils;
using GitHubClient;

var arguments = Args.Parse(args);
if (arguments is null) return;
(
    string org,
    string[] repos,
    string githubToken,
    string? issuesPath,
    int? issueLimit,
    string? pullsPath,
    int? pullLimit,
    int? pageSize,
    int? pageLimit,
    int[] retries,
    Predicate<string> labelPredicate,
    bool verbose
) = arguments.Value;

List<Task> tasks = new();

if (!string.IsNullOrEmpty(issuesPath))
{
    EnsureOutputDirectory(issuesPath);
    tasks.Add(Task.Run(() => DownloadIssues(issuesPath)));
}

if (!string.IsNullOrEmpty(pullsPath))
{
    EnsureOutputDirectory(pullsPath);
    tasks.Add(Task.Run(() => DownloadPullRequests(pullsPath)));
}

await Task.WhenAll(tasks);

async Task DownloadIssues(string outputPath)
{
    Console.WriteLine($"Issues Data Path: {outputPath}");

    byte perFlushCount = 0;

    using StreamWriter writer = new StreamWriter(outputPath);
    writer.WriteLine(FormatIssueRecord("Label", "Title", "Body"));

    foreach (var repo in repos)
    {
        await foreach (var result in GitHubApi.DownloadIssues(githubToken, org, repo, labelPredicate, issueLimit, pageSize ?? 100, pageLimit ?? 1000, retries, verbose))
        {
            writer.WriteLine(FormatIssueRecord(result.Label, result.Issue.Title, result.Issue.Body));

            if (++perFlushCount == 100)
            {
                writer.Flush();
                perFlushCount = 0;
            }
        }
    }

    writer.Close();
}

async Task DownloadPullRequests(string outputPath)
{
    Console.WriteLine($"Pulls Data Path: {outputPath}");

    byte perFlushCount = 0;

    using StreamWriter writer = new StreamWriter(outputPath);
    writer.WriteLine(FormatPullRequestRecord("Label", "Title", "Body", ["FileNames"], ["FolderNames"]));

    foreach (var repo in repos)
    {
        await foreach (var result in GitHubApi.DownloadPullRequests(githubToken, org, repo, labelPredicate, pullLimit, pageSize ?? 25, pageLimit ?? 4000, retries, verbose))
        {
            writer.WriteLine(FormatPullRequestRecord(result.Label, result.PullRequest.Title, result.PullRequest.Body, result.PullRequest.FileNames, result.PullRequest.FolderNames));

            if (++perFlushCount == 100)
            {
                writer.Flush();
                perFlushCount = 0;
            }
        }
    }

    writer.Close();
}
