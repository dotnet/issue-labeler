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
    public class Labeler
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
                        CanUpdateLabels = _configuration.GetValue<bool>($"{owner}:{repo}:can_update_labels", false),
                        CanCommentOnIssue = _configuration.GetValue<bool>($"{owner}:{repo}:can_comment_on", false),

                        AreaOwnersDoc = _configuration.GetValue<string>($"{owner}:{repo}:area_owners_doc", null),
                        NewApiPrLabel = _configuration.GetValue<string>($"{owner}:{repo}:new_api_pr_label", null),
                        ApplyLinkedIssueAreaLabelToPr = _configuration.GetValue<bool>($"{owner}:{repo}:apply_linked_issue_area_label_to_pr", false),
                        NoAreaDeterminedSkipComment = _configuration.GetValue<bool>($"{owner}:{repo}:no_area_determined:skip_comment", false),
                        NoAreaDeterminedLabel = _configuration.GetValue<string>($"{owner}:{repo}:no_area_determined:label", null),

                        DelayLabelingSeconds = _configuration.GetValue<int>($"{owner}:{repo}:delay_labeling_seconds", 0),
                        SkipLabelingForAuthors = _configuration.GetValue<string>($"{owner}:{repo}:skip_labeling_for_authors", "").Split(new[] { ',', ';', ' '}),
                        SkipPrediction = _configuration.GetValue<bool>($"{owner}:{repo}:skip_prediction", false),
                        SkipUntriagedLabel = _configuration.GetValue<bool>($"{owner}:{repo}:skip_untriaged_label", false),
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
            public LabelRetriever LabelRetriever { get; init; }
            public string PredictionUrl { get; init; }
            public double Threshold { get; init; }
            public bool CanCommentOnIssue { get; init; }
            public bool CanUpdateLabels { get; init; }

            public string AreaOwnersDoc { get; init; }
            public string NewApiPrLabel { get; init; }
            public bool ApplyLinkedIssueAreaLabelToPr { get; init; }
            public bool NoAreaDeterminedSkipComment { get; init; }
            public string NoAreaDeterminedLabel { get; init; }

            public int DelayLabelingSeconds { get; init; }
            public string[] SkipLabelingForAuthors { get; init; }
            public bool SkipPrediction { get; init; }
            public bool SkipUntriagedLabel { get; init; }
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

            if (options.SkipLabelingForAuthors.Contains(iop.User.Login, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"! dispatcher app - skipped labeling for author '{iop.User.Location}' on {owner}/{repo}#{number} ({issueOrPr}).");
                return;
            }

            // get non area labels
            labels = await GetNonAreaLabelsAsync(options, owner, repo, iop);

            bool foundArea = false;
            string theFoundLabel = default;

            if (!options.SkipPrediction)
            {
                // find shortcut to get label
                if (iop.PullRequest != null)
                {
                    string body = iop.Body ?? string.Empty;
                    if (options.ApplyLinkedIssueAreaLabelToPr)
                    {
                        (string label, int number) linkedIssue = await GetAnyLinkedIssueLabel(owner, repo, body);
                        if (!string.IsNullOrEmpty(linkedIssue.label))
                        {
                            _logger.LogInformation($"! dispatcher app - PR number {owner}/{repo}#{number} fixes issue number {linkedIssue.number} with area label {linkedIssue.label}.");
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
                        _logger.LogInformation($"#  dispatcher app - skipped for {owner}/{repo}#{number} ({issueOrPr}): prefer manual prediction instead.");
                    }
                    else if (topChoice.Score >= options.Threshold)
                    {
                        foundArea = true;
                        theFoundLabel = topChoice.LabelName;
                    }
                    else
                    {
                        _logger.LogInformation($"! dispatcher app - The Model was not able to assign the label to {owner}/{repo}#{number} ({issueOrPr}) confidently.");
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
            HashSet<string> labelsToAdd,
            string theFoundLabel,
            GithubObjectType issueOrPr,
            LabelRetriever labelRetriever)
        {
            if (options.DelayLabelingSeconds > 0)
            {
                // to avoid race with dotnet-bot
                await Task.Delay(TimeSpan.FromSeconds(options.DelayLabelingSeconds));
            }

            // get iop again to retrieve its labels
            var iop = await _gitHubClientWrapper.GetIssue(owner, repo, number);
            var existingLabelList = iop?.Labels?.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.Name).ToList();

            // Check for existing area labels or the label indicating no area was determined
            bool hasAnAreaLabelAlready = existingLabelList?.Any(x => x.StartsWith("area-", StringComparison.OrdinalIgnoreCase)) ?? false;
            bool hasNeedsAreaLabelAlready = !string.IsNullOrEmpty(options.NoAreaDeterminedLabel) && existingLabelList is not null ? existingLabelList.Any(x => x.Equals(options.NoAreaDeterminedLabel, StringComparison.OrdinalIgnoreCase)) : false;

            if (!hasAnAreaLabelAlready && !hasNeedsAreaLabelAlready)
            {
                if (foundArea)
                {
                    labelsToAdd.Add(theFoundLabel);
                }
                else if (!string.IsNullOrEmpty(options.NoAreaDeterminedLabel))
                {
                    labelsToAdd.Add(options.NoAreaDeterminedLabel);
                }
            }

            if (labelsToAdd.Any())
            {
                if (options.CanUpdateLabels)
                {
                    await _gitHubClientWrapper.AddLabels(owner, repo, number, labelsToAdd);
                }
                else if (!options.CanUpdateLabels)
                {
                    _logger.LogInformation($"! skipped adding labels for {owner}/{repo}#{number} ({issueOrPr}). would have been added: {string.Join(",", labelsToAdd)}");
                }
                else
                {
                    _logger.LogInformation($"! dispatcher app - No labels added to {owner}/{repo}#{number} ({issueOrPr}).");
                }
            }

            // comment section
            if (options.CanCommentOnIssue)
            {
                if (!string.IsNullOrWhiteSpace(options.NewApiPrLabel) && labelsToAdd.Contains(options.NewApiPrLabel))
                {
                    string newApiComment = labelRetriever.GetMessageToAddDocForNewApi(options.NewApiPrLabel);
                    await _gitHubClientWrapper.CommentOn(owner, repo, iop.Number, newApiComment);
                }

                // If there's no area label yet and we didn't find an area, optionally leave a comment
                if (!foundArea && !hasAnAreaLabelAlready && !options.NoAreaDeterminedSkipComment)
                {
                    if (issueOrPr == GithubObjectType.Issue)
                    {
                        await _gitHubClientWrapper.CommentOn(owner, repo, iop.Number, labelRetriever.GetMessageToAddAreaLabelForIssue(options.AreaOwnersDoc));
                    }
                    else
                    {
                        await _gitHubClientWrapper.CommentOn(owner, repo, iop.Number, labelRetriever.GetMessageToAddAreaLabelForPr(options.AreaOwnersDoc));
                    }
                }
            }
            else
            {
                _logger.LogInformation($"! dispatcher app - No comment made to labels for {owner}/{repo}#{number} ({issueOrPr}).");
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

                _logger.LogInformation($"! received prediction for {owner}/{repo}#{number}: {0}", string.Join(",", predictionList.Select(x => x.LabelName)));

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

        private async Task<HashSet<string>> GetNonAreaLabelsAsync(LabelerOptions options, string owner, string repo, Octokit.Issue iop)
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

            HashSet<string> nonAreaLabelsToAdd = new HashSet<string>();

            if (iopModel is PrModel pr)
            {
                if (!string.IsNullOrWhiteSpace(options.NewApiPrLabel) && pr.ShouldAddDoc)
                {
                    nonAreaLabelsToAdd.Add(options.NewApiPrLabel);
                }

                if (pr.Author.Equals("monojenkins"))
                {
                    nonAreaLabelsToAdd.Add("mono-mirror");
                }
            }

            if (iopModel is IssueModel issue)
            {
                if (!options.SkipUntriagedLabel) nonAreaLabelsToAdd.Add("untriaged");
            }

            return nonAreaLabelsToAdd;
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
                    _logger.LogInformation($"! problem with new approach on PR {owner}/{repo}#{number}: " + ex.Message);
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
