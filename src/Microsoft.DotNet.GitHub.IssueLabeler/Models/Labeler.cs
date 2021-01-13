// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using Microsoft.DotNet.Github.IssueLabeler.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
    public class Labeler : ILabeler
    {
        private Regex _regex;
        private readonly IDiffHelper _diffHelper;
        private readonly bool _useIssueLabelerForPrsToo;
        private readonly IModelHolder _modelHolder;
        private readonly IPredictor _predictor;
        private readonly ILogger<Labeler> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IGitHubClientWrapper _gitHubClientWrapper;

        public Labeler(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<Labeler> logger,
            IModelHolder modelHolder,
            IGitHubClientWrapper gitHubClientWrapper,
            IPredictor predictor,
            IDiffHelper diffHelper)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _gitHubClientWrapper = gitHubClientWrapper;
            _predictor = predictor;
            _diffHelper = diffHelper;
            _modelHolder = modelHolder;
            _useIssueLabelerForPrsToo = configuration.GetSection(("UseIssueLabelerForPrsToo")).Get<bool>();
        }

        public async Task<LabelSuggestion> PredictUsingModelsFromStorageQueue(string owner, string repo, int number)
        {
            if (_regex == null)
            {
                _regex = new Regex(@"@[a-zA-Z0-9_//-]+");
            }
            if (!_modelHolder.IsIssueEngineLoaded || !_modelHolder.IsPrEngineLoaded)
            {
                throw new InvalidOperationException("load engine before calling predict");
            }

            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
            bool isPr = iop.PullRequest != null;


            string body = iop.Body ?? string.Empty;
            var userMentions = _regex.Matches(body).Select(x => x.Value).ToArray();
            LabelSuggestion labelSuggestion = null;

            if (isPr && !_useIssueLabelerForPrsToo)
            {
                var prModel = await CreatePullRequest(owner, repo, iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
                labelSuggestion = _predictor.Predict(prModel, _logger);
                _logger.LogInformation("predicted with pr model the new way");
                _logger.LogInformation(string.Join(",", labelSuggestion.LabelScores.Select(x => x.LabelName)));
                return labelSuggestion;
            }
            var issueModel = CreateIssue(iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
            labelSuggestion = _predictor.Predict(issueModel, _logger);
            _logger.LogInformation("predicted with issue model the new way");
            _logger.LogInformation(string.Join(",", labelSuggestion.LabelScores.Select(x => x.LabelName)));
            return labelSuggestion;
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

        private async Task<PrModel> CreatePullRequest(string owner, string repo, int number, string title, string body, string[] userMentions, string author)
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
            IReadOnlyList<PullRequestFile> prFiles = await _gitHubClientWrapper.GetPullRequestFiles(owner, repo, number);
            if (prFiles.Count != 0)
            {
                string[] filePaths = prFiles.Select(x => x.FileName).ToArray();
                var segmentedDiff = _diffHelper.SegmentDiff(filePaths);
                pr.Files = string.Join(' ', segmentedDiff.FileDiffs);
                pr.Filenames = string.Join(' ', segmentedDiff.Filenames);
                pr.FileExtensions = string.Join(' ', segmentedDiff.Extensions);
                pr.Folders = _diffHelper.FlattenWithWhitespace(segmentedDiff.Folders);
                pr.FolderNames = _diffHelper.FlattenWithWhitespace(segmentedDiff.FolderNames);
                try
                {
                    pr.ShouldAddDoc = await DoesPrAddNewApiAsync(owner, repo, pr.Number);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("! problem with new approach: " + ex.Message);
                    pr.ShouldAddDoc = segmentedDiff.AddDocInfo;
                }
            }
            pr.FileCount = prFiles.Count;
            return pr;
        }

        private async Task<bool> DoesPrAddNewApiAsync(string owner, string repo, int prNumber)
        {
            var pr = await _gitHubClientWrapper.GetPullRequest(owner, repo, prNumber);
            var diff = new Uri(pr.DiffUrl);
            var httpclient = _httpClientFactory.CreateClient();
            var response = await httpclient.GetAsync(diff.LocalPath);
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
