using Microsoft.DotNet.GitHub.IssueLabeler;
using System.Collections.Generic;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public interface ILabelRetriever
    {
        string MessageToAddAreaLabelForIssue { get; }
        string MessageToAddAreaLabelForPr { get; }
        bool AddDelayBeforeUpdatingLabels { get; }
        bool AllowTakingLinkedIssueLabel { get; }
        bool CommentWhenMissingAreaLabel { get; }
        bool OkToAddUntriagedLabel { get; }
        bool SkipPrediction { get; }

        string CommentFor(string label);
        HashSet<string> GetNonAreaLabelsForIssueAsync(IssueModel issue);
        bool OkToIgnoreThresholdFor(string chosenLabel);
        bool PreferManualLabelingFor(string chosenLabel);
        bool ShouldSkipUpdatingLabels(string issueAuthor);
    }
}