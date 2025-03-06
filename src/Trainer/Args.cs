// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

public struct Args
{
    public string? IssueDataPath { get; set; }
    public string? IssueModelPath { get; set; }
    public string? PullDataPath { get; set; }
    public string? PullModelPath { get; set; }

    static void ShowUsage(string? message = null)
    {
        // If you provide a path for issue data, you must also provide a path for the issue model, and vice versa.
        // If you provide a path for pull data, you must also provide a path for the pull model, and vice versa.
        // At least one pair of paths(either issue or pull) must be provided.
        string executableName = Process.GetCurrentProcess().ProcessName;

        Console.WriteLine($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Usage:
              {{executableName}} [options]

                Required for training the issue model:
                  --issue-data        Path to existing issue data file (TSV file).
                  --issue-model       Path for issue prediction model file to create (ZIP file).

                Required for training the pull request model:
                  --pull-data         Path to existing pull request data file (TSV file).
                  --pull-model        Path for pull request prediction model file to create (ZIP file).
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args)
    {
        Args config = new();

        Queue<string> arguments = new(args);
        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--issue-data":
                    if ((config.IssueDataPath = ArgUtils.DequeuePath(arguments)) is null)
                    {
                        ShowUsage("Argument '--issue-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--issue-model":
                    if ((config.IssueModelPath = ArgUtils.DequeuePath(arguments)) is null)
                    {
                        ShowUsage("Argument '--issue-model' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-data":
                    if ((config.PullDataPath = ArgUtils.DequeuePath(arguments)) is null)
                    {
                        ShowUsage("Argument '--pull-data' has an empty value.");
                        return null;
                    }
                    break;

                case "--pull-model":
                    if ((config.PullModelPath = ArgUtils.DequeuePath(arguments)) is null)
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

        if ((config.IssueDataPath is null != config.IssueModelPath is null) ||
            (config.PullDataPath is null != config.PullModelPath is null) ||
            (config.IssueModelPath is null && config.PullModelPath is null))
        {
            ShowUsage();
            return null;
        }

        return config;
    }
}
