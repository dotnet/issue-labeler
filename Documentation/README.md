# DotNet Issue Labeler

This document describes how to set up a new issue labeler app (for a new GitHub repo), as well as how to update the training data for an existing labeler app and repo.

Note: Several parts of this document have steps that require particular membership and permissions for various GitHub repos, Azure subscriptions and resources, and so forth. You may have to request permissions or send PRs to make certain changes.

## Add support for a new GitHub repo

If your repo is not yet set up for issue labeling, there are two options:

1. Use an existing labeler app that supports the same GitHub org and add your repo's data to it
1. Create a new labeler app and add your repo's data to it

Please note that a single labeler app can support only one org, so if your org isn't supported yet, you must create a new labeler app instance.

If your are adding your repo to an existing labeler, see the [Web App Service List](#web-app-service-list) for a list of existing apps.

Follow these steps to create a new labeler app:

1. To create a new labeler, you must have access to the `GitHubIssueLabeller` resource group in the `DDFun IaaS Dev Shared Public` Azure Subscription. Contact the DevDiv Azure Ops email alias to request access.
1. Go to the Azure Portal to create a new web app: https://ms.portal.azure.com/#create/Microsoft.WebSite
1. Select these settings:
   - **Subscription**: DDFun IaaS subscription
   - **Resource Group**: GitHubIssueLabeller
   - **Name**: Arbitrary, but try to follow a pattern like `nuget-home-labeler`
   - **Publish**: Code
   - **Runtime stack**: .NET 6 (LTS)
   - **OS**: Windows
   - **Region**: West US or similar is best, so that it is nearer to other resources in this resource group, and where there are existing App Service Plans that can be shared
   - **App Service Plan**: If possible, pick an existing service plan (all apps in the same service plan share the same machine resources, so don't put "too much" into one). Otherwise create a new service plan of an appropriate size. How big is big enough? Memory is the largest concern, so start small, and upgrade to more memory if it runs out.
1. Click **Review and Create**, and then **Create**
1. Go to the new Web App resource you created
1. Go to **Settings** / **Configuration**
   - In General Settings make sure these are set:
     - **Platform**: 64bit
     - **Always on**: On
     - And click **Save**
   - In Application settings set the following common settings:
     - `AppSecretUri`: Copy the value from any of the other labeler apps
     - `BlobContainer`: `areamodels`
     - `QConnectionString`: Copy the value from any of the other labeler apps
     - `RepoOwner`: The owner of the repo as seen in the GitHub repo URL. For example, the repo `https://github.com/ABC/XYZ` would have `ABC` as the owner.
     - `GitHubAppId`: Copy this from another labeler app that targets the *same* GitHub org (for example, the `dotnet`, `microsoft`, or `nuget` org)
     - `InstallationId`: Copy this from another labeler app that targets the *same* GitHub org (for example, `dotnet`, `microsoft`, or `nuget` org)
     - And click **Save**
1. Go to **Settings** / **Identity**
   - Under System Assigned, select **Status: On**, and click **Save**, and then **Yes**. This identity will enable the app to access Azure Key Vault secrets
1. Configure Key Vault access
   - In the same DDFun IaaS subscription, go to the **Mirror** Key Vault resource
     - Select **Access Policies**
     - Click **Create**
     - Select the following **Secret Permissions**: `Get`, `List` (note: _not_ Key or Certificat permissions!)
     - Click **Next**
     - Search in the list for the Web App name that you created (example: `nuget-home-labeler`)
     - Click **Next**
     - Don't select anything for Application
     - Click **Next**
     - Click **Create**
1. Publish the labeler app from Visual Studio
   - Clone the https://github.com/dotnet/issue-labeler repo
   - Open the **issue-labeler.sln** solution in Visual Studio
   - Check that you can build the **Microsoft.DotNet.Github.IssueLabeler** project
   - Right-click on the **Microsoft.DotNet.Github.IssueLabeler** project and select **Publish...**
   - Create a new Publish Profile and select **Azure** as the target, then pick **Azure App Service (Windows)** as the specific target
   - Select the **DDFun IaaS Dev Shared Public** subscription (formerly known as **DDITPublic**)
   - Select the App Service instance that you created earlier (for example, `nuget-home-labeler`)
   - Deployment type: Publish
   - Click **Finish**, then **Close**
   - In the Settings screen click one of the pencil icons to edit the Settings and make sure the following are set:
     - **Configuration**: Release
     - **Target framework**: net6.0
     - **Deployment mode**: Self-contained
     - **Target Runtime**: win-x64
     - Click **Save**
   - Click **Publish** (this will build the app and upload to Azure, and can take a few minutes)
1. The publish operation should launch your web browser to its URL and you should see the message `Check the logs, or predict labels.`, indicating the app is running
   - Note that the app is not yet configured to predict anything! It's just an app with no data.
   - You will need to follow the steps to create and upload prediction models and configure the app to serve those predictions.


## Create and upload prediction models to Azure Storage

If the label training model for your repo is out of date or non-existent, you will need to create a new model, upload it to Azure Storage, and update the predictor app to use the new model.

Pre-requisites:

1. The repo must already have "area" labels that follow the pattern `area/some_name`, `area:some_name`, or `area-some_name`, and a minimum of 500 issues labeled with at least one area label. If there are too few labeled issues, the model will not be reliable. Also, areas are ideally exclusive, meaning that issues or PRs should have exactly one label. It's acceptable to have more than one area label on an item, but those are not reliable for generating models.
1. To upload models to Azure, you must have access to the `GitHubIssueLabeller` resource group in the `DDFun IaaS Dev Shared Public` Azure Subscription. Contact the DevDiv Azure Ops email alias to request access.

To get started, clone the https://github.com/dotnet/issue-labeler repo so that you can run the required tools.

1. Create training model on your machine
   1. Open a command prompt in the `src/CreateMikLabelModel` folder of the issue-labeler repo
   1. If you have not yet done so, create a GitHub OAuth token to use for this app:
      1. Create a [GitHub Personal Access Token](https://github.com/settings/tokens) and copy the value to your clipboard
      1. In the command prompt run: `dotnet user-secrets set GitHubAccessToken THE_TOKEN_VALUE`.
   1. Run the tool for the repo you wish to use: `dotnet run -- owner/repo`, for example, `dotnet run -- dotnet/maui`
      1. If you get an error that the repo is not listed, edit the `repos.json` file and add your repo. And then also send a PR to have the list updated in the issue labeler repo.
      1. The tool will download all the GitHub issues and PRs from the repo. This can take anywhere from a few minutes to even 10 minutes, depending on how many issues/PRs exist in the repo.
      1. It will then run a computationally intensive process to perform the machine learning. This can take around 30 minutes on a high-end workstation.
      1. The output will be two ZIP files containing the models for issues and PRs. If the repo has no PRs, that model will be skipped.
1. Upload model to Azure storage
   1. Open a command prompt in the `src/DotNetLabelerUploader` folder of the issue-labeler repo
   1. Get the Azure Storage access key for the `dotnetissuelabelerdata` storage account in Azure
      1. In the Azure Portal, go to the DDFun IaaS Dev Shared Public subscription, and select the `dotnetissuelabelerdata` storage account
      1. Select **Access keys** from the left side menu
      1. Copy the Key value for key1 or key2.
      1. In the command prompt run: `dotnet user-secrets set IssueLabelerKey AZURE_KEY_HERE`.
   1. Run the tool for the repo data you wish to upload: `dotnet run -- C:\PATH\TO\ZIPS OWNER/REPO`, for example, `dotnet run -- C:\GitHub\issue-labeler\src\CreateMikLabelModel dotnet/maui`
1. Update predictor app to point to the new model
   1. The uploader tool from the previous step printed out further instructions how to do that. It looks something like this:
      1. Go to https://portal.azure.com/
      1. Go to this subscription: DDFun IaaS Dev Shared Public
      1. Go to the appropriate App Service resource for this repo (see Web App Service List in this document)
      1. Select Configuration
      1. Set Application Setting `IssueModel:SOME_REPO:BlobName` to: `owner-repo-il-03.zip`
      1. Set Application Setting `IssueModel:SOME_REPO:PathPrefix` to a new value, typically just incremented by one (the exact name doesn't matter)
      1. Set Application Setting `PrModel:SOME_REPO:BlobName` to: `owner-repo-pr-03.zip`
      1. Set Application Setting `PrModel:SOME_REPO:PathPrefix` to a new value, typically just incremented by one (the exact name doesn't matter; common pattern is to use `GHM-[ZIP_FILE_NAME_WITHOUT_EXTENSION]`, such as GHM-aspnetcore-issue-03)
      1. Click Save and accept the confirmation, which will restart the application and start using the new values
      1. Run the DotNetLabelerWakerUpper tool in this repo to re-load the new models and check that they are all working.

## Test prediction models and warm up the predictor apps

Once the new model is uploaded, you'll need to warm up the predictor app and test that it is predicting labels for issues and PRs in that repo.

1. Open a command prompt in the `src/DotNetLabelerWakerUpper` folder of the issue-labeler repo
1. Run `dotnet run`

This will call the `load` API of each known labeler app, wait for it to be ready, and then get label predictions for arbitrary issues and PRs in that repo. This can take a few minutes to run.

Note: If you added a new repo, please edit the `src/DotNetLabelerWakerUpper/appSettings.json` file in the issue labeler repo and add two issues and two PRs to the list so that the waker-upper tool can warm up the predictions for the new repo as well.

## Use predicted labels in your repo

Once you have configured a predictor and uploaded a training model, there are three patterns for predicting labels in your repo:

1. Use a GitHub Webhook to automatically apply the best predicted area label to a new issue or PR
1. Use Hubbup's MikLabel UI to view predicted labels and apply them
1. Use an Edge/Chrome browser extension to view predicted labels and apply them

### Setting up GitHub Webhooks for fully automated labeling

TODO: How to do this?

### Use Hubbup to view label predictions

TODO: Contact Eilon Lipton

### Use a browser extension to view label predictions

TODO: Contact Eilon Lipton

## Web App Service List

There are several Web App Services running, each configured for predictions for a set of repos. Note that a Web App Service can support multiple repos in the same GitHub org, but cannot support multiple orgs.

If you are adding a new repo and the repo is in the same org as one of the repos below, consider adding your repo's data to an existing app (but ask the existing users for permission first!).

* App: dotnet-aspnetcore-labeler
  1. dotnet/aspnetcore issue+pr
  1. dotnet/maui issue+pr
  1. dotnet/roslyn issue+pr (not actually used!)
* App: dotnet-roslyn-labeler
  1. dotnet/roslyn issue+pr
  1. dotnet/source-build issue
* App: dotnet-runtime-issue-labeler
  1. dotnet/docker-tools issues
  1. dotnet/dotnet-api-docs issues+pr
  1. dotnet/dotnet-buildtools-prereqs-docker issues
  1. dotnet/dotnet-docker issues
  1. dotnet/runtime issues+pr
  1. dotnet/sdk issues+pr
* App: microsoft-dotnet-framework-docker
  1. microsoft/dotnet-framework-docker issues
* App: nuget-home-labeler
  1. nuget/home issues

## Further reading

* [Implementation details](./Implementation.md) - more details on how certain projects in this repo work
