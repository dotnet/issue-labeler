// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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

        [HttpPost]
        public async Task PostAsync([FromBody]IssueEventPayload data)
        {
            IssueModel issueOrPullRequest = data.Issue ?? data.Pull_Request;
            GithubObjectType issueOrPr = data.Issue == null ? GithubObjectType.PullRequest : GithubObjectType.Issue;
            bool areaLabelAddedOrNewlyEmpty = false;
            var labels = new List<string>();
            int number = issueOrPullRequest.Number;
            if (data.Action == "opened")
            {
                areaLabelAddedOrNewlyEmpty = true;
                var predictedLabels = await Issuelabeler.PredictLabelAsync(number, issueOrPr, Logger, canCommentOnIssue: false);
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
                        areaLabelAddedOrNewlyEmpty = true;
                    }
                }
            }
            else
            {
                Logger.LogInformation($"! The {issueOrPr} {issueOrPullRequest.Number} was {data.Action}.");
            }

            if (areaLabelAddedOrNewlyEmpty)
            {
                if (issueOrPr == GithubObjectType.Issue)
                {
                    labels.Add("untriaged");
                }
            }

            if (labels.Count > 0)
            {
                await Issuelabeler.UpdateAreaLabelAsync(number, issueOrPr, Logger, labels);
            }
        }
    }
}
