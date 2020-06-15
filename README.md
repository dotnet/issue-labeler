# Issue Labeler Project

issue-labeler uses [ML.NET](https://github.com/dotnet/machinelearning) to help predict labels on github issues and pull requests. We consider labels  with names starting with "area-" as the label of interest, even though that could be easily configured to be anything else. This repository shows how we could use existing issue and pull requests on a github repository to train ML models that can in turn be used for predicting area labels of incoming issues on any given trained repository automatically upon creation.

## How it works
The pretrained [ML.NET](https://github.com/dotnet/machinelearning) model is consumed through a nuget package Microsoft.DotNet.GitHubIssueLabeler.Assets. This model has been trained on over 15,000 issues, and 10,000 PRs already labeled in the runtime repo. To see a simple end-to-end machine learning sample for how to create a model, you can check [here](https://github.com/dotnet/machinelearning-samples/tree/master/samples/csharp/end-to-end-apps/MulticlassClassification-GitHubLabeler).

Whenever an issue is opened in the repo, the web api receives the payload (using webhooks) containing all the information about the issue like title, body, milestone, issue number etc. It then supplies this information to already loaded pretrained model and the model predicts a probability distribution over the all possible labels. We then take the label with maximum probability and compare it with a threshold. if the predicted probability is greater than threshold then we apply the label otherwise we do nothing. We use a separate model for predicting label for pull requests, since PRs contain extra information through their file diffs. 

You can learn more about the project from the project [Documentation](Documentation).

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
