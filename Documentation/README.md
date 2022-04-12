

## Intro to issue labeling

This repository contains the source code to train ML models for making label predictions, as well as the code for automatically applying issue labels onto issue/pull requests on GitHub repositories.

This issue-labeler uses [ML.NET](https://github.com/dotnet/machinelearning) to help predict labels on github issues and pull requests. 

## Table of Contents

- Download/Train ML models
- Pack ML models into nuget
- Application setup: a step-by-step example
  - Get top-N label predictions
  - Enable automatic label assignments

## Download/Train ML models

We use GraphQL and [Octokit](https://www.nuget.org/packages/Octokit/) to download issues from GitHub and then train models using [ML.NET](ML.NET). e.g. dotnet/runtime repository has been trained on over 30,000 issues, and 5,000 PRs which have been labeled in the past, either manually or automatically.

#### About CreateMikLabel project

The [CreateMikLabelModel](https://github.com/dotnet/issue-labeler/tree/master/src/CreateMikLabelModel) project is responsible for:

1. Downloading Github issues and pull requests
2. Specifying which data to download (title, description, labels, author, mentions, PR file names, optionally PR diff etc.)
3. Segmenting issue or PR records into train (first 80%), validate (second 10%), and test (last 10%) data.
4. Customizing ML training settings: ML models to skip/consider (e.g. FastTreeOva), time to train, information to consider while training (e.g. number of file changes).
5. Optionally testing the ML generated Models to help understand which area labels may be getting more missed predictions or lower confidence compared to others.

#### ML customization:

As seen in [commit](https://github.com/dotnet/issue-labeler/commit/77e4dbc45184f34e940c0f3cba57160e30c2c183), the [ExperimentModifier](https://github.com/maryamariyan/issue-labeler-2/blob/213a96cf88d31333295126e7815c4688c2e31b54/src/CreateMikLabelModel/ML/ExperimentModifier.cs) class in CreateMikLabelModel project helps configure how the models should be trained (what column information to use (e.g. issue Description), how to treat them (as Text, Categorical data, Numeric or Ignore), how long to let the experiment run, and which algorithms to let AutoML consider while training (FastTreeOva, LightGbm, etc.)).

## Pack ML models into nuget

In the past we used to pack the resulting models into a nuget package called `Microsoft.DotNet.GitHubIssueLabeler.Assets` to be easily consumable within a web application. But now we have retired the nuget package and instead use Azure Blob Storage to upload the models for use in the application.

#### About Microsoft.DotNet.GitHub.IssueLabeler project

The [Microsoft.DotNet.GitHub.IssueLabeler](https://github.com/dotnet/issue-labeler/tree/master/src/Microsoft.DotNet.GitHub.IssueLabeler) project is the web application that uses ML models created using CreateMikLabelModel to predict area labels. Given repository owner/name/number combination, the IssueLabeler app provides an API returning top three predictions along with their confidence score. This information is computed using the ML models loaded in memory uploaded from Azure Blob Storage, which we produced in CreateMikLabelerModel project.
Since dotnet/runtime has a big set of area owners and contributors, we decided to use an automatic assignemnt for issues and PRs. In order to achieve automatic label assignments, the IssueLabeler app, listens to all issue and PR creations via a webhook setting and finds top three predictions and only when the top prediction score has above 40% confidence, then this labeler app is allowed to automatically add that area label name to the newly created issue or PR. For dotnet/aspnetcore however, this webhook is not active and instead, the aspnetcore repository uses the hubbup web app to allow for manual area label assignment. Rather than doing automatic assignments, the hubbup app provides a nice UI for the prediction results it receives from [Microsoft.DotNet.GitHub.IssueLabeler](https://github.com/dotnet/issue-labeler/tree/master/src/Microsoft.DotNet.GitHub.IssueLabeler).

#### About the public-dispatcher branch

The public-dispatcher branch is the code base for the github app that gets installed per github organization. We would have one app instance per org (one for Microsoft and one for dotnet) based off the public-dispatcher branch. The github app would be able to query top three predictions in a distributed way from other app(s) based off of the main branch which are configured for one or more repository to provide prediction scores.

![image](https://user-images.githubusercontent.com/5897654/154319795-35975683-c4ae-477d-8a7c-74ad3079f1ed.png)

Based on the above diagram we can publish multiple apps using the same source code from main branch, where each app is responsible for giving predictions for one or more github repositories. But there would be only a single github app per github organization (e.g. dotnet) which has the webhook setup to update issue/PRs with labels by referring to prediction results from the one or more ML-based apps configured.

## Application setup: a step-by-step example

### Prerequisites

* Create a [GitHub Personal Access Token](https://github.com/settings/tokens) that includes the `repo:public_repo` scope (to access public repositories)
* Register that token on your machine with the command: `dotnet user-secrets set GitHubAccessToken "{{ghp_token_value}}"`

### Execution

* Run the CreateMikLabelModel application using `dotnet run` from the `src\CreateMikLabelModel` folder
* Specify the repo to be modeled as a command line argument: `dotnet run -- [repo]`

You can configure the web application to either give you the top 3 recommended labels or automatically assign the top choice.
