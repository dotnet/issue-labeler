// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.GitHub.IssueLabeler;
using Microsoft.ML;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public interface IModelHolder
    {
        bool IsPrEngineLoaded { get; }
        bool LoadRequested { get; }
        bool IsIssueEngineLoaded { get; }
        PredictionEngine<IssueModel, GitHubIssuePrediction> IssuePredEngine { get; }
        PredictionEngine<PrModel, GitHubIssuePrediction> PrPredEngine { get; }
        Task LoadEnginesAsync();
        bool UseIssuesForPrsToo { get; }
    }
}
