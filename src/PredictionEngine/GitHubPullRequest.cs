// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

using Microsoft.ML.Data;

namespace PredictionEngine;

public class GitHubPullRequest : GitHubIssue
{
    // Columns 0-8 are used in the base GitHubIssue model

    [LoadColumn(9)]
    public float FileCount;

    [LoadColumn(10)]
    public string Files;

    [LoadColumn(11)]
    public string Filenames;

    [LoadColumn(12)]
    public string FileExtensions;

    [LoadColumn(13)]
    public string FolderNames;

    [LoadColumn(14)]
    public string Folders;
}
