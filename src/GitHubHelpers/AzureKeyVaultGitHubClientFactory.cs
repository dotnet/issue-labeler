// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubJwt;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Octokit;

namespace GitHubHelpers;

public sealed class AzureKeyVaultGitHubClientFactory : IGitHubClientFactory
{
    private readonly IConfiguration _configuration;

    public AzureKeyVaultGitHubClientFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<GitHubClient> CreateAsync()
    {
        // See: https://octokitnet.readthedocs.io/en/latest/github-apps/ for details.

        var appId = Convert.ToInt32(_configuration["GitHubAppId"]);

        AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
        KeyVaultClient keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
        SecretBundle secretBundle = await keyVaultClient.GetSecretAsync(_configuration["AppSecretUri"]).ConfigureAwait(false);
        string privateKey = secretBundle.Value;

        var privateKeySource = new PlainStringPrivateKeySource(privateKey);
        var generator = new GitHubJwtFactory(
            privateKeySource,
            new GitHubJwtFactoryOptions
            {
                AppIntegrationId = appId,
                ExpirationSeconds = 8 * 60 // 600 is apparently too high
            });
        var token = generator.CreateEncodedJwtToken();

        var client = CreateForToken(token, AuthenticationType.Bearer);

        var installations = await client.GitHubApps.GetAllInstallationsForCurrent();
        var installationTokenResult = await client.GitHubApps.CreateInstallationToken(long.Parse(_configuration["InstallationId"]));

        return CreateForToken(installationTokenResult.Token, AuthenticationType.Oauth);
    }

    private static GitHubClient CreateForToken(string token, AuthenticationType authenticationType)
    {
        var productInformation = new ProductHeaderValue("issuelabelertemplate");
        var client = new GitHubClient(productInformation)
        {
            Credentials = new Credentials(token, authenticationType)
        };
        return client;
    }

    public sealed class PlainStringPrivateKeySource : IPrivateKeySource
    {
        private readonly string _key;

        public PlainStringPrivateKeySource(string key)
        {
            _key = key;
        }

        public TextReader GetPrivateKeyReader()
        {
            return new StringReader(_key);
        }
    }
}
