// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string Org { get; set; }
    public string Repo { get; set; }
    public string GithubToken { get; set; }
    public string? IssueModelPath { get; set; }
    public List<ulong>? IssueNumbers { get; set; }
    public string? PullModelPath { get; set; }
    public List<ulong>? PullNumbers { get; set; }
    public float Threshold { get; set; }
    public Func<string, bool> LabelPredicate { get; set; }
    public string? DefaultLabel { get; set; }
    public int[] Retries { get; set; }
    public bool Verbose { get; set; }
    public string[]? ExcludedAuthors { get; set; }
    public bool Test { get; set; }

    static void ShowUsage(string? message = null)
    {
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} --repo {org/repo} --label-prefix {label-prefix} [options]

                Required arguments:
                  --repo              GitHub repository in the format {org}/{repo}.
                  --label-prefix      Prefix for label predictions. Must end with a character other than a letter or number.

                Required for predicting issue labels:
                  --issue-model       Path to existing issue prediction model file (ZIP file).
                  --issue-numbers     Comma-separated list of issue number ranges. Example: 1-3,7,5-9.

                Required for predicting pull request labels:
                  --pull-model        Path to existing pull request prediction model file (ZIP file).
                  --pull-numbers      Comma-separated list of pull request number ranges. Example: 1-3,7,5-9.

                Optional arguments:
                  --default-label     Default label to use if no label is predicted.
                  --threshold         Minimum prediction confidence threshold. Range (0,1]. Default 0.4.
                  --retries           Comma-separated retry delays in seconds. Default: 30,30,300,300,3000,3000.
                  --excluded-authors  Comma-separated list of authors to exclude.
                  --token             GitHub token. Default: read from GITHUB_TOKEN env var.
                  --test              Run in test mode, outputting predictions without applying labels.
                  --verbose           Enable verbose output.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args argsData = new()
        {
            Threshold = 0.4f,
            Retries = [30, 30, 300, 300, 3000, 3000]
        };

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    if (!ArgUtils.TryDequeueString(arguments, ShowUsage, "--token", out string? token))
                    {
                        return null;
                    }
                    argsData.GithubToken = token;
                    break;

                case "--repo":
                    if (!ArgUtils.TryDequeueRepo(arguments, ShowUsage, "--repo", out string? org, out string? repo))
                    {
                        return null;
                    }
                    argsData.Org = org;
                    argsData.Repo = repo;
                    break;

                case "--issue-model":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--issue-model", out string? issueModelPath))
                    {
                        return null;
                    }
                    argsData.IssueModelPath = issueModelPath;
                    break;

                case "--issue-numbers":
                    if (!ArgUtils.TryDequeueNumberRanges(arguments, ShowUsage, "--issue-numbers", out List<ulong>? issueNumbers))
                    {
                        return null;
                    }
                    argsData.IssueNumbers = issueNumbers;
                    break;

                case "--pull-model":
                    if (!ArgUtils.TryDequeuePath(arguments, ShowUsage, "--pull-model", out string? pullModelPath))
                    {
                        return null;
                    }
                    argsData.PullModelPath = pullModelPath;
                    break;

                case "--pull-numbers":
                    if (!ArgUtils.TryDequeueNumberRanges(arguments, ShowUsage, "--pull-numbers", out List<ulong>? pullNumbers))
                    {
                        return null;
                    }
                    argsData.PullNumbers = pullNumbers;
                    break;

                case "--label-prefix":
                    if (!ArgUtils.TryDequeueLabelPrefix(arguments, ShowUsage, "--label-prefix", out Func<string, bool>? labelPredicate))
                    {
                        return null;
                    }
                    argsData.LabelPredicate = labelPredicate;
                    break;

                case "--threshold":
                    if (!ArgUtils.TryDequeueFloat(arguments, ShowUsage, "--threshold", out float? threshold))
                    {
                        return null;
                    }
                    argsData.Threshold = threshold.Value;
                    break;

                case "--default-label":
                    if (!ArgUtils.TryDequeueString(arguments, ShowUsage, "--default-label", out string? defaultLabel))
                    {
                        return null;
                    }
                    argsData.DefaultLabel = defaultLabel;
                    break;

                case "--retries":
                    if (!ArgUtils.TryDequeueIntArray(arguments, ShowUsage, "--retries", out int[]? retries))
                    {
                        return null;
                    }
                    argsData.Retries = retries;
                    break;

                case "--excluded-authors":
                    if (!ArgUtils.TryDequeueStringArray(arguments, ShowUsage, "--excluded-authors", out string[]? excludedAuthors))
                    {
                        return null;
                    }
                    argsData.ExcludedAuthors = excludedAuthors;
                    break;

                case "--test":
                    argsData.Test = true;
                    break;

                case "--verbose":
                    argsData.Verbose = true;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        // Check if any required argsDatauration properties are missing or invalid.
        // The conditions are:
        // - Org is null
        // - Repo is null
        // - gitHubToken is null and the environment variable was not set
        // - Threshold is 0
        // - LabelPredicate is null
        // - IssueModelPath is null while IssueNumbers is not null, or vice versa
        // - PullModelPath is null while PullNumbers is not null, or vice versa
        // - Both IssueModelPath and PullModelPath are null
        if (argsData.Org is null || argsData.Repo is null || argsData.Threshold == 0 || argsData.LabelPredicate is null ||
            (argsData.IssueModelPath is null != argsData.IssueNumbers is null) ||
            (argsData.PullModelPath is null != argsData.PullNumbers is null) ||
            (argsData.IssueModelPath is null && argsData.PullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        if (argsData.GithubToken is null)
        {
            string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (string.IsNullOrEmpty(token))
            {
                ShowUsage("Argument '--token' not specified and environment variable GITHUB_TOKEN is empty.");
                return null;
            }

            argsData.GithubToken = token;
        }

        return argsData;
    }
}
