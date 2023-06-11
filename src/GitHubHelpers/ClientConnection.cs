// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Microsoft.Extensions.Configuration;

namespace GitHubHelpers;

public static class ClientConnection
{
    public static string GetGitHubAuthToken()
    {
        const string UserSecretKey = "GitHubAccessToken";

        var config = new ConfigurationBuilder()
            .AddUserSecrets("dotnet-issue-labeler")
            .Build();

        var gitHubAccessToken = config[UserSecretKey];
        if (string.IsNullOrEmpty(gitHubAccessToken))
        {
            throw new InvalidOperationException($"Couldn't find User Secret named '{UserSecretKey}' in configuration.");
        }
        return gitHubAccessToken;
    }

    public static GraphQLHttpClient CreateGraphQLClient()
    {
        var gitHubAccessToken = ClientConnection.GetGitHubAuthToken();

        var graphQLHttpClient = new GraphQLHttpClient("https://api.github.com/graphql", new NewtonsoftJsonSerializer());
        graphQLHttpClient.HttpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                scheme: "bearer",
                parameter: gitHubAccessToken);
        return graphQLHttpClient;
    }
}