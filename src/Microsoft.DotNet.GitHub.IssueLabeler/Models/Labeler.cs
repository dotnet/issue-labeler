// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using Microsoft.DotNet.Github.IssueLabeler;
using Microsoft.DotNet.Github.IssueLabeler.Models;
using Microsoft.DotNet.GitHub.IssueLabeler.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    public class Labeler : ILabeler
    {
        private IQueueHelper _queueHelper;
        private Regex _regex;
        private readonly Regex _regexIssueMatch;
        private readonly IDiffHelper _diffHelper;
        private readonly ILogger<Labeler> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IGitHubClientWrapper _gitHubClientWrapper;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public Labeler(
            IQueueHelper queueHelper,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<Labeler> logger,
            IBackgroundTaskQueue backgroundTaskQueue,
            IGitHubClientWrapper gitHubClientWrapper,
            IDiffHelper diffHelper)
        {
            _queueHelper = queueHelper;
            _backgroundTaskQueue = backgroundTaskQueue;
            _gitHubClientWrapper = gitHubClientWrapper;
            _diffHelper = diffHelper;
            _regexIssueMatch = new Regex(@"[Ff]ix(?:ed|es|)( )+#(\d+)");
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
        }

        public Task DispatchLabelsAsync(string owner, string repo, int number)
        {
            var tasks = new List<Task>();
            tasks.Add(InnerTask(owner, repo, number));
            return tasks.First();
        }

        private readonly ConcurrentDictionary<(string, string), LabelerOptions> _options =
            new ConcurrentDictionary<(string, string), LabelerOptions>();

        private LabelerOptions GetOptionsFor(string owner, string repo)
        {
            try
            {
                return _options.TryGetValue((owner, repo), out LabelerOptions options) ?
                    options :
                    _options.GetOrAdd((owner, repo), new LabelerOptions()
                    {
                        LabelRetriever = new LabelRetriever(owner, repo),
                        PredictionUrl = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}/api/WebhookIssue/{1}/{2}/", _configuration[$"{owner}:{repo}:prediction_url"],
                            owner, repo),
                        Threshold = double.Parse(_configuration[$"{owner}:{repo}:threshold"]),
                        CanUpdateIssue = _configuration.GetSection($"{owner}:{repo}:can_update_labels").Get<bool>(),
                        CanCommentOnIssue = _configuration.GetSection($"{owner}:{repo}:can_comment_on").Get<bool>(),
                        NoAreaDeterminedLabel = _configuration.GetSection($"{owner}:{repo}:no_area_determined_label").Get<string>()
                    });
            }
            catch (Exception)
            {
                // the repo is not configured, return null to skip
                _logger.LogError($"{owner}/{repo} is not yet configured.");
                return null;
            }
        }

        private class LabelerOptions
        {
            public ILabelRetriever LabelRetriever { get; set; }
            public string PredictionUrl { get; set; }
            public double Threshold { get; set; }
            public bool CanCommentOnIssue { get; set; }
            public bool CanUpdateIssue { get; set; }
            public string NoAreaDeterminedLabel { get; set; }
        }

        private async Task InnerTask(string owner, string repo, int number)
        {
            var options = GetOptionsFor(owner, repo);
            if (options == null)
            {
                return;
            }
            var labelRetriever = options.LabelRetriever;
            string msg = $"! dispatcher app - started query for {owner}/{repo}#{number}";
            _logger.LogInformation(msg);

            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);

            var labels = new HashSet<string>();
            GithubObjectType issueOrPr = iop.PullRequest != null ? GithubObjectType.PullRequest : GithubObjectType.Issue;

            if (labelRetriever.ShouldSkipUpdatingLabels(iop.User.Login))
            {
                _logger.LogInformation($"! dispatcher app - skipped for racing for {issueOrPr} {number}.");
                return;
            }

            // get non area labels
            labels = await GetNonAreaLabelsAsync(labelRetriever, owner, repo, iop);

            bool foundArea = false;
            string theFoundLabel = default;
            if (!labelRetriever.SkipPrediction)
            {
                // find shortcut to get label
                if (iop.PullRequest != null)
                {
                    string body = iop.Body ?? string.Empty;
                    if (labelRetriever.AllowTakingLinkedIssueLabel)
                    {
                        (string label, int number) linkedIssue = await GetAnyLinkedIssueLabel(owner, repo, body);
                        if (!string.IsNullOrEmpty(linkedIssue.label))
                        {
                            _logger.LogInformation($"! dispatcher app - PR number {iop.Number} fixes issue number {linkedIssue.number} with area label {linkedIssue.label}.");
                            foundArea = true;
                            theFoundLabel = linkedIssue.label;
                        }
                    }
                }

                // then try ML prediction
                if (!foundArea)
                {
                    var labelSuggestion = await GetLabelSuggestion(options.PredictionUrl, owner, repo, number);
                    if (labelSuggestion == null)
                    {
                        _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => _queueHelper.InsertMessageTask($"TODO - Dispatch labels for: /{owner}/{repo}#{number}"));
                        return;
                    }
                    var topChoice = labelSuggestion.LabelScores.OrderByDescending(x => x.Score).First();
                    if (labelRetriever.PreferManualLabelingFor(topChoice.LabelName))
                    {
                        _logger.LogInformation($"#  dispatcher app - skipped: prefer manual prediction instead.");
                    }
                    else if (topChoice.Score >= options.Threshold || labelRetriever.OkToIgnoreThresholdFor(topChoice.LabelName))
                    {
                        foundArea = true;
                        theFoundLabel = topChoice.LabelName;
                    }
                    else
                    {
                        _logger.LogInformation($"! dispatcher app - The Model was not able to assign the label to the {issueOrPr} {number} confidently.");
                    }
                }
            }
            await UpdateTask(options, owner, repo, number, foundArea, labels, theFoundLabel, issueOrPr, labelRetriever);
        }

        private async Task UpdateTask(
            LabelerOptions options,
            string owner, string repo,
            int number,
            bool foundArea,
            HashSet<string> labels,
            string theFoundLabel,
            GithubObjectType issueOrPr,
            ILabelRetriever labelRetriever)
        {

            if (labelRetriever.AddDelayBeforeUpdatingLabels)
            {
                // to avoid race with dotnet-bot
                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            // get iop again
            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);

            var existingLabelList = iop?.Labels?.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name).ToList();
            bool issueMissingAreaLabel = !existingLabelList?.Where(x => x.StartsWith("area-", StringComparison.OrdinalIgnoreCase)).Any() ?? true;

            // update section
            if (labels.Count > 0 || (foundArea && issueMissingAreaLabel))
            {
                //var issueUpdate = iop.ToUpdate();
                var labelsToAdd = new List<string>();

                if (foundArea && issueMissingAreaLabel)
                {
                    // no area label yet
                    labelsToAdd.Add(theFoundLabel);
                }

                labelsToAdd.AddRange(labels);

                if (options.CanUpdateIssue && labelsToAdd.Any())
                {
                    await _gitHubClientWrapper.AddLabels(owner, repo, number, labelsToAdd);
                }
                else if (!options.CanUpdateIssue && labelsToAdd.Any())
                {
                    _logger.LogInformation($"! skipped adding labels for {issueOrPr} {number}. would have been added: {string.Join(",", labelsToAdd)}");
                }
                else
                {
                    _logger.LogInformation($"! dispatcher app - No labels added to {issueOrPr} {number}.");
                }
            }

            // comment section
            if (options.CanCommentOnIssue)
            {
                foreach (var labelFound in labels)
                {
                    var labelComment = labelRetriever.CommentFor(labelFound);

                    if (!string.IsNullOrEmpty(labelComment))
                    {
                        await _gitHubClientWrapper.CommentOn(owner, repo, iop.Number, labelComment);
                    }
                }

                // if newlabels has no area-label and existing does not also. then comment
                if (!foundArea && issueMissingAreaLabel)
                {
                    if (!string.IsNullOrEmpty(options.NoAreaDeterminedLabel))
                    {
                        await _gitHubClientWrapper.AddLabels(owner, repo, iop.Number, new[] { options.NoAreaDeterminedLabel });
                    }
                    else if (labelRetriever.CommentWhenMissingAreaLabel)
                    {
                        if (issueOrPr == GithubObjectType.Issue)
                        {
                            await _gitHubClientWrapper.CommentOn(owner, repo, iop.Number, labelRetriever.MessageToAddAreaLabelForIssue);
                        }
                        else
                        {
                            await _gitHubClientWrapper.CommentOn(owner, repo, iop.Number, labelRetriever.MessageToAddAreaLabelForPr);
                        }
                    }
                }
            }
            else
            {
                _logger.LogInformation($"! dispatcher app - No comment made to labels for {issueOrPr} {number}.");
            }
        }

        private async Task<LabelSuggestion> GetLabelSuggestion(string partUrl, string owner, string repo, int number)
        {
            var predictionUrl = @$"{partUrl}{number}";
            var request = new HttpRequestMessage(HttpMethod.Get, predictionUrl);
            var client = _httpClientFactory.CreateClient();
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                using var responseStream = await response.Content.ReadAsStreamAsync();
                var remotePrediction = await JsonSerializer.DeserializeAsync<RemoteLabelPrediction>(responseStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var predictionList = remotePrediction.LabelScores.Select(ls => new LabelScore()
                {
                    LabelAreaScore = new LabelAreaScore { LabelName = ls.LabelName, Score = ls.Score },
                    Label = default
                }).Select(x => x.LabelAreaScore).ToList();

                _logger.LogInformation("! received prediction: {0}", string.Join(",", predictionList.Select(x => x.LabelName)));

                return new LabelSuggestion()
                {
                    LabelScores = predictionList,
                };
            }
            else
            {
                // queue task again until the suggestion comes back safe
                _logger.LogError($"Could not retrieve label predictions for this issue. Remote HTTP prediction status code {response.StatusCode} from URL '{predictionUrl}'.");
                return null;
            }
        }

        private async Task<(string label, int number)> GetAnyLinkedIssueLabel(string owner, string repo, string body)
        {
            Match match = _regexIssueMatch.Match(body);
            if (match.Success && int.TryParse(match.Groups[2].Value, out int issueNumber))
            {
                return (await TryGetIssueLabelForPrAsync(owner, repo, issueNumber), issueNumber);
            }
            return await Task.FromResult<(string, int)>(default);
        }

        private async Task<HashSet<string>> GetNonAreaLabelsAsync(ILabelRetriever labelRetriever, string owner, string repo, Octokit.Issue iop)
        {
            if (_regex == null)
            {
                _regex = new Regex(@"@[a-zA-Z0-9_//-]+");
            }
            string body = iop.Body ?? string.Empty;
            var userMentions = _regex.Matches(body).Select(x => x.Value).ToArray();
            IssueModel iopModel = null;
            if (iop.PullRequest != null)
            {
                iopModel = await CreatePullRequest(owner, repo, iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
            }
            else
            {
                iopModel = CreateIssue(iop.Number, iop.Title, iop.Body, userMentions, iop.User.Login);
            }
            return labelRetriever.GetNonAreaLabelsForIssueAsync(iopModel);
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
            // TODO: fix failure here seen in logs.
            var response = await httpclient.GetAsync(diff.LocalPath);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return TakeDiffContentReturnMeaning(content.Split("\n"));
        }

        private async Task<string> TryGetIssueLabelForPrAsync(string owner, string repo, int issueNumber)
        {
            var issue = await _gitHubClientWrapper.GetIssue(owner, repo, issueNumber);
            return issue?.Labels?
                .Where(x => !string.IsNullOrEmpty(x.Name))
                .Select(x => x.Name)
                .Where(x => x.StartsWith("area-", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
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

    public interface IGitHubClientWrapper
    {
        Task<Octokit.Issue> GetIssue(string owner, string repo, int number);
        Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number);
        Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number);
        Task AddLabels(string owner, string repo, int number, IEnumerable<string> labels);
        Task CommentOn(string owner, string repo, int number, string comment);
    }
    public class GitHubClientWrapper : IGitHubClientWrapper
    {
        private readonly  ILogger<GitHubClientWrapper> _logger;
        private GitHubClient _client;
        private readonly GitHubClientFactory _gitHubClientFactory;
        private readonly bool _skipAzureKeyVault;

        public GitHubClientWrapper(
            ILogger<GitHubClientWrapper> logger,
            IConfiguration configuration,
            GitHubClientFactory gitHubClientFactory)
        {
            _logger = logger;
            _skipAzureKeyVault = configuration.GetSection("SkipAzureKeyVault").Get<bool>(); // TODO locally true
            _gitHubClientFactory = gitHubClientFactory;

        }

        public async Task<Octokit.Issue> GetIssue(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            Octokit.Issue iop = null;
            try
            {
                iop = await _client.Issue.Get(owner, repo, number);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                iop = await _client.Issue.Get(owner, repo, number);
            }
            return iop;
        }

        public async Task<Octokit.PullRequest> GetPullRequest(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            Octokit.PullRequest iop = null;
            try
            {
                iop = await _client.PullRequest.Get(owner, repo, number);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                iop = await _client.PullRequest.Get(owner, repo, number);
            }
            return iop;
        }

        public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFiles(string owner, string repo, int number)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            IReadOnlyList<PullRequestFile> prFiles = null;
            try
            {
                prFiles = await _client.PullRequest.Files(owner, repo, number);

            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                prFiles = await _client.PullRequest.Files(owner, repo, number);
            }
            return prFiles;
        }

        public async Task AddLabels(string owner, string repo, int number, IEnumerable<string> labels)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            try
            {
                await _client.Issue.Labels.AddToIssue(owner, repo, number, labels.ToArray());
            }
            catch (Exception ex)
            {
                // Log the error and retry the operation once
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                await _client.Issue.Labels.AddToIssue(owner, repo, number, labels.ToArray());
            }
        }

        // lambda -> call and pass a lambda calls create, and if fails remake and call it again.

        public async Task CommentOn(string owner, string repo, int number, string comment)
        {
            if (_client == null)
            {
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
            }
            try
            {
                await _client.Issue.Comment.Create(owner, repo, number, comment);
            }
            catch (Exception ex)
            {
                _logger.LogError($"ex was of type {ex.GetType()}, message: {ex.Message}");
                _client = await _gitHubClientFactory.CreateAsync(_skipAzureKeyVault);
                await _client.Issue.Comment.Create(owner, repo, number, comment);
            }
        }
    }
}
