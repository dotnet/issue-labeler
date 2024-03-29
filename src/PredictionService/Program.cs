// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace PredictionService;

public class Program
{
    public static void Main(string[] args)
    {
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webHostBuilder =>
            {
                webHostBuilder.UseStartup<Startup>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddAzureWebAppDiagnostics();
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                if (context.HostingEnvironment.IsDevelopment())
                {
                    config.AddUserSecrets("dotnet-issue-labeler");
                }
            })
            .Build()
            .Run();
    }
}
