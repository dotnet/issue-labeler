// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string? Org { get; set; }
    public List<string> Repos { get; set; }
    public string? GithubToken { get; set; }
    public string? IssueDataPath { get; set; }
    public string? IssueModelPath { get; set; }
    public int? IssueLimit { get; set; }
    public string? PullDataPath { get; set; }
    public string? PullModelPath { get; set; }
    public int? PullLimit { get; set; }
    public float? Threshold { get; set; }
    public Predicate<string> LabelPredicate { get; set; }

    static void ShowUsage(string? message = null)
    {
        // The entire condition is used to determine if the configuration is invalid.
        // If any of the following are true, the configuration is considered invalid:
        // • The LabelPredicate is null.
        // • Both IssueDataPath and PullDataPath are null, and either Org, Repos, or GithubToken is null.
        // • Both IssueModelPath and PullModelPath are null.

        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{(message is null ? "" : " " + message)}

            Usage:
              {{executableName}} --repo {org/repo1}[,{org/repo2},...] --label-prefix {label-prefix} [options]

                Required arguments:
                  --repo              The GitHub repositories in format org/repo (comma separated for multiple).
                  --label-prefix      Prefix for label predictions.

                Required for testing the issue model:
                  --issue-data        Path to existing issue data file (TSV file).
                  --issue-model       Path to existing issue prediction model file (ZIP file).

                Required for testing the pull request model:
                  --pull-data         Path to existing pull request data file (TSV file).
                  --pull-model        Path to existing pull request prediction model file (ZIP file).

                Optional arguments:
                  --threshold         Minimum prediction confidence threshold. Range (0,1]. Default 0.4.
                  --issue-limit       Maximum number of issues to download. Default: No limit.
                  --pull-limit        Maximum number of pull requests to download. Default: No limit.
                  --token             GitHub access token. Default: read from GITHUB_TOKEN env var.
            """);


        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args config = new()
        {
            Threshold = 0.4f
        };

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    string? gitHubToken = ArgUtils.Dequeue(arguments);

                    if (gitHubToken is null)
                    {
                        ShowUsage("Argument '--token' has an empty value.");
                        return null;
                    }

                    config.GithubToken = gitHubToken;
                    break;

                case "--repo":
                    string? orgRepos = ArgUtils.Dequeue(arguments);

                    if (orgRepos is null)
                    {
                        ShowUsage("Argument '--repo' has an empty value.");
                        return null;
                    }

                    foreach (var orgRepo in orgRepos.Split(',').Select(r => r.Trim()))
                    {
                        if (!orgRepo.Contains('/'))
                        {
                            ShowUsage($"Argument '--repo' is not in the format of '{{org}}/{{repo}}': {orgRepo}");
                            return null;
                        }

                        string[] parts = orgRepo.Split('/');

                        if (config.Org is not null && config.Org != parts[0])
                        {
                            ShowUsage("All '--repo' values must be from the same org.");
                            return null;
                        }

                        config.Org ??= parts[0];
                        config.Repos ??= [];
                        config.Repos.Add(parts[1]);
                    }
                    break;

                case "--issue-data":
                    config.IssueDataPath = ArgUtils.DequeuePath(arguments);

                    if (config.IssueDataPath is null)
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--issue-model":
                    config.IssueModelPath = ArgUtils.Dequeue(arguments);

                    if (config.IssueModelPath is null)
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }
                    break;

                case "--issue-limit":
                    config.IssueLimit = ArgUtils.DequeueInt(arguments);

                    if (config.IssueLimit is null)
                    {
                        ShowUsage("Argument '--issue-limit' has an empty or invalid value.");
                        return null;
                    }
                    break;

                case "--pull-data":
                    config.PullDataPath = ArgUtils.DequeuePath(arguments);

                    if (config.PullDataPath is null)
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-model":
                    config.PullModelPath = ArgUtils.Dequeue(arguments);

                    if (config.PullModelPath is null)
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-limit":
                    config.PullLimit = ArgUtils.DequeueInt(arguments);

                    if (config.PullLimit is null)
                    {
                        ShowUsage("Argument '--pull-limit' has an empty or invalid value.");
                        return null;
                    }
                    break;

                case "--label-prefix":
                    string? labelPrefix = ArgUtils.Dequeue(arguments);

                    if (labelPrefix is null)
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    config.LabelPredicate = (label) => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;

                case "--threshold":
                    float? threshold = ArgUtils.DequeueFloat(arguments);

                    if (threshold is null)
                    {
                        ShowUsage($"Argument '--threshold' has an empty or invalid value.");
                        return null;
                    }
                    config.Threshold = threshold.Value;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (config.LabelPredicate is null ||
            (
                config.IssueDataPath is null && config.PullDataPath is null &&
                (config.Org is null || config.Repos.Count == 0 || config.GithubToken is null)
            ) ||
            (config.IssueModelPath is null && config.PullModelPath is null)
        )
        {
            ShowUsage();
            return null;
        }

        return config;
    }
}
