
# Implementation details

The following sections help better understand how some of the projects in this repo work.

## The public-dispatcher branch

The public-dispatcher branch is the code base for the GitHub app that gets installed per GitHub organization. We would have one app instance per org (one for Microsoft and one for dotnet) based off the public-dispatcher branch. The GitHub app would be able to query top three predictions in a distributed way from other app(s) based off of the main branch which are configured for one or more repository to provide prediction scores.

![image](https://user-images.githubusercontent.com/5897654/154319795-35975683-c4ae-477d-8a7c-74ad3079f1ed.png)

Based on the above diagram we can publish multiple apps using the same source code from main branch, where each app is responsible for giving predictions for one or more GitHub repositories. But there would be only a single github app per github organization (e.g. dotnet) which has the webhook setup to update issue/PRs with labels by referring to prediction results from the one or more ML-based apps configured.
