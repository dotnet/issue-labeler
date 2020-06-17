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
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    public class Labeler
    {
        private GitHubClient _client;
        private HttpClient _httpClient;
        private Regex _regex;
        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly double _threshold;
        private readonly string _secretUri;
        private readonly DiffHelper _diffHelper;
        private readonly DatasetHelper _datasetHelper;
        private readonly string MessageToAddDoc =
            "Note regarding the `new-api-needs-documentation` label:" + Environment.NewLine + Environment.NewLine +
            "This serves as a reminder for when your PR is modifying a ref *.cs file and adding/modifying public APIs, to please make sure the API implementation in the src *.cs file is documented with triple slash comments, so the PR reviewers can sign off that change.";
        private readonly string MessageToAddAreaLabelForPr =
            "I couldn't figure out the best area label to add to this PR. Please help me learn by adding exactly one " + AreaLabelLinked + ".";
        private readonly string MessageToAddAreaLabelForIssue =
            "I couldn't figure out the best area label to add to this issue. Please help me learn by adding exactly one " + AreaLabelLinked + ".";
        private static readonly string AreaLabelLinked =
            "[area label](" + 
                @"https://github.com/dotnet/runtime/blob/master/docs/area-owners.md" +
            ")";

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

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AppName", "1.0"));
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", secretBundle.Value);
            _httpClient.BaseAddress = new Uri("https://github.com/");
            _httpClient.Timeout = new TimeSpan(0, 0, 30);
        }

        public async Task UpdateAreaLabelAsync(int number, GithubObjectType issueOrPr, ILogger logger, List<string> newLabels)
        {
            if (_client == null)
            {
                await GitSetupAsync();
            }

            Issue iop = await _client.Issue.Get(_repoOwner, _repoName, number);
            var existingLabelNames = iop?.Labels?.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name);

            if (newLabels.Count > 0)
            {
                var issueUpdate = new IssueUpdate();
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

            // if newlabels has no area-label and existing does not also. then comment
            if (!newLabels.Where(x => x.StartsWith("area-")).Any() &&
                !existingLabelNames.Where(x => x.StartsWith("area-")).Any())
            {
                if (issueOrPr == GithubObjectType.Issue)
                {
                    await _client.Issue.Comment.Create(_repoOwner, _repoName, number, MessageToAddAreaLabelForIssue);
                }
                else
                {
                    await _client.Issue.Comment.Create(_repoOwner, _repoName, number, MessageToAddAreaLabelForPr);
                }
            }
        }

        internal async Task<string> JustPredictLabelAsync(int number, ILogger logger)
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
            logger.LogInformation($"! Just checking for {iop} {number}.");
            bool isPr = iop.PullRequest != null;
            var userMentions = _regex.Matches(iop.Body).Select(x => x.Value).ToArray();
            string areaLabel = null;
            if (!isPr)
            {
                IssueModel issue = CreateIssue(number, iop.Title, iop.Body, userMentions, iop.User.Login);
                areaLabel = Predictor.Predict(issue, logger, _threshold);
            }
            else
            {
                PrModel pr = await CreatePullRequest(number, iop.Title, iop.Body, userMentions, iop.User.Login, logger);
                areaLabel = Predictor.Predict(pr, logger, _threshold);
                if (pr.ShouldAddDoc)
                {
                    logger.LogInformation($"! PR number {number} should be a documentation PR as it adds lines to a ref *cs file.");
                }
            }

            if (areaLabel == null)
            {
                logger.LogInformation($"! The Model was not able to assign the label to the {iop} {number} confidently.");
            }
            logger.LogInformation($"! Just checked for {iop} {number}.");
            return RenameMapping(areaLabel);
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
                PrModel pr = await CreatePullRequest(number, iop.Title, iop.Body, userMentions, iop.User.Login, logger);
                areaLabel = Predictor.Predict(pr, logger, _threshold);
                if (pr.ShouldAddDoc)
                {
                    logger.LogInformation($"! PR number {number} should be a documentation PR as it adds lines to a ref *cs file.");
                    if (canCommentOnIssue)
                    {
                        labels.Add("new-api-needs-documentation");
                        await _client.Issue.Comment.Create(_repoOwner, _repoName, number, MessageToAddDoc);
                    }
                }
                if (iop.User.Login.Equals("monojenkins"))
                {
                    labels.Add("mono-mirror");
                }
            }

            if (areaLabel == null)
            {
                logger.LogInformation($"! The Model was not able to assign the label to the {issueOrPr} {number} confidently.");
            }
            else
            {
                labels.Add(RenameMapping(areaLabel));
            }
            return labels;
        }

        private static string RenameMapping(string predictedLabel)
        {
            var ret = predictedLabel;
            switch (predictedLabel)
            {
                /* ????
area-System.ComponentModel : split off System.ComponentModel.Composition (for MEF1 specific issues)
                 */
                case "area-Meta-corelib":
                    ret = "area-Meta";
                    break;
                case "area-System.AppContext":
                case "area-System.Runtime.Extensions":
                    ret = "area-System.Runtime";
                    break;
                case "area-System.IO.Packaging":
                    ret = "area-System.IO.Compression";
                    break;
                case "area-System.Security.Cryptography.Xml":
                    ret = "area-System.Security";
                    break;
                case "area-AssemblyLoader":
                case "area-CodeGen":
                case @"area-CrossGen/NGEN":
                case "area-crossgen2":
                case "area-Diagnostics":
                case "area-ExceptionHandling":
                case "area-GC":
                case "area-Interop":
                case "area-PAL":
                case "area-TieredCompilation":
                case "area-Tracing":
                case "area-TypeSystem":
                case "area-R2RDump":
                case "area-ReadyToRun":
                case "area-ILTools":
                case "area-VM":
                    ret = predictedLabel + "-coreclr";
                    break;
                default:
                    break;
            }
            return ret;
        }

        private static IssueModel CreateIssue(int number, string title, string body, string[] userMentions, string author)
        {
            return new IssueModel()
            {
                Number = number,
                Title = title,
                Description = body,
                IsPR = 0,
                Author = author,
                UserMentions = string.Join(' ', userMentions),
                NumMentions = userMentions.Length
            };
        }

        private async Task<PrModel> CreatePullRequest(int number, string title, string body, string[] userMentions, string author, ILogger logger)
        {
            var pr = new PrModel()
            {
                Number = number,
                Title = title,
                Description = body,
                IsPR = 1,
                Author = author,
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
                try
                {
                    pr.ShouldAddDoc = await prAddsNewApi(pr.Number);
                }
                catch (Exception ex)
                {
                    logger.LogInformation("! problem with new approach: " + ex.Message);
                    pr.ShouldAddDoc = segmentedDiff.addDocInfo;
                }
            }
            pr.FileCount = prFiles.Count;
            return pr;
        }

        internal async Task<bool> prAddsNewApi(int prNumber)
        {
            if (_client == null)
            {
                await GitSetupAsync();
            }
            var pr = await _client.PullRequest.Get(_repoOwner, _repoName, prNumber);
            var diff = new Uri(pr.DiffUrl);
            var response = await _httpClient.GetAsync(diff.LocalPath);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return TakeDiffContentReturnMeaning(content.Split("\n"));
        }

        private enum DiffContentLineReadingStatus
        {
            readyToStartOver = 0,
            expectingIndex,
            expectingTripleMinus,
            expectingTriplePlus,
            expectingDoubleAtSign
        }

        private bool TakeDiffContentReturnMeaning(string[] contentLines)
        {
            string curFile = string.Empty;
            var refFilesWithAdditions = new Dictionary<string, int>();
            int additions = 0, deletions = 0;
            bool lookingAtRefDiff = false;
            var stat = DiffContentLineReadingStatus.readyToStartOver;
            for (int i = 0; i < contentLines.Length; i++)
            {
                var line = contentLines[i];
                switch (stat)
                {
                    case DiffContentLineReadingStatus.readyToStartOver:
                        if (ContainsRefChanges(line))
                        {
                            if (!string.IsNullOrEmpty(curFile) && additions > deletions)
                            {
                                refFilesWithAdditions.Add(curFile, additions - deletions);
                                // reset
                                additions = 0;
                                deletions = 0;
                            }
                            lookingAtRefDiff = true;
                            curFile = line.Substring(13, line.IndexOf(@".cs b/") + 3 - 13);
                            stat = DiffContentLineReadingStatus.expectingIndex;
                        }
                        else if (line.StartsWith("diff --git"))
                        {
                            lookingAtRefDiff = false;
                        }
                        else if (lookingAtRefDiff)
                        {
                            if (line.StartsWith("+") && !IsUnwantedDiff(line))
                            {
                                additions++;
                            }
                            else if (line.StartsWith("-") && !IsUnwantedDiff(line))
                            {
                                deletions++;
                            }
                        }
                        break;
                    case DiffContentLineReadingStatus.expectingIndex:
                        if (line.StartsWith("index "))
                        {
                            stat = DiffContentLineReadingStatus.expectingTripleMinus;
                        }
                        break;
                    case DiffContentLineReadingStatus.expectingTripleMinus:
                        if (line.StartsWith("--- "))
                        {
                            stat = DiffContentLineReadingStatus.expectingTriplePlus;
                        }
                        break;
                    case DiffContentLineReadingStatus.expectingTriplePlus:
                        if (line.StartsWith("+++ "))
                        {
                            stat = DiffContentLineReadingStatus.expectingDoubleAtSign;
                        }
                        break;
                    case DiffContentLineReadingStatus.expectingDoubleAtSign:
                        if (line.StartsWith("@@ "))
                        {
                            stat = DiffContentLineReadingStatus.readyToStartOver;
                        }
                        break;
                    default:
                        break;
                }
            }
            if (!string.IsNullOrEmpty(curFile) && additions > deletions)
            {
                refFilesWithAdditions.Add(curFile, additions - deletions);
            }
            return refFilesWithAdditions.Any();
            // given a diff content
            // readyToStartOver = true
            // additions = 0, deletions = 0
            // read all lines
            // for each line, if readyToStartOver and starts with diff: set expectingIndex to true
            // for each line, if expectingIndex and starts with index: set expectingTripleMinus
            // for each line, if expectingTripleMinus and starts ---: set expectingTriplePlus
            // for each line, if expectingTriplePlus and starts with +++: set expectingDoubleAtSign
            // for each line, if expectingTriplePlus and starts with @@: set readyToStartOver
            // for each line, if readyToStartOver and starts with +: additions++ and if starts with - deletions++
            // for each line, if readyToStartOver and starts with +: additions++ and if starts with - deletions++
            // for each line, if readyToStartOver and starts with diff: ... (already planned for)
            // 


        }

        private bool IsUnwantedDiff(string line)
        {
            if (string.IsNullOrWhiteSpace(line.Substring(1)))
            {
                return true;
            }
            var trimmed = line.Substring(1).TrimStart();
            if (trimmed.StartsWith("[") || trimmed.StartsWith("#") || trimmed.StartsWith("//") || trimmed.StartsWith("using "))
            {
                return true;
            }
            return false;
        }

        private bool ContainsRefChanges(string content)
        {
            if (content.Contains(@"/ref/") && content.Contains(".cs b/src/libraries"))
            {
                return true;
            }
            return false; // diff --git a/src/libraries/(.*)/ref/(.*).cs b/src/libraries/(.*)/ref/(.*).cs
        }
    }
}
