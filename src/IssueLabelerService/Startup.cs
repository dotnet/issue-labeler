// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using GitHubHelpers;
using Microsoft.Extensions.Azure;

namespace IssueLabelerService;

public class Startup
{
    public IConfiguration Configuration { get; }

    public IWebHostEnvironment HostEnvironment { get; }

    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        HostEnvironment = env;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddHostedService<QueuedHostedService>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddSingleton<IGitHubClientFactory, AzureKeyVaultGitHubClientFactory>();
        services.AddHttpClient();
        services.AddSingleton<IQueueHelper, QueueHelper>();
        services.AddSingleton<GitHubClientWrapper, GitHubClientWrapper>();
        services.AddSingleton<Labeler, Labeler>();
        services.AddAzureClients(
            builder => {
                builder.AddBlobServiceClient(Configuration["QConnectionString"]);
            });
    }

    public void Configure(IApplicationBuilder app)
    {
        if (HostEnvironment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
        });
    }

}
