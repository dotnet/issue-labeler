// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.DotNet.Github.IssueLabeler.Helpers;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    public class Labeler
    {
        private GitHubClient _client;
        private Regex _regex;
        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly double _threshold;
        private readonly string _secretUri;
        private readonly DiffHelper _diffHelper;
        private readonly DatasetHelper _datasetHelper;
        private readonly string MessageToAddDoc = "We detected that you modified a ref cs file. If you are adding or modifying a public API, please make sure to document it with triple slash comments so the reviewers can sign off your change.";

        public Labeler(string repoOwner, string repoName, string secretUri, double threshold, DiffHelper diffHelper, DatasetHelper datasetHelper)
        {
            _repoOwner = repoOwner;
            _repoName = repoName;
            _threshold = threshold;
            _secretUri = secretUri;
            _diffHelper = diffHelper;
            _datasetHelper = datasetHelper;
        }

        private async Task GitSetupAsync()
        {
            AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
            KeyVaultClient keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            SecretBundle secretBundle = await keyVaultClient.GetSecretAsync(_secretUri).ConfigureAwait(false);

            var productInformation = new ProductHeaderValue("MLGitHubLabeler");
            _client = new GitHubClient(productInformation)
            {
                Credentials = new Credentials(secretBundle.Value)
            };
        }

        public async Task UpdateAreaLabelAsync(int number, GithubObjectType issueOrPr, ILogger logger, List<string> newLabels)
        {
            if (_client == null)
            {
                await GitSetupAsync();
            }
            var issueUpdate = new IssueUpdate();
            Issue iop = await _client.Issue.Get(_repoOwner, _repoName, number);
            var existingLabelNames = iop?.Labels?.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name);

            foreach (var newLabel in newLabels)
            {
                if (newLabel.StartsWith("area-"))
                {
                    if (!existingLabelNames.Where(x => x.StartsWith("area-")).Any())
                    {
                        issueUpdate.AddLabel(newLabel);
                    }
                }
                else if (newLabel.StartsWith("tenet-performance"))
                {
                    if (!existingLabelNames.Where(x => x.StartsWith("tenet-performance")).Any())
                    {
                        issueUpdate.AddLabel(newLabel);
                    }
                }
                else
                {
                    // could be untriaged label or documentation label
                    if (!existingLabelNames.Contains(newLabel))
                    {
                        issueUpdate.AddLabel(newLabel);
                    }
                }
            }

            if (issueUpdate.Labels != null && issueUpdate.Labels.Count > 0)
            {
                issueUpdate.Milestone = iop.Milestone?.Number; // The number of milestone associated with the issue.
                foreach (var existingLabel in existingLabelNames)
                {
                    issueUpdate.AddLabel(existingLabel);
                }
                await _client.Issue.Update(_repoOwner, _repoName, number, issueUpdate);
            }
            else
            {
                logger.LogInformation($"! No update made to labels for {issueOrPr} {number}.");
            }
        }

        internal async Task<List<string>> PredictLabelAsync(int number, GithubObjectType issueOrPr, ILogger logger, bool canCommentOnIssue = false)
        {
            if (_client == null)
            {
                await GitSetupAsync();
            }
            if (_regex == null)
            {
                _regex = new Regex(@"@[a-zA-Z0-9_//-]+");
            }
            var iop = await _client.Issue.Get(_repoOwner, _repoName, number);
            var userMentions = _regex.Matches(iop.Body).Select(x => x.Value).ToArray();

            List<string> labels = new List<string>();
            string areaLabel = null;
            if (issueOrPr == GithubObjectType.Issue)
            {
                IssueModel issue = CreateIssue(number, iop.Title, iop.Body, userMentions, iop.User.Login);
                areaLabel = Predictor.Predict(issue, logger, _threshold);
            }
            else
            {
                PrModel pr = await CreatePullRequest(number, iop.Title, iop.Body, userMentions, iop.User.Login);
                areaLabel = Predictor.Predict(pr, logger, _threshold);
                if (pr.ShouldAddDoc)
                {
                    logger.LogInformation($"! PR number {number} should be a documentation PR.");
                    if (canCommentOnIssue)
                    {
                        labels.Add("documentation");
                        await _client.Issue.Comment.Create(_repoOwner, _repoName, number, MessageToAddDoc);
                    }
                }
            }

            if (areaLabel == null)
            {
                logger.LogInformation($"! The Model was not able to assign the label to the {issueOrPr} {number} confidently.");
            }
            else
            {
                labels.Add(areaLabel);
            }
            return labels;
        }

        private static IssueModel CreateIssue(int number, string title, string body, string[] userMentions, string author)
        {
            return new IssueModel()
            {
                Number = number,
                Title = title,
                Body = body,
                IsPR = 0,
                IssueAuthor = author,
                UserMentions = string.Join(' ', userMentions),
                NumMentions = userMentions.Length
            };
        }

        private async Task<PrModel> CreatePullRequest(int number, string title, string body, string[] userMentions, string author)
        {
            var pr = new PrModel()
            {
                Number = number,
                Title = title,
                Body = body,
                IsPR = 1,
                IssueAuthor = author,
                UserMentions = string.Join(' ', userMentions),
                NumMentions = userMentions.Length,
            };
            IReadOnlyList<PullRequestFile> prFiles = await _client.PullRequest.Files(_repoOwner, _repoName, number);
            if (prFiles.Count != 0)
            {
                string[] filePaths = prFiles.Select(x => x.FileName).ToArray();
                var segmentedDiff = _diffHelper.SegmentDiff(filePaths);
                pr.Files = _datasetHelper.FlattenIntoColumn(segmentedDiff.fileDiffs);
                pr.Filenames = _datasetHelper.FlattenIntoColumn(segmentedDiff.filenames);
                pr.FileExtensions = _datasetHelper.FlattenIntoColumn(segmentedDiff.extensions);
                pr.Folders = _datasetHelper.FlattenIntoColumn(segmentedDiff.folders);
                pr.FolderNames = _datasetHelper.FlattenIntoColumn(segmentedDiff.folderNames);
                pr.ShouldAddDoc = segmentedDiff.addDocInfo;
            }
            pr.FileCount = prFiles.Count;
            return pr;
        }
    }
}
