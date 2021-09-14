// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CreateMikLabelModel.DL
{
    public static class OctokitDownloadHelper
    {
        private static GitHubClient _client;
        static OctokitDownloadHelper()
        {
            var productInformation = new ProductHeaderValue("MLGitHubLabeler");
            var token = CommonHelper.GetGitHubAuthToken();

            _client = new GitHubClient(productInformation)
            {
                Credentials = new Credentials(token)
            };
        }

        public static async Task<(string, long, string, bool)> CanPickAsManuallyAssigned(
            int number, string owner, string repo,
            string areaLabel, string createdAtString)
        {
            var events = await _client.Issue.Events.GetAllForIssue(owner, repo, number);
            bool canPick = events
                .OrderBy(x => x.CreatedAt)
                .Where(
                    @event => @event.Label != null &&
                    @event.Event == EventInfoState.Labeled &&
                    // if user is deleted login is null, as ghost user
                    (@event.Actor == null || !@event.Actor.Login.Equals("Dotnet-GitSync-Bot", StringComparison.OrdinalIgnoreCase)) &&
                    @event.Label.Name.Equals(areaLabel, StringComparison.OrdinalIgnoreCase)
                ).Any();

            //Trace.WriteLine($"{@event.Actor.Login} labeled {owner}/{repo}#{number} to: {@event.Label.Name}, not the bot");
            return (createdAtString, number, repo, canPick);
        }

        public static async Task<bool> CanPickAsManuallyAssigned(
            int number, string owner, string repo,
            string areaLabel)
        {
            try
            {
                var events = await _client.Issue.Events.GetAllForIssue(owner, repo, number);
                bool canPick = events
                    .OrderBy(x => x.CreatedAt)
                    .Where(
                        @event => @event.Label != null &&
                        @event.Event == EventInfoState.Labeled &&
                        !@event.Actor.Login.Equals("Dotnet-GitSync-Bot", StringComparison.OrdinalIgnoreCase) &&
                        @event.Label.Name.Equals(areaLabel, StringComparison.OrdinalIgnoreCase)
                    ).Any();

                foreach (var @event in events.OrderBy(x => x.CreatedAt))
                {
                    if (@event.Label != null &&
                        @event.Event == EventInfoState.Labeled &&
                        !@event.Actor.Login.Equals("Dotnet-GitSync-Bot", StringComparison.OrdinalIgnoreCase) &&
                        @event.Label.Name.Equals(areaLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        Trace.WriteLine($"{@event.Actor.Login} labeled {owner}/{repo}#{number} to: {@event.Label.Name}, not the bot");
                    }
                }
                return canPick;
            }
            catch (Exception rex)
            {
                Trace.WriteLine(rex.Message);
                throw rex;
            }
        }

        internal static async Task<HashSet<(DateTimeOffset, int, string)>> FindIssueOrPrsWithAreaLabels(string owner, string name)
        {
            var rir = new RepositoryIssueRequest()
            {
                State = ItemStateFilter.All
            };
            var issues = await _client.Issue.GetAllForRepository(owner, name, rir);
            var labeledIssues = issues.Where(issue => issue.Labels.Where(x => LabelHelper.IsAreaLabel(x.Name)).Any());
            var oddItems = labeledIssues.Where(x => !x.HtmlUrl.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (oddItems.Any())
            {
                Trace.WriteLine($"What is wrong with this? #{oddItems.First().Number} with htmlUrl: {oddItems.First().HtmlUrl} ");
            }
            return labeledIssues.Select(x => (x.CreatedAt, x.Number, name)).ToHashSet();
        }

        public static async Task<Dictionary<(DateTimeOffset, long, string), string>> DownloadMissingIssueAndPrsAsync(
            Dictionary<(DateTimeOffset, long, string), string> lookup,
            (string owner, string repo) repoCombo, (int, int) missingCount)
        {
            var labeledIssues = await FindIssueOrPrsWithAreaLabels(repoCombo.owner, repoCombo.repo);

            var labeledIopNumbers = labeledIssues
                .Where(x => x.Item3.Equals(repoCombo.repo, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Item2)
                .ToHashSet();

            var ordered = lookup
                .OrderBy(x => x.Key.Item1.UtcDateTime.ToFileTimeUtc())  //-> first by created date
                .ThenBy(x => x.Key.Item3)                               //-> then by repo name
                .ThenBy(x => x.Key.Item2);                          //-> then by issue number

            var missingIssuePrs = new HashSet<int>();
            var thoseNotTransferred = ordered
                .Where(x => x.Key.Item3.Equals(repoCombo.repo, StringComparison.OrdinalIgnoreCase));
            if (thoseNotTransferred.Any())
            {
                var last = thoseNotTransferred.Last().Key.Item2;

                var existingLookup = thoseNotTransferred
                    .ToDictionary(x => (x.Key.Item1, (int)x.Key.Item2, x.Key.Item3), x => x.Value);

                var existingNonTransferredLookup = existingLookup
                    .Where(x => x.Key.Item3.Equals(repoCombo.repo, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Key.Item2)
                    .ToHashSet();

                foreach (var labeledIssue in labeledIopNumbers)
                {
                    if (!existingNonTransferredLookup.Contains(labeledIssue))
                    {
                        missingIssuePrs.Add(labeledIssue);
                    }
                }

                Trace.WriteLine($"There were reported {missingCount.Item1} missing issues and {missingCount.Item2} missing Prs left to download, a total of {missingCount.Item1 + missingCount.Item2}.");
                Trace.WriteLine($"Found out there is actually {missingIssuePrs.Count()} missing issue/Prs not downloaded (the rest had more than one area-* label on them):");
                Trace.WriteLine($"{string.Join(", ", missingIssuePrs)}.");

                var more = await DownloadRemainingAsync(
                    existingLookup.Keys.Select(x => (long)x.Item2).ToHashSet(), 
                    1, 
                    (int)last, 
                    repoCombo,
                    CommonHelper.GetCompressedLine,
                    labeledIopNumbers,
                    missingIssuePrs);

                Trace.WriteLine($"Adding {more.Count()} more items.");
                lookup.AddRange(more);
            }
            RedoStatCalculation(lookup, repoCombo, missingCount, labeledIssues);
            return lookup;
        }

        public static void RedoStatCalculation(
            Dictionary<(DateTimeOffset, long, string), string> lookup,
            (string owner, string repo) repoCombo,
            (int, int) missingCount,
            HashSet<(DateTimeOffset, int, string)> labeledIssues)
        {
            var ordered = lookup
                .OrderBy(x => x.Key.Item1.UtcDateTime.ToFileTimeUtc())  //-> first by created date
                .ThenBy(x => x.Key.Item3)                               //-> then by repo name
                .ThenBy(x => x.Key.Item2);                          //-> then by issue number

            var missingIssuePrs = new HashSet<(DateTimeOffset, long, string)>();
            var thoseNotTransferred = ordered
                .Where(x => x.Key.Item3.Equals(repoCombo.repo, StringComparison.OrdinalIgnoreCase));
            if (thoseNotTransferred.Any())
            {
                var existingLookup = thoseNotTransferred.ToDictionary(x => (x.Key.Item1, (int)x.Key.Item2, x.Key.Item3), x => x.Value);

                foreach (var labeledIssue in labeledIssues)
                {
                    if (!existingLookup.ContainsKey(labeledIssue))
                    {
                        missingIssuePrs.Add(labeledIssue);
                    }
                }

                Trace.WriteLine($"We have {missingIssuePrs.Count()} remaining missing issue/Prs not downloaded.");
                var inRepoIops = missingIssuePrs.Where(x => x.Item3.Equals(repoCombo.repo, StringComparison.OrdinalIgnoreCase));
                if (inRepoIops.Any())
                    Trace.WriteLine($"The missing {inRepoIops.Count()} issue/Pr numbers in {repoCombo.owner}/{repoCombo.repo} are: {string.Join(", ", inRepoIops.Select(x => x.Item2))}.");
            }
        }

        private static async Task<Dictionary<(DateTimeOffset, long, string), string>> DownloadRemainingAsync(
            HashSet<long> toSkip, int startIndex, int endIndex, 
            (string owner, string repo) repoCombo,
            Func<List<string>, string, string, string, string, DateTimeOffset, int, string, bool, string> getCompressedLine,
            HashSet<int> issuesWithAreaLabel, HashSet<int> missingIssueAndPrs)
        {
            int mod = 500;
            int counter = 0;
            bool isRuntimeRepo = repoCombo.repo.Equals("runtime", StringComparison.OrdinalIgnoreCase);
            var lookup = new Dictionary<(DateTimeOffset, long, string), string>();
            for (int i = startIndex; i < endIndex; i++)
            {
                if (toSkip.Contains((long)i) || !issuesWithAreaLabel.Contains(i))
                {
                    if (missingIssueAndPrs.Contains(i))
                        Trace.WriteLine($"1. Odd turnout for #{i}");
                    continue;
                }
                if ((i == 6 || i == 7) && isRuntimeRepo) // issues #6 and #7 have too many file changes. ignore!
                {
                    if (!missingIssueAndPrs.Contains(i))
                        Trace.WriteLine($"2. Odd turnout for #{i}");
                    continue;
                }
                if (counter++ % mod == 0)
                    Trace.WriteLine($"Downloading more... now at #{i}.");
                if (!missingIssueAndPrs.Contains(i))
                        Trace.WriteLine($"3. Odd turnout for #{i}");
                List<string> filePaths = null;// string.Empty;
                bool isPr = true;
                Issue issueOrPr = null;
                string areaLabel = null;
                try
                {
                    issueOrPr = await _client.Issue.Get(repoCombo.owner, repoCombo.repo, i).ConfigureAwait(false);
                    var areaLabelPerhaps = issueOrPr.Labels.Where(x => LabelHelper.IsAreaLabel(x.Name));
                    if (!areaLabelPerhaps.Any())
                        continue; // don't care about unlabeled iops
                    areaLabel = areaLabelPerhaps.Select(x => x.Name).First();
                    isPr = issueOrPr.PullRequest != null;
                    if (isPr)
                    {
                        var prFiles = await _client.PullRequest.Files(repoCombo.owner, repoCombo.repo, i).ConfigureAwait(false);
                        filePaths = prFiles.Select(x => x.FileName).ToList();
                        //filePaths = String.Join(";", prFiles.Select(x => x.FileName));
                    }
                }
                catch (NotFoundException)
                {
                    Trace.WriteLine($"Issue #{i} not found. Will skip and continue to next.");
                    continue;
                }
                catch (RateLimitExceededException ex)
                {
                    TimeSpan timeToWait = ex.Reset.AddMinutes(1) - DateTimeOffset.UtcNow;
                    Trace.WriteLine($"rate limit exceeded while downloading {i}. threw: {ex.Message}");
                    Trace.WriteLine($"adding a delay here until this limit gets resolved... please wait!");
                    await Task.Delay((int)timeToWait.TotalMilliseconds).ConfigureAwait(false);
                    i--;
                    continue;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"downloading #{i} threw (Will skip and continue to next): {ex.Message}");
                    continue;
                }

                // analyze issue or PR here

                /* GetCompressedLine(
                List<string> filePaths,
                string area,
                string authorLogin,
                string prbody,
                string prtitle,
                DateTimeOffset prcreatedAt,
                int prnumber,
                string repo,
                bool isPr)
                 */
                lookup.Add((issueOrPr.CreatedAt, (long)issueOrPr.Number, repoCombo.repo), getCompressedLine(
                    filePaths,
                    areaLabel,
                    issueOrPr.User.Login,
                    issueOrPr.Body,
                    issueOrPr.Title,
                    issueOrPr.CreatedAt,
                    issueOrPr.Number,
                    repoCombo.repo,
                    isPr));
            }
            return lookup;
        }

    }
}
