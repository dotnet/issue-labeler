using Microsoft.DotNet.GitHub.IssueLabeler;
using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.Github.IssueLabeler.Models
{
    public class LabelRetriever
    {
        public bool PreferManualLabelingFor(string chosenLabel)
        {
            if (_owner.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && _repo.Equals("runtime", StringComparison.OrdinalIgnoreCase))
            {
                return chosenLabel.Equals("area-Infrastructure", StringComparison.OrdinalIgnoreCase) || chosenLabel.Equals("area-System.Runtime", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public string GetMessageToAddDocForNewApi(string label) => $"""
            Note regarding the `{label}` label:

            This serves as a reminder for when your PR is modifying a ref *.cs file and adding/modifying public APIs, please make sure the API implementation in the src *.cs file is documented with triple slash comments, so the PR reviewers can sign off that change.
            """;

        private static string LinkToAreaOwnersDoc(string areaOwnersDoc) =>
            string.IsNullOrWhiteSpace(areaOwnersDoc) ? "area label" : $"[area label]({areaOwnersDoc})";

        public string GetMessageToAddAreaLabelForPr(string areaOwnersDoc) =>
            $"I couldn't figure out the best area label to add to this PR. If you have write-permissions please help me learn by adding exactly one {LinkToAreaOwnersDoc(areaOwnersDoc)}.";

        public string GetMessageToAddAreaLabelForIssue(string areaOwnersDoc) =>
            $"I couldn't figure out the best area label to add to this issue. If you have write-permissions please help me learn by adding exactly one {LinkToAreaOwnersDoc(areaOwnersDoc)}.";

        private readonly string _owner;
        private readonly string _repo;
        public LabelRetriever(string owner, string repo)
        {
            _owner = owner;
            _repo = repo;
        }
    }
}