// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

public static class ArgUtils
{
    public static bool TryDequeueString(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out string? argValue)
    {
        argValue = Dequeue(args);
        if (argValue is null)
        {
            showUsage($"Argument '{argName}' has an empty value.");
            return false;
        }

        return true;
    }

    public static bool TryDequeueRepo(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out string? repo)
    {
        string? orgRepo = Dequeue(args);
        if (orgRepo is null || !orgRepo.Contains('/'))
        {
            showUsage($$"""Argument '{{argName}}' has an empty value or is not in the format of '{org}/{repo}'.""");
            org = null;
            repo = null;
            return false;
        }

        string[] parts = orgRepo.Split('/');
        org = parts[0];
        repo = parts[1];
        return true;
    }

    public static bool TryDequeueRepoList(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out string? org, [NotNullWhen(true)] out List<string>? repos)
    {
        string? orgRepos = ArgUtils.Dequeue(args);
        org = null;
        repos = null;

        if (orgRepos is null)
        {
            showUsage($$"""Argument '{argName}' has an empty value or is not in the format of '{org}/{repo}'.""");
            return false;
        }

        foreach (var orgRepo in orgRepos.Split(',').Select(r => r.Trim()))
        {
            if (!orgRepo.Contains('/'))
            {
                showUsage($"Argument '--repo' is not in the format of '{{org}}/{{repo}}': {orgRepo}");
                return false;
            }

            string[] parts = orgRepo.Split('/');

            if (org is not null && org != parts[0])
            {
                showUsage("All '--repo' values must be from the same org.");
                return false;
            }

            org ??= parts[0];
            repos ??= [];
            repos.Add(parts[1]);
        }

        return (org is not null && repos is not null);
    }

    public static bool TryDequeueLabelPrefix(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out Func<string, bool>? labelPredicate)
    {
        if (!TryDequeueString(args, showUsage, argName, out string? labelPrefix))
        {
            labelPredicate = null;
            return false;
        }

        labelPredicate = (label) => label.StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    public static bool TryDequeuePath(Queue<string> args, Action<string> showUsage, string argName, out string? path)
    {
        if (!TryDequeueString(args, showUsage, argName, out path))
        {
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(path);
        }

        return true;
    }

    public static bool TryDequeueInt(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out int? argValue)
    {
        if (TryDequeueString(args, showUsage, argName, out string? argString) && int.TryParse(argString, out int parsedValue))
        {
            argValue = parsedValue;
            return true;
        }

        argValue = null;
        return false;
    }

    public static bool TryDequeueIntArray(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out int[]? argValues)
    {
        if (TryDequeueString(args, showUsage, argName, out string? argString))
        {
            argValues = argString.Split(',').Select(r => int.Parse(r)).ToArray();
            return true;
        }

        argValues = null;
        return false;
    }

    public static bool TryDequeueFloat(Queue<string> args, Action<string> showUsage, string argName, [NotNullWhen(true)] out float? argValue)
    {
        if (TryDequeueString(args, showUsage, argName, out string? argString) && float.TryParse(argString, out float parsedValue))
        {
            argValue = parsedValue;
            return true;
        }

        argValue = null;
        return false;
    }

    public static bool TryDequeueNumberRanges(Queue<string> args, Action<string> showUsage, string argName, out List<ulong>? argValues)
    {
        if (!TryDequeueString(args, showUsage, argName, out string? argString))
        {
            argValues = null;
            return false;
        }

        List<ulong> numbers = new();

        foreach (var range in argString.Split(','))
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
                showUsage($"Argument '{argName}' must be comma-separated list of numbers and/or dash-separated ranges. Example: 1-3,5,7-9.");
                argValues = null;
                return false;
            }
        }

        argValues = numbers;
        return true;
    }

    public static string? Dequeue(Queue<string> args)
    {
        if (args.TryDequeue(out string? argValue))
        {
            return string.IsNullOrWhiteSpace(argValue) ? null : argValue;
        }

        return null;
    }
}
