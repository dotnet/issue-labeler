# issue-labeler

Use machine learning models to automatically label issues and pull requests, all through GitHub workflows.

[Onboarding](https://github.com/dotnet/issue-labeler/wiki/Onboarding) is entirely self-service, and models can be retrained at any time.

## Saved held-out evaluation

Training keeps the existing random 80/20 split and saves
`<model-name>.evaluation.json` beside the model ZIP. The evidence contains
model/dataset SHA-256 hashes, tool version, training support, and the held-out
reference labels and full score vectors, not issue titles or bodies. Scores
come from the saved/reloaded model with the input `Label` empty.

After building, evaluate a matching model/scores pair without a GitHub token,
source dataset, downloads, loading model weights, or retraining:

```powershell
dotnet run --no-build --project .\IssueLabeler\src\Tester -- --evaluation .\issues-model.evaluation.json --issues-model .\issues-model.zip --label-prefix area- --threshold 0.15
```

Use `--pulls-model` for pull request models. Exactly one model is required.
The threshold uses an invariant decimal in [0,1] (default 0.4). Evaluation reports
total, correct, wrong, and abstained counts, precision (correct / accepted), and
coverage (accepted / total). Top-1 scores equal to the threshold are accepted;
ties keep score-slot order. Precision is N/A when all examples abstain.
The prefix must include every model and reference label. Missing, malformed,
wrong-type, or mismatched evidence fails explicitly; older models need retraining.

The `train` action preserves the existing model ZIP cache format and saves
evidence separately under `issue-labeler/evaluation/<type>/<suffix>`.
The `test` action defaults to `mode: historical`, which downloads currently
labeled items and may include training examples. Use `mode: heldout` with the
same type/cache suffix, label prefix, and a single `threshold` to replay saved
scores. Historical repository/download/filter options cannot be combined with
held-out mode.

Model and score caches are separate, not atomic. Use fresh suffixes; delete both
entries before retrying a partial save. Caches can be evicted, so retain matching
model/scores pairs before expiry for longer-lived evidence.

This feature requires a release containing these changes and repinning consumers;
v2.2.0 does not contain it. The existing training workflow remains unchanged and
pinned to its release SHA. Production prediction, promotion, and live thresholds
are unchanged. Scores are not guaranteed correctness probabilities. Once used
to select a threshold, this holdout is a tuning set, not an untouched final test;
small support and duplicate/related issues limit the evidence.
