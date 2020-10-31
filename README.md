# dotnet/issue-labeler

This repo contains the code to build the dotnet issue labeler.

## Which github repositories use this issue labeler today?

The dotnet organization contains repositories with many incoming issues and pull requests. In order to help with the triage process, we categorize issues into subcategories called areas. We mark issues related to each area, with a specific `area-` label, and therefore over time we are able to employ an issue labeler which learns from these assignments. 

The following repositories triage their incoming issues by manually setting labels based on top 3 predictions returned from dotnet/issue-labeler:

* [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore)
* [dotnet/extensions](https://github.com/dotnet/extensions)

The following repositories use dotnet/issue-labeler to automatically set `area-` labels for incoming issues and pull requests:

* [dotnet/runtime](https://github.com/dotnet/runtime)
* [dotnet/roslyn](https://github.com/dotnet/roslyn)
* [dotnet/dotnet-api-docs](https://github.com/dotnet/dotnet-api-docs)
* [dotnet/corefx](https://github.com/dotnet/corefx) (archived)

Of course with automatic labeling there is always a margin of error. But the good thing is the issue-labeler learns from mistakes so long as wrong label assignments have been updated with a correct label manually.

## How to use this issue labeler today?

To get the most out of this issue labeler, prior setup, the repository needs to get to a point where it has been pre-populated with a portion of issues with `area-` labels on them. 

It is possible to still get usage out of this issue labeler, even if you decided to continue doing manual label assignments, e.g. to get top-N predictions recommendations only.

But once the issue labeling is automated, it is recommended to make sure:

- Contributors have a habit of manually applying `area-` labels even when the labeler was not confident enough to select one.
- Contributors have a habit of manually correcting prediction mistakes done.

These two habits help the issue labeler learn better over time.

Also note, the labeler does not learn in real-time, but instead ML trainings need to be redone every once in a while (e.g. every two months depending on issue traffic).

## How to get started?

The [docs](Documentation/) page explains in more detail steps involved in setting up an issue labeler for a github repository.

## Useful Links

* [ML.NET](ML.NET) 
* [.NET home repo](https://github.com/Microsoft/dotnet) - links to 100s of .NET projects, from Microsoft and the community.
* [ASP.NET Core home](https://docs.microsoft.com/aspnet/core/?view=aspnetcore-3.1) - the best place to start learning about ASP.NET Core.

## License

.NET is licensed under the [MIT](LICENSE.TXT) license.
