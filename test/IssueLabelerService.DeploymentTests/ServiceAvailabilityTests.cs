// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace IssueLabelerService.DeploymentTests;

public class ServiceAvailabilityTests : ServiceTestBase
{
    public ServiceAvailabilityTests() : base(nameof(ServiceAvailabilityTests)) { }

    [Fact]
    public async Task RootUrlResponds()
    {
        string response = await CreateTestHttpClient().GetStringAsync(ServiceTestBase.ServiceRoot);
        Assert.Equal("Check the logs, or predict labels.", response);
    }

    [Fact]
    public async Task WebhookApiRootResponds()
    {
        string response = await CreateTestHttpClient().GetStringAsync(ServiceTestBase.ServiceWebhookApiRoot);
        Assert.Equal("Check the logs, or predict labels.", response);
    }
}