
# Implementation details

The following sections help better understand how some of the projects in this repo work.

## Downloading GitHub issue data and training ML models

We use GraphQL and [Octokit](https://www.nuget.org/packages/Octokit/) to download issues from GitHub and then train models using [ML.NET](ML.NET). e.g. dotnet/runtime repository has been trained on over 30,000 issues, and 5,000 PRs which have been labeled in the past, either manually or automatically.

## ModelCreator

The [ModelCreator](https://github.com/dotnet/issue-labeler/tree/master/src/ModelCreator) project is responsible for:

1. Downloading GitHub issues and pull requests
2. Specifying which data to download (title, description, labels, author, mentions, PR file names, optionally PR diff etc.)
3. Segmenting issue or PR records into train (first 80%), validate (second 10%), and test (last 10%) data.
4. Customizing ML training settings: ML models to skip/consider (e.g. FastTreeOva), time to train, information to consider while training (e.g. number of file changes).
5. Optionally testing the ML generated Models to help understand which area labels may be getting more missed predictions or lower confidence compared to others.

### ML customization

As seen in [commit](https://github.com/dotnet/issue-labeler/commit/77e4dbc45184f34e940c0f3cba57160e30c2c183), the [ExperimentModifier](https://github.com/maryamariyan/issue-labeler-2/blob/213a96cf88d31333295126e7815c4688c2e31b54/src/CreateMikLabelModel/ML/ExperimentModifier.cs) class in ModelCreator project helps configure how the models should be trained (what column information to use (e.g. issue Description), how to treat them (as Text, Categorical data, Numeric or Ignore), how long to let the experiment run, and which algorithms to let AutoML consider while training (FastTreeOva, LightGbm, etc.)).

## ModelTester

After creating models with `ModelCreator`, the `ModelTester` console application can be used to test the model locally before loading it into Azure.

## ModelUploader

This console application consumes the ZIP files produced by `ModelCreator` and uploads them to Azure Blob Storage. After upload, instructions are provided for updating the `PredictionService` configuration to use the new models.

## ModelWarmup

After uploading new models to Azure and configuring the `PredictionService` to use the new models, the `ModelWarmup` console application can be used to load and warm up the models by issuing requests to the `PredictionService` for all of the repositories' models hosted by that service.

## PredictionService

The [PredictionService](https://github.com/dotnet/issue-labeler/tree/master/src/PredictionService) project is the web application that uses ML models created using `ModelCreator` to predict area labels. Given repository owner/name/number combination, the `PredictionService` app provides an API returning top three predictions along with their confidence score. This information is computed using the ML models loaded in memory uploaded from Azure Blob Storage, which we produced in `ModelCreator` project.

Since dotnet/runtime has a big set of area owners and contributors, we decided to use an automatic assignment for issues and PRs. In order to achieve automatic label assignments, a GitHub app listens to all issue and PR creations via a webhook setting and gets the top three predictions from the `PredictionService` and only when the top prediction score has above 40% confidence, then this labeler app is allowed to automatically add that area label name to the newly created issue or PR.

For dotnet/aspnetcore however, this webhook is not active and instead, the aspnetcore repository uses the https://hubbup.io web app to allow for manual area label assignment. Rather than doing automatic assignments, the hubbup app provides a nice UI for the prediction results it receives from [PredictionService](https://github.com/dotnet/issue-labeler/tree/master/src/PredictionService).

## IssueLabelerService

The [IssueLabelerService](https://github.com/dotnet/issue-labeler/tree/master/src/IssueLabelerService) project is the GitHub app that gets installed into repositories that opt into automatic issue labeling.

The GitHub app receives webhoook events for issue and pull request events, queries the top three predictions in a distributed way from the various `PredictionService` deployments (with routing based on org and repo), and updates the issues and pull requests with labels and comments per each repo's configuration.

![image](https://user-images.githubusercontent.com/5897654/154319795-35975683-c4ae-477d-8a7c-74ad3079f1ed.png)

We publish multiple `PredictionService` apps using the same source code, where each app is responsible for giving predictions for one or more GitHub repositories. There is only one `IssueLabelerService` GitHub app which has the webhook set up to update issue/PRs with labels by referring to prediction results from the one or more ML-based apps configured.

The GitHub App is configured as the [dotnet-issue-labeler](https://github.com/apps/dotnet-issue-labeler/) app on GitHub, and its corresponding service is https://dispatcher-app.azurewebsites.net/. This service was developed under the `feat/public-dispatcher` branch and ultimately merged back into `main`.

## IssueLabelerService.DeploymentTests

The [IssueLabelerService.DeploymentTests](https://github.com/dotnet/issue-labeler/tree/master/test/IssueLabelerService.DeploymentTests) test project will make requests to the production deployment of the `IssueLabelerService` (`dispatcher-app`) to verify that the service is responding to simulated webhook events.
