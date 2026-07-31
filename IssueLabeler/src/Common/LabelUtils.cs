// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class LabelUtils
{
    public static string[] NormalizeLabels(string[]? labels) =>
        labels is null
            ? []
            : [.. labels
                .Select(label => label.Trim())
                .Where(label => label.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
}
