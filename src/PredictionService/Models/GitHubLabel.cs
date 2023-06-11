// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Data;
using Newtonsoft.Json;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace PredictionService.Models;

public class GitHubLabel
{
    [LoadColumn(0)]
    [ColumnName("id")]
    public long Id;

    [LoadColumn(1)]
    [ColumnName("node_id")]
    public string NodeId;

    [LoadColumn(2)]
    [ColumnName("url")]
    public string Url;

    [LoadColumn(3)]
    [ColumnName("name")]
    public string Name;

    [LoadColumn(4)]
    [ColumnName("color")]
    public string Color;

    [LoadColumn(5)]
    [ColumnName("default")]
    public bool Flag;

    [LoadColumn(6)]
    [ColumnName("description")]
    public string Description;
}
