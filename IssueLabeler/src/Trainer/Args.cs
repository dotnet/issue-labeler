// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Actions.Core.Services;

public struct Args
{
    public string? IssuesDataPath { get; set; }
    public string? IssuesModelPath { get; set; }
    public string? PullsDataPath { get; set; }
    public string? PullsModelPath { get; set; }

    static void ShowUsage(string? message, ICoreService action)
    {
        // If you provide a path for issue data, you must also provide a path for the issue model, and vice versa.
        // If you provide a path for pull data, you must also provide a path for the pull model, and vice versa.
        // At least one pair of paths(either issue or pull) must be provided.
        action.WriteNotice($$"""
            ERROR: Invalid or missing arguments.{{(message is null ? "" : " " + message)}}

            Required for training the issues model:
              --issues-data           Path to existing issue data file (TSV file).
              --issues-model          Path to existing issue prediction model file (ZIP file).

            Required for training the pull requests model:
              --pulls-data            Path to existing pull request data file (TSV file).
              --pulls-model           Path to existing pull request prediction model file (ZIP file).
            """);

        Environment.Exit(1);
    }

    public static Args? Parse(string[] args, ICoreService action)
    {
        Queue<string> arguments = new(args);
        ArgUtils argUtils = new(action, ShowUsage, arguments);
        Args argsData = new();

        while (arguments.Count > 0)
        {
            string argument = arguments.Dequeue();

            switch (argument)
            {
                case "--issues-data":
                    if (!argUtils.TryGetPath("--issues-data", out string? IssuesDataPath))
                    {
                        return null;
                    }
                    argsData.IssuesDataPath = IssuesDataPath;
                    break;

                case "--issues-model":
                    if (!argUtils.TryGetPath("--issues-model", out string? IssuesModelPath))
                    {
                        return null;
                    }
                    argsData.IssuesModelPath = IssuesModelPath;
                    break;

                case "--pulls-data":
                    if (!argUtils.TryGetPath("--pulls-data", out string? PullsDataPath))
                    {
                        return null;
                    }
                    argsData.PullsDataPath = PullsDataPath;
                    break;

                case "--pulls-model":
                    if (!argUtils.TryGetPath("--pulls-model", out string? PullsModelPath))
                    {
                        return null;
                    }
                    argsData.PullsModelPath = PullsModelPath;
                    break;

                default:
                    ShowUsage($"Unrecognized argument: {argument}", action);
                    return null;
            }
        }

        if ((argsData.IssuesDataPath is null != argsData.IssuesModelPath is null) ||
            (argsData.PullsDataPath is null != argsData.PullsModelPath is null) ||
            (argsData.IssuesModelPath is null && argsData.PullsModelPath is null))
        {
            ShowUsage(null, action);
            return null;
        }

        return argsData;
    }
}
