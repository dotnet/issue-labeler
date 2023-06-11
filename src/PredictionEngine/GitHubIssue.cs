// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Data;
using Newtonsoft.Json;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace PredictionEngine;

public class GitHubIssue
{
    [JsonIgnore]
    [LoadColumn(0)]
    public string CombinedID;

    [LoadColumn(1)]
    public float ID;

    [LoadColumn(2)]
    public string Area;

    [LoadColumn(3)]
    public string Title;

    [LoadColumn(4)]
    public string Description;

    [LoadColumn(5)]
    public string Author;

    [LoadColumn(6)]
    public float IsPR;

    [LoadColumn(7)]
    public string UserMentions;

    [LoadColumn(8)]
    public float NumMentions;

    [NoColumn]
    public List<GitHubLabel> Labels { get; set; }

    [NoColumn]
    public int Number { get; set; }
}
