# dotnet/issue-labeler

This repo contains several projects that are all used together to operate the issue-labeler system.

1. `src/PredictionEngine` is the class library that contains the ML.NET engine and its prediction logic
1. `src/ModelCreator` is a console app that produces ML.NET models for repositories to be used by the `PredictionEngine`
1. `src/ModelTester` is a console app that can locally use the `PredictionEngine` and show predictions on the console
1. `src/ModelUploader` is a console app that uploads those models to Azure Blob Storage
   - After upload, the app shows Azure app service configuration changes that need to be made to use the new model
1. `src/PredictionService` is an ASP.NET app (Azure App Service) that hosts the ML.NET engine to make predictions
1. `src/ModelWarmup` is a console app that will "wake up" the ASP.NET app and force load the ML.NET models to prepare the app for processing predictions
1. `src/IssueLabelerService` is an ASP.NET app (Azure App Service) that responds to GitHub issue/PR webhooks
   - For each webhook event, it handles the business logic of what actions might need to occur
   - When one of the actions indicates that an area label prediction is needed, it will dispatch a web request out to the `PredictionService` associated with the repo, receive the area label predictions/scores, and update the issue/PR accordingly
   - There is also logic in this application that performs other issue/PR automation, such as conditionally marking issues as `untriaged` or creating comments on issues
1. `src/GitHubHelpers` is a class library containing helpers and wrappers around the GitHub APIs with methods specific to the issue-labeler system's needs

## Intro to issue labeling

This repository contains the source code to train ML models for making label predictions, as well as the code for automatically applying issue labels onto issue/pull requests on GitHub repositories.

This issue-labeler uses [ML.NET](https://github.com/dotnet/machinelearning) to help predict labels on GitHub issues and pull requests.

## Which GitHub repositories use this issue labeler today?

The dotnet organization contains repositories with many incoming issues and pull requests. In order to help with the triage process, issues get categorized with area labels. The issues related to each area get labeled with a specific `area-` label, and then these label assignments get treated as learning data for an issue labeler to be built. 

The following repositories triage their incoming issues semi-automatically, by manually selecting one of top 3 predictions received from a dotnet/issue-labeler:

* [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)
* [dotnet/extensions](https://github.com/dotnet/extensions)
* [dotnet/maui](https://github.com/dotnet/maui)

The following repositories allow dotnet/issue-labeler to automatically set `area-` labels for incoming issues and pull requests using [GitHub Webhooks](https://docs.github.com/en/get-started/customizing-your-github-workflow/exploring-integrations/about-webhooks):

* [dotnet/runtime](https://github.com/dotnet/runtime)
* [dotnet/corefx](https://github.com/dotnet/corefx) (archived)
* [dotnet/roslyn](https://github.com/dotnet/roslyn)
* [dotnet/dotnet-api-docs](https://github.com/dotnet/dotnet-api-docs)
* [dotnet/docker-tools](https://github.com/dotnet/docker-tools)
* [dotnet/dotnet-docker](https://github.com/dotnet/dotnet-docker)
* [dotnet/dotnet-buildtools-prereqs-docker](https://github.com/dotnet/dotnet-buildtools-prereqs-docker)
* [dotnet/sdk](https://github.com/dotnet/sdk)
* [dotnet/source-build](https://github.com/dotnet/source-build)
* [microsoft/dotnet-framework-docker](https://github.com/microsoft/dotnet-framework-docker)
* [microsoft/vscode-dotnettools](https://github.com/microsoft/vscode-dotnettools)

Of course with automatic labeling there is always a margin of error. But the good thing is that the labeler learns from mistakes so long as wrong label assignments get corrected manually.

For some repos, new issues get an `untriaged` label, which then is expected to get removed by the area owner for the assigned area label as they go through their triage process. Once reviewed by the area owner, if they deem the automatic label as incorrect they may remove incorrect label and allow for correct one to get added manually.

## How to use this issue labeler today?

Enabling the issue labeler for a repo entails these steps:

1. Manually apply `area-*` labels to a few hundred issues, which will be used as training data
1. Generate machine learning training data and upload to an Azure Storage container
1. Configure a new or existing Azure Web App with the issue labeler web app
1. Update the issue labeler web app to use the training data
1. Then either:
   * Configure GitHub webhooks for fully automated label application
   * Configure [Hubbup](https://hubbup.io/) to show predictions
   * Use a browser extension to view predictions

And then periodically re-train the machine learning data so that the model can learn from a larger dataset of issues that have correct area labels applied. Note: the system does not learn in real time!

To get started, check out the [docs](Documentation/) for detailed steps to set up the issue labeler for your GitHub repo.

## License

.NET is licensed under the [MIT](LICENSE.TXT) license.
