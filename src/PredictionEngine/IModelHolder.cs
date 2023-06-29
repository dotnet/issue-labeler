// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML;

namespace PredictionEngine;

public interface IModelHolder
{
    bool IsPrEngineLoaded { get; }
    bool LoadRequested { get; }
    bool IsIssueEngineLoaded { get; }
    PredictionEngine<GitHubIssue, GitHubIssuePrediction> IssuePredEngine { get; }
    PredictionEngine<GitHubPullRequest, GitHubIssuePrediction> PrPredEngine { get; }
    bool UseIssuesForPrsToo { get; }
}
