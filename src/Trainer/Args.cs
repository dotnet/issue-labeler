// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

static class Args
{
    static void ShowUsage(string? message = null)
    {
        Console.WriteLine($"Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine("  [--issue-data {path/to/issue-data.tsv}]");
        Console.WriteLine("  [--issue-model {path/to/issue-model.zip}]");
        Console.WriteLine("  [--pull-data {path/to/pull-data.tsv}]");
        Console.WriteLine("  [--pull-model {path/to/pull-model.zip}]");

        Environment.Exit(1);
    }

    public static (
        string? IssueDataPath,
        string? IssueModelPath,
        string? PullDataPath,
        string? PullModelPath
    )
    Parse(string[] args)
    {
        Queue<string> arguments = new(args);
        string? issueDataPath = null;
        string? issueModelPath = null;
        string? pullDataPath = null;
        string? pullModelPath = null;

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--issue-data":
                    issueDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(issueDataPath))
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return (null, null, null, null);
                    }

                    break;
                case "--issue-model":
                    issueModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(issueModelPath))
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return (null, null, null, null);
                    }

                    break;
                case "--pull-data":
                    pullDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullDataPath))
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return (null, null, null, null);
                    }

                    break;
                case "--pull-model":
                    pullModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullModelPath))
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return (null, null, null, null);
                    }

                    break;
                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return (null, null, null, null);
            }
        }

        if ((issueDataPath is null != issueModelPath is null) ||
            (pullDataPath is null != pullModelPath is null) ||
            (issueModelPath is null && pullModelPath is null))
        {
            ShowUsage();
            return (null, null, null, null);
        }

        return (issueDataPath, issueModelPath, pullDataPath, pullModelPath);
    }
}
