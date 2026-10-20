# issue-labeler

Use machine learning models to automatically label issues and pull requests, all through GitHub workflows.

[Onboarding](https://github.com/dotnet/issue-labeler/wiki/Onboarding) is entirely self-service, and models can be retrained at any time.

## Held-out model evaluation

Training saves the exact 20% held-out split beside each model as
`<model-name>.test-data.bin`. Both the training metrics and the Tester evaluate
this frozen dataset. The Tester does not download current GitHub items and does
not require a GitHub token.

The training action caches the held-out data separately under
`issue-labeler/test-data/<type>/<model-content-hash>`. The test action restores
the model first, then restores only its matching test set. Promoting or backing
up a model preserves its content hash, so the same test set remains associated
with it without changing the model ZIP or prediction downloads.

Keep the test-data cache while retaining its matching model. The cache-retention
workflow refreshes both the selected model and its test set. Neither cache has
guaranteed durability: if the test set is evicted, deleted, or was never saved
for an older model, evaluation fails with a retraining instruction instead of
using different data. Predictions still require only the model ZIP.

For local evaluation, keep the two files beside each other and run:

```powershell
dotnet run --project .\IssueLabeler\src\Tester -- --issues-model .\labeler-cache\issues-model.zip --threshold 0.4
```

Use `--pulls-model` for pull requests, or `--issues-model` with `--discussions`
for a discussion model. Download-related test-action inputs (`repository`,
`label_prefix`, `excluded_authors`, `limit`, `page_size`, `page_limit`, and
`retries`) are deprecated and ignored. `excluded_labels` reports excluded labels
found in the results without filtering or changing the saved test set.

Publish and pin the updated `train` and `test` actions together using the normal
release process. Existing models must be retrained to create their saved test
sets; older action releases continue to use their previous testing behavior.
