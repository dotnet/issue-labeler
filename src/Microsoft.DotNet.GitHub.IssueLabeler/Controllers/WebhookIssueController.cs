// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    [Route("api/WebhookIssue")]
    public class WebhookIssueController : Controller
    {
        private Labeler Issuelabeler { get; set; }
        private ILogger Logger { get; set; }

        public WebhookIssueController(Labeler labeler, ILogger<WebhookIssueController> logger)
        {
            Issuelabeler = labeler;
            Logger = logger;
        }

        [HttpGet("")]
        [HttpGet("/")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return Content($"Check the logs, or predict labels.");
            // process has been started > in logger and time
        }

        [HttpGet("{owner}/{repo}/{id}")]
        public async Task<IActionResult> GetIssueOrPr(string owner, string repo, int id)
        {
            Logger.LogInformation("Prediction for: {Owner}/{Repo}#{IssueNumber}", owner, repo, id);
            // todo: return top 3 predictions only for dotnet/runtime for now
            if (!owner.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
                !repo.Equals("runtime", StringComparison.OrdinalIgnoreCase))
                return NotFound("returning top 3 predictions only for dotnet/runtime for now");
            LabelSuggestion labelSuggestion = await Issuelabeler.JustPredictLabelAsync(id, Logger);
            return Ok(labelSuggestion);
        }

        [HttpPost]
        public async Task PostAsync([FromBody]IssueEventPayload data)
        {
            IssueModel issueOrPullRequest = data.Issue ?? data.Pull_Request;
            GithubObjectType issueOrPr = data.Issue == null ? GithubObjectType.PullRequest : GithubObjectType.Issue;
            var labels = new List<string>();
            int number = issueOrPullRequest.Number;
            if (data.Action == "opened")
            {
                if (issueOrPr == GithubObjectType.Issue)
                {
                    labels.Add("untriaged");
                }
                var predictedLabels = await Issuelabeler.PredictLabelAsync(number, issueOrPr, Logger, canCommentOnIssue: true);
                labels.AddRange(predictedLabels);
            }
            else if (data.Action == "unlabeled" || data.Action == "labeled")
            {
                if (data.Label != null && !string.IsNullOrEmpty(data.Label.Name))
                {
                    string labelName = data.Label.Name;
                    if (labelName.StartsWith("area-"))
                    {
                        Logger.LogInformation($"! Area label {labelName} for {issueOrPr} {issueOrPullRequest.Number} got {data.Action}.");
                    }
                }
            }
            else
            {
                Logger.LogInformation($"! The {issueOrPr} {issueOrPullRequest.Number} was {data.Action}.");
            }

            if (data.Action == "opened")
            {
                await Issuelabeler.UpdateAreaLabelAsync(number, issueOrPr, Logger, labels);
            }
        }
    }
}
