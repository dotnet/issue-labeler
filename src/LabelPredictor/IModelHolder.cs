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
