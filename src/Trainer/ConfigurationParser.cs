// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

static class ConfigurationParser
{
    private const string DefaultIssuesModelFileName = "issue-model.zip";
    private const string DefaultPullsModelFileName = "pull-model.zip";

    static void ShowUsage(string? message = null)
    {
        // • If you provide a path for issue data, you must also provide a path for the issue model, and vice versa.
        // • If you provide a path for pull data, you must also provide a path for the pull model, and vice versa.
        // • At least one pair of paths(either issue or pull) must be provided.

        Console.WriteLine($"ERROR: Invalid or missing arguments.{(message is null ? "" : " " + message)}");
        Console.WriteLine();

        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine($"  {executableName} --issue-data {{path/to/issue-data.tsv}} --issue-model {{path/to/{DefaultIssuesModelFileName}}}");
        Console.WriteLine("      --issue-data        Input tab-separated list of source issues");
        Console.WriteLine($"      --issue-model       Output ML model. Default: <current folder>/{DefaultIssuesModelFileName}");
        Console.WriteLine();
        Console.WriteLine($"  {executableName} --pull-data {{path/to/pull-data.tsv}} --pull-model {{path/to/{DefaultPullsModelFileName}}}");
        Console.WriteLine("      --pull-data         Input tab-separated list of source pull-requests");
        Console.WriteLine($"      --pull-model        Output ML model. Default: <current folder>/{DefaultPullsModelFileName}");

        Environment.Exit(1);
    }

    public static Configuration? Parse(string[] args)
    {
        Queue<string> arguments = new(args);
        Configuration config = new();

        string? issueModelPath = null;
        string? pullModelPath = null;

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--issue-data":
                    config.IssueDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.IssueDataPath))
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--issue-model":
                    issueModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(issueModelPath))
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-data":
                    config.PullDataPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(config.PullDataPath))
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }
                    break;
                case "--pull-model":
                    pullModelPath = arguments.Dequeue();
                    if (string.IsNullOrWhiteSpace(pullModelPath))
                    {
                        ShowUsage("Argument '--pull-model' has an empty value.");
                        return null;
                    }
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}");
                    return null;
            }
        }

        if ((config.IssueDataPath is null != issueModelPath is null) ||
            (config.PullDataPath is null != pullModelPath is null) ||
            (issueModelPath is null && pullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        if (config.IssueDataPath is not null)
        {
            config.IssueModelPath = ResolvePath(issueModelPath, DefaultIssuesModelFileName);
        }

        if (config.PullDataPath is not null)
        {
            config.PullModelPath = ResolvePath(pullModelPath, DefaultPullsModelFileName);
        }

        return config;

        static string ResolvePath(string? path, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Path.Combine(Environment.CurrentDirectory, defaultFileName);
            }

            if (!Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return path;
        }
    }
}
