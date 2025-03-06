// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class ArgUtils
{
    public static string? Dequeue(Queue<string> args)
    {
        if (args.TryDequeue(out string? argValue))
        {
            return string.IsNullOrWhiteSpace(argValue) ? null : argValue;
        }

        return null;
    }

    public static int? DequeueInt(Queue<string> args)
    {
        string? argValue = Dequeue(args);

        if (argValue is not null && int.TryParse(argValue, out int parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    public static float? DequeueFloat(Queue<string> args)
    {
        string? argValue = Dequeue(args);

        if (argValue is not null && float.TryParse(argValue, out float parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    public static string? DequeuePath(Queue<string> args)
    {
        string? path = Dequeue(args);

        if (path is not null && !Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return path;
    }
}
