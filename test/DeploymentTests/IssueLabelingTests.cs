using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace DeploymentTests;

/// <summary>
/// Executes tests against the issue labeling service to verify it's behaving as expected.
/// </summary>
/// <remarks>
/// These tests invoke the issue labeler through the webhook endpoint, but they cannot validate the results
/// of the app's labeling behavior since there's no output from the requests. To verify the behavior, the
/// application service logs must be reviewed after running these tests to ensure the expected results occurred.
/// <para>
/// The tests use a custom User-Agent that shows up in the HTTP request/response Application logs. The User-Agent
/// is in the format of `DeploymentTests/yyyyMMdd.HHmmss+(IssueLabelingTests;{test-name})`.
/// </para>
/// <para>
/// Preparation steps for seeing the basic HTTP request/response Application logs:
/// 1. Log into the Azure portal: https://portal.azure.com
/// 2. Navigate to the deployed application (e.g. 'dispatcher-app')
/// 3. Wait for everything on the left-nav to load
/// 4. Within the Monitoring section, open 'App Service logs'
/// 5. Ensure 'Application logging (Filesystem)' is enabled. This setting turns itself off after 12 hours.
/// 6. Set the Application logging 'Level' to 'Information'
/// 7. Save changes if needed.
/// 8. Within the Monitoring section, open 'Log stream'
/// 9. Verify that the log stream indicates that you are now connected to the log-streaming service.
/// 
/// Once the above preparation is completed, proceed with running these tests. Each test has a comment for how
/// to validate that the expected behavior occurred. Note that the Log stream has a delay to it and it can take
/// as much as 2 minutes between the time a test is invoked and its corresponding logs are visible in the stream.
/// </para>
/// <para>
/// Preparation steps for seeing the application logger information that shows the background worker logs:
/// 1. From the deployed application in the Azure portal, within the 'Development Tools' section, open 'App Service Editor (Preview)'
/// 2. Click the 'Open Editor' link to open the App Service Editor
/// 3. Open the `web.config` file (e.g. https://dispatcher-app.scm.azurewebsites.net/dev/wwwroot/web.config)
/// 4. Change `stdoutLogEnabled` to "true"
/// 5. On the Azure portal 'Overview' page for the application, Restart the application
/// 6. Under the 'Development Tools' section, go to 'Advanced Tools' and click 'Go'
/// 7. From the top-nav, open a 'Debug Console' (either CMD or PowerShell)
/// 8. Navigate into the 'LogFiles' folder
/// 9. Find the most recent file named 'stdout_yyyyMMddHHmmss_x.log', and copy the URL from its download icon
/// 
/// Here's a PowerShell command to get the most recent 5 stdout_*.log files:
///     ls stdout*.log | sort LastWriteTime -Descending | Select -First 5
/// 
/// Given the URL of the most recent log, you can download the current log file. The file will be written to repeatedly as more
/// logs accumulate. Log data is flushed to disk periodically; you can force the log to be flushed by restarting the service.
/// </para>
/// </remarks>
public class IssueLabelingTests : ServiceTestBase
{
    public IssueLabelingTests() : base(nameof(IssueLabelingTests)) { }

    public enum IssueOrPullRequest
    {
        Issue,
        PullRequest
    }

    /// <summary>
    /// Produces a log result of:
    /// GET /api/webhookissue/test/{org}/{repo}/{id}
    /// </summary>
    [Theory]
    [InlineData("dotnet", "runtime", 42_000)]
    public async Task GET_test_endpoint(string org, string repo, int id)
    {
        HttpResponseMessage? response = await CreateTestHttpClient().GetAsync(new Uri(ServiceTestBase.ServiceWebhookApiRoot, $"test/{org}/{repo}/{id}"));
        Assert.True(response.IsSuccessStatusCode);
    }

    /// <summary>
    /// Produces a log entry in the Application logs and starts a background worker that will yield entries in the stdout log.
    /// </summary>
    /// <remarks>
    /// Application Logs:
    /// POST /api/webhookissue/ ... 443 ... DeploymentTests/yyyyMMdd.HHmmss+(IssueLabelingTests;POST_webhook_endpoint)
    /// <para>
    /// Stdout Logs:
    /// Log entries will be created tracking the results of receiving a webhook event for the issues/prs. These log entries
    /// can be helpful for troubleshooting behavior of the Labeler and/or verifying that the expected results occurred.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("dotnet", "runtime", IssueOrPullRequest.Issue, 42000)]
    [InlineData("dotnet", "roslyn", IssueOrPullRequest.PullRequest, 42000)]
    [InlineData("dotnet", "aspnetcore", IssueOrPullRequest.Issue, 42000)]
    public async Task POST_webhook_endpoint(string org, string repo, IssueOrPullRequest type, int id)
    {
        string issueOrPullRequest = type switch
        {
            IssueOrPullRequest.Issue => "issue",
            IssueOrPullRequest.PullRequest => "pull_request",
            _ => throw new ArgumentException(nameof(type)),
        };

        StringContent requestContent = new StringContent($$"""
            {
                "action": "opened",
                "{{issueOrPullRequest}}": {
                    "number": {{id}}
                },
                "repository": {
                    "full_name": "{{org}}/{{repo}}"
                }
            }
            """, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = await CreateTestHttpClient().PostAsync(ServiceTestBase.ServiceWebhookApiRoot, requestContent);

        Assert.True(response.IsSuccessStatusCode);
    }
}
