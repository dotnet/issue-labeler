// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class Args
{
    public static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine("  --token {github_token}");
        Console.WriteLine("  --repo {org}/{repo}");
        Console.WriteLine("  --label-prefix {label-prefix}");
        Console.WriteLine("  --threshold {threshold}");
        Console.WriteLine("  [--issue-model {path/to/issue-model.zip}]");
        Console.WriteLine("  [--issue-numbers {issue-numbers}]");
        Console.WriteLine("  [--pull-model {path/to/pull-model.zip}]");
        Console.WriteLine("  [--pull-numbers {pull-numbers}]");
        Console.WriteLine("  [--default-label {needs-area-label}]");
        Console.WriteLine("  [--test]");

        Environment.Exit(1);
    }

    public static (
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
    )?
    Parse(string[] args)
    {
        Queue<string> arguments = new(args);
        string? org = null;
        string? repo = null;
        string? githubToken = null;
        string? issueModelPath = null;
        List<ulong>? issueNumbers = null;
        string? pullModelPath = null;
        List<ulong>? pullNumbers = null;
        float? threshold = null;
        Func<string, bool>? labelPredicate = null;
        string? defaultLabel = null;
        bool test = false;

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--token":
                    githubToken = arguments.Dequeue();
                    break;
                case "--repo":
                    string orgRepo = arguments.Dequeue();

                    if (!orgRepo.Contains('/'))
                    {
                        ShowUsage($$"""Argument '--repo' is not in the format of '{org}/{repo}': {{orgRepo}}""");
                        return null;
                    }

                    string[] parts = orgRepo.Split('/');
                    org = parts[0];
                    repo = parts[1];
                    break;
                case "--issue-model":
                    issueModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(issueModelPath))
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--issue-numbers":
                    issueNumbers ??= new();
                    issueNumbers.AddRange(ParseNumbers(arguments.Dequeue()));
                    break;
                case "--pull-model":
                    pullModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullModelPath))
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }

                    break;
                case "--pull-numbers":
                    pullNumbers ??= new();
                    pullNumbers.AddRange(ParseNumbers(arguments.Dequeue()));
                    break;
                case "--label-prefix":
                    string labelPrefix = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(labelPrefix))
                    {
                        ShowUsage("Argument '--label-prefix' has an empty value.");
                        return null;
                    }

                    labelPredicate = label => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
                    break;
                case "--threshold":
                    threshold = float.Parse(arguments.Dequeue());
                    break;
                case "--default-label":
                    defaultLabel = arguments.Dequeue();
                    break;
                case "--test":
                    test = true;
                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if (org is null || repo is null || githubToken is null || threshold is null || labelPredicate is null ||
            (issueModelPath is null != issueNumbers is null) ||
            (pullModelPath is null != pullNumbers is null) ||
            (issueModelPath is null && pullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        return (
            (string)org,
            (string)repo,
            (string)githubToken,
            issueModelPath,
            issueNumbers,
            pullModelPath,
            pullNumbers,
            (float)threshold,
            (Func<string, bool>)labelPredicate,
            defaultLabel,
            test
        );
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
