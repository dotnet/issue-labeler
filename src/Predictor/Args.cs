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
                  --label-prefix      Prefix for label predictions.

                Required for predicting issue labels:
                  --issue-model       Path to existing issue prediction model file (ZIP file).
                  --issue-numbers     Comma-separated list of issue number ranges. Example: 1-3,7,5-9.

                Required for predicting pull request labels:
                  --pull-model        Path to existing pull request prediction model file (ZIP file).
                  --pull-numbers      Comma-separated list of pull request number ranges. Example: 1-3,7,5-9.

                Optional arguments:
                  --default-label     Default label to use if no label is predicted.
                  --threshold         Minimum prediction confidence threshold. Range (0,1]. Default 0.4.
                  --token             GitHub token. Default: read from GITHUB_TOKEN env var.
                  --test              Run in test mode, outputting predictions without applying labels.
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args config = new()
        {
            Threshold = 0.4f,
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
                    string? orgRepo = ArgUtils.Dequeue(arguments);

                    if (orgRepo is null || !orgRepo.Contains('/'))
                    {
                        ShowUsage($$"""Argument '--repo' is not in the format of '{org}/{repo}': {{orgRepo}}""");
                        return null;
                    }

                    string[] parts = orgRepo.Split('/');
                    config.Org = parts[0];
                    config.Repo = parts[1];
                    break;

                case "--issue-model":
                    config.IssueModelPath = ArgUtils.Dequeue(arguments);

                    if (config.IssueModelPath is null)
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }
                    break;

                case "--issue-numbers":
                    string? issueNums = ArgUtils.Dequeue(arguments);

                    if (issueNums is null)
                    {
                        ShowUsage($"Argument '--issue-numbers' has an empty value.");
                        return null;
                    }

                    config.IssueNumbers ??= new();
                    config.IssueNumbers.AddRange(ParseNumbers(issueNums));
                    break;

                case "--pull-model":
                    config.PullModelPath = ArgUtils.Dequeue(arguments);

                    if (config.PullModelPath is null)
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-numbers":
                    string? pullNums = ArgUtils.Dequeue(arguments);

                    if (pullNums is null)
                    {
                        ShowUsage($"Argument '--pull-numbers' has an empty value.");
                        return null;
                    }

                    config.PullNumbers ??= new();
                    config.PullNumbers.AddRange(ParseNumbers(pullNums));
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

                case "--default-label":
                    config.DefaultLabel = ArgUtils.Dequeue(arguments);

                    if (config.DefaultLabel is null)
                    {
                        ShowUsage("Argument '--default-label' has an empty value.");
                        return null;
                    }
                    break;

                case "--test":
                    config.Test = true;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        // Check if any required configuration properties are missing or invalid.
        // The conditions are:
        // - Org is null
        // - Repo is null
        // - gitHubToken is null and the environment variable was not set
        // - Threshold is 0
        // - LabelPredicate is null
        // - IssueModelPath is null while IssueNumbers is not null, or vice versa
        // - PullModelPath is null while PullNumbers is not null, or vice versa
        // - Both IssueModelPath and PullModelPath are null
        if (config.Org is null || config.Repo is null || config.Threshold == 0 || config.LabelPredicate is null ||
            (config.IssueModelPath is null != config.IssueNumbers is null) ||
            (config.PullModelPath is null != config.PullNumbers is null) ||
            (config.IssueModelPath is null && config.PullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        if (config.GithubToken is null)
        {
            string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (string.IsNullOrEmpty(token))
            {
                ShowUsage("Argument '--token' not specified and environment variable GITHUB_TOKEN is empty.");
                return null;
            }

            config.GithubToken = token;
        }

        return config;
    }

    private static ulong[] ParseNumbers(string argument)
    {
        List<ulong> numbers = new();

        foreach (var range in argument.Split(','))
        {
            var beginEnd = range.Split('-');

            if (beginEnd.Length == 1)
            {
                numbers.Add(ulong.Parse(beginEnd[0]));
            }
            else if (beginEnd.Length == 2)
            {
                var begin = ulong.Parse(beginEnd[0]);
                var end = ulong.Parse(beginEnd[1]);

                for (var number = begin; number <= end; number++)
                {
                    numbers.Add(number);
                }
            }
            else
            {
                ShowUsage($"Issue and pull numbers must be comma-separated lists of numbers or dash-separated ranges.");
                return [];
            }
        }

        return numbers.ToArray();
    }
}
