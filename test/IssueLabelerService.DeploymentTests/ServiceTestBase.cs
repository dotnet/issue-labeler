// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

namespace IssueLabelerService.DeploymentTests;

public abstract class ServiceTestBase
{
    public static Uri ServiceRoot = new Uri("https://dispatcher-app.azurewebsites.net/");
    public static Uri ServiceWebhookApiRoot = new Uri(ServiceRoot, "/api/webhookissue/");

    private readonly string _nameofTestClass;

    protected ServiceTestBase(string nameOfTestClass) => _nameofTestClass = nameOfTestClass;

    protected HttpClient CreateTestHttpClient([CallerMemberName] string? nameofTestMethod = null)
    {
        string[] userAgentParts = new[] { _nameofTestClass, nameofTestMethod } switch
        {
            [null, null] => new string[] { },
            [string className, null] => new[] { className },
            [null, string testName] => new[] { testName },
            [string className, string testName] => new[] { className, testName },
            _ => throw new ArgumentException(),
        };

        string userAgent = $"DeploymentTests/{DateTime.Now.ToString("yyyyMMdd.HHmmss")} ({string.Join(';', userAgentParts)})";

        HttpClient testClient = new HttpClient();
        testClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return testClient;
    }
}
