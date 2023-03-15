namespace DeploymentTests
{
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
}