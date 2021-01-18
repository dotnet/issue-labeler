// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.Github.IssueLabeler;
using Microsoft.DotNet.Github.IssueLabeler.Models;
using Microsoft.DotNet.GitHub.IssueLabeler.Data;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
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
            services.AddSingleton<GitHubClientFactory>();
            services.AddHttpClient();
            services.AddSingleton<IGitHubClientWrapper, GitHubClientWrapper>();
            services.AddSingleton<IModelHolderFactory, ModelHolderFactory>();
            services.AddSingleton<IDiffHelper, DiffHelper>();
            services.AddSingleton<ILabeler, Labeler>();
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
}
