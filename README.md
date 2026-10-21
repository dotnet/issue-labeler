# issue-labeler

Use machine learning models to automatically label issues and pull requests, all through GitHub workflows.

[Onboarding](https://github.com/dotnet/issue-labeler/wiki/Onboarding) is entirely self-service, and models can be retrained at any time.

## Training evaluation

Training reports accuracy and log loss on its random 20% held-out split, plus a table for precision targets of 60%, 70%, 80%, 90%, and 95%. Each row shows the score threshold with the highest observed coverage that meets the target, the actual precision, coverage, and number of accepted predictions. Predictions tied at the cutoff are included together. Targets that cannot be reached are marked explicitly.

These thresholds are selected and evaluated on the same split, so the table is exploratory, not an independent estimate of future precision. Production thresholds are not changed automatically. The separate Test job is a historical recheck that may include training data.
