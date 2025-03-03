// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class ConfigurationParser
{
    public static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine("  --repo {org}/{repo}");
        Console.WriteLine("  --label-prefix {label-prefix}");
        Console.WriteLine("  --threshold {threshold}. Numbers in range [0, 1]");
        Console.WriteLine("  [--issue-model {path/to/issue-model.zip}]");
        Console.WriteLine("  [--issue-numbers {issue-numbers}]. Comma-separated list of number ranges");
        Console.WriteLine("  [--pull-model {path/to/pull-model.zip}]");
        Console.WriteLine("  [--pull-numbers {pull-numbers}]. Comma-separated list of number ranges");
        Console.WriteLine("  [--default-label {needs-area-label}]");
        Console.WriteLine("  [--token {github_token}]. Default: read from GITHUB_TOKEN env var");
        Console.WriteLine("  [--test]");

        Environment.Exit(1);
    }

    public static Configuration? Parse(string[] args)
    {
        string? gitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        Queue<string> arguments = new(args);
        Configuration config = new();

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    gitHubToken = arguments.Dequeue();
                    break;
                case "--repo":
                    string orgRepo = arguments.Dequeue();

                    if (!orgRepo.Contains('/'))
                    {
                        ShowUsage($$"""Argument '--repo' is not in the format of '{org}/{repo}': {{orgRepo}}""");
                        return null;
                    }

                    string[] parts = orgRepo.Split('/');
                    config.Org = parts[0];
                    config.Repo = parts[1];
                    break;
                case "--issue-model":
                    config.IssueModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.IssueModelPath))
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--issue-numbers":
                    config.IssueNumbers ??= new();
                    config.IssueNumbers.AddRange(ParseNumbers(arguments.Dequeue()));
                    break;
                case "--pull-model":
                    config.PullModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.PullModelPath))
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-numbers":
                    config.PullNumbers ??= new();
                    config.PullNumbers.AddRange(ParseNumbers(arguments.Dequeue()));
                    break;
                case "--label-prefix":
                    string labelPrefix = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(labelPrefix))
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    config.LabelPredicate = label => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;
                case "--threshold":
                    config.Threshold = float.Parse(arguments.Dequeue());
                    break;
                case "--default-label":
                    config.DefaultLabel = arguments.Dequeue();
                    break;
                case "--test":
                    config.Test = true;
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (config.Org is null || config.Repo is null || gitHubToken is null || config.Threshold == 0 || config.LabelPredicate is null ||
            (config.IssueModelPath is null != config.IssueNumbers is null) ||
            (config.PullModelPath is null != config.PullNumbers is null) ||
            (config.IssueModelPath is null && config.PullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        config.GithubToken = gitHubToken;

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
