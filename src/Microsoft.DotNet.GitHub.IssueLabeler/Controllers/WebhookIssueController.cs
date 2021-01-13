// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    [Route("api/WebhookIssue")]
    public class WebhookIssueController : Controller
    {
        private ILabeler _labeler { get; set; }

        private ILogger<WebhookIssueController> Logger { get; set; }
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public WebhookIssueController(
            ILabeler labeler,
            ILogger<WebhookIssueController> logger,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _labeler = labeler;
            Logger = logger;
            _backgroundTaskQueue = backgroundTaskQueue;
        }

        [HttpGet("")]
        [HttpGet("/")]
        [HttpGet("Index")]
        public IActionResult Index()
        {
            return Content($"Check the logs, or predict labels.");
            // process has been started > in logger and time
        }

        [HttpGet("test/{owner}/{repo}/{id}")]
        public IActionResult GetPredictionTest(string owner, string repo, int id)
        {
            Logger.LogInformation("! test workflow for dispatch label: {Owner}/{Repo}#{IssueNumber}", owner, repo, id);

            _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => _labeler.DispatchLabelsAsync(owner, repo, id));
            
            return Ok();
        }

        [HttpPost]
        public IActionResult PostAsync([FromBody]IssueEventPayload data)
        {
            IssueModel issueOrPullRequest = data.Issue ?? data.Pull_Request;
            GithubObjectType issueOrPr = data.Issue == null ? GithubObjectType.PullRequest : GithubObjectType.Issue;
            if (data.Action == "opened")
            {
                string owner = data.Repository.Full_Name.Split("/")[0];
                string repo = data.Repository.Full_Name.Split("/")[1];
                Logger.LogInformation("! Webhook call for: {Owner}/{Repo}#{IssueNumber}", owner, repo, issueOrPullRequest.Number);

                _backgroundTaskQueue.QueueBackgroundWorkItem((ct) => _labeler.DispatchLabelsAsync(owner, repo, issueOrPullRequest.Number));
            }
            else if (data.Action == "unlabeled" || data.Action == "labeled")
            {
                if (data.Label != null && !string.IsNullOrEmpty(data.Label.Name))
                {
                    string labelName = data.Label.Name;
                    if (labelName.StartsWith("area-", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogInformation($"! Area label {labelName} for {issueOrPr} {issueOrPullRequest.Number} got {data.Action}.");
                    }
                }
            }
            else
            {
                Logger.LogInformation($"! The {issueOrPr} {issueOrPullRequest.Number} was {data.Action}.");
            }
            return Ok();
        }
    }
}
