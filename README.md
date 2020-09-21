# Issue Labeler Project

issue-labeler uses [ML.NET](https://github.com/dotnet/machinelearning) to help predict labels on github issues and pull requests. We consider labels with names starting with "area-" as the label of interest, even though that could be easily configured to be anything else. This repository shows how we could use existing issue and pull requests on a github repository to train ML models that can in turn be used for predicting area labels of incoming issues on any given trained repository automatically upon creation.

You can learn more about the project from the project [Documentation](Documentation).

## More about the projects in dotnet/issue-labeler

### Which GitHub repositories already use issue-labeler?
[dotnet/runtime](https://github.com/dotnet/runtime) and [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) already use [ML.NET](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet) via [issue-labeler](https://github.com/dotnet/issue-labeler) to get prediction on the area label for any incoming issue or pull request they receive. Issue labeling is trained on all existing issue and PRs which have a label starting with "area-". 

### How it works
[ML.NET](https://github.com/dotnet/machinelearning) trained models are consumed through a nuget package Microsoft.DotNet.GitHubIssueLabeler.Assets. For dotnet/runtime repository this model has been trained on over 30,000 issues, and 5,000 PRs already labeled in the runtime repo.

Whenever an issue is opened in the repo, the web api receives the payload (using webhooks) containing all the information about the issue like title, body, milestone, issue number etc. It then supplies this information to already loaded pretrained model and the model predicts a probability distribution over the all possible labels. We then take the label with maximum probability and compare it with a threshold. if the predicted probability is greater than threshold then we apply the label otherwise we do nothing. We use a separate model for predicting label for pull requests, since PRs contain extra information through their file diffs. 

### About CreateMikLabel project
The [CreateMikLabelModel](https://github.com/dotnet/issue-labeler/tree/master/src/CreateMikLabelModel) project is responsible for:

1. Downloading Github issues and pull requests
2. Specifying which data to download (title, description, labels, author, mentions, PR file names, optionally PR diff etc.)
3. Segmenting issue or PR records into train (first 80%), validate (second 10%), and test (last 10%) data.
4. Customizing ML training settings: ML models to skip/consider (e.g. FastTreeOva), time to train, information to consider while training (e.g. number of file changes).
5. Optionally testing the ML generated Models to help understand which area labels may be getting more missed predictions or lower confidence compared to others.

### About Microsoft.DotNet.GitHubIssueLabeler.Assets nuget package
Once we have ML models generated using [CreateMikLabelModel](https://github.com/dotnet/issue-labeler/tree/master/src/CreateMikLabelModel), they get packed in a nuget package called Microsoft.DotNet.GitHub.IssueLabeler in the [dotnet-eng](https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-eng/nuget/v3/index.json) nuget source. This nuget package contains PR and issue labeler models for all github repositories trained via CreateMikLabelerModel project. The tree structure where ML models get placed in the nuget package is as follows:
```
 > model
    > dotnet
       > aspnetcore
          - GitHubLabelerModel.zip
          - GitHubPrLabelerModel.zip
       > runtime
          - GitHubLabelerModel.zip
          - GitHubPrLabelerModel.zip
       > extensions
          - GitHubLabelerModel.zip
          - GitHubPrLabelerModel.zip
    > microsoft
       > service-fabric
          - GitHubLabelerModel.zip
```
### About Microsoft.DotNet.GitHub.IssueLabeler project
The [Microsoft.DotNet.GitHub.IssueLabeler](https://github.com/dotnet/issue-labeler/tree/master/src/Microsoft.DotNet.GitHub.IssueLabeler) project is the web application that uses ML models created using CreateMikLabelModel via a nuget package called `Microsoft.DotNet.GitHubIssueLabeler.Assets`.
Given repository owner/name/number combination, the IssueLabeler app provides an API returning top three predictions along with their confidence score. This information is computed using the ML models in the Microsoft.DotNet.GitHub.IssueLabeler nuget package we produced in CreateMikLabelerModel project.
Since dotnet/runtime has a big set of area owners and contributors, we decided to use an automatic assignemnt for issues and PRs. In order to achieve automatic label assignments, the IssueLabeler app, listens to all issue and PR creations via a webhook setting and finds top three predictions and only when the top prediction score has above 40% confidence, then this labeler app is allowed to automatically add that area label name to the newly created issue or PR. For dotnet/aspnetcore however, this webhook is not active and instead, the aspnetcore repository uses the hubbup web app to allow for manual area label assignment. Rather than doing automatic assignments, the hubbup app provides a nice UI for the prediction results it receives from [Microsoft.DotNet.GitHub.IssueLabeler](https://github.com/dotnet/issue-labeler/tree/master/src/Microsoft.DotNet.GitHub.IssueLabeler).

The nice thing with [Microsoft.DotNet.GitHub.IssueLabeler](https://github.com/dotnet/issue-labeler/tree/master/src/Microsoft.DotNet.GitHub.IssueLabeler) is that we can publish multiple apps using the same source code, where each app is responsible for giving predictions for a single github repository. This would be possible if the Microsoft.DotNet.GitHubIssueLabeler.Assets nuget package used by the IssueLabeler app contains ML models for that repository and RepoName/RepoOwner Configuration values are properly setup for that app in azure app service portal.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for information on contributing to this project.

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) 
to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](http://www.dotnetfoundation.org/code-of-conduct).

## License

This project is licensed with the [MIT license](LICENSE).

## .NET Foundation

issue-labeler is a [.NET Foundation project](https://dotnetfoundation.org/projects).

## Related Projects

You should take a look at these related projects:

- [dotnet/machinelearning](https://github.com/dotnet/machinelearning)
- [dotnet/runtime](https://github.com/dotnet/runtime)
- [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)
