# issue-labeler

Use machine learning models to automatically label issues and pull requests, all through GitHub workflows.

[Onboarding](https://github.com/dotnet/issue-labeler/wiki/Onboarding) is entirely self-service, and models can be retrained at any time.

## Train once, evaluate thresholds later

Use the existing **Labeler: Training** workflow:

1. Select **All** (download, train, historical recheck and threshold report) or
   **Train Model** (reuse downloaded data, train and report). Choose the model
   type, a fresh `cache_key_suffix`, and candidate `thresholds`. Use a staged
   suffix rather than `ACTIVE` while reviewing a new model.
2. Review the threshold table: correct/wrong accepted predictions, abstentions,
   accepted precision, coverage, and per-area evidence. The configured reference
   threshold is included, but no production variable is changed.
3. To try other thresholds, select **Evaluate Thresholds** with the same model
   type and cache suffix on the same branch. This reads the saved scores without
   downloading issues, loading weights, or retraining. **Test Model** remains the
   historical recheck and may include training examples.

Each evaluation uploads one bundle containing the model ZIP,
`<model-name>.evaluation.json`, `summary.md`, `scorecard.json`, and `per-area.csv`.
The report is produced before the historical recheck. The saved evidence
contains model/dataset hashes, tool version, training support, and exact held-out
labels/scores, not issue titles or bodies. Scoring uses the saved/reloaded model
with the input `Label` empty. Missing or mismatched evidence fails explicitly;
older models must be retrained to produce it.

Reports cover **top-1** routing (`max_labels: 1`), accepting a score equal to or
above the threshold and keeping score-slot order for ties. The label prefix must
include every model and reference label. Per-area precision measures assignments
to that area; coverage measures non-abstention on reference examples from it.
Raw micro/macro accuracy does not change with the threshold, and scores are not
guaranteed correctness probabilities. No threshold is automatically recommended.

The existing random 80/20 split is retained. Once used for threshold selection,
the holdout is a **tuning set**, not an untouched final test. Small per-area
support and duplicate/related issues limit the evidence; pointwise confidence
intervals do not guarantee the quality of a threshold selected after searching.

### Actions and offline use

The `train` action preserves the model ZIP cache format and saves scores under
`issue-labeler/evaluation/<type>/<suffix>`. The `test` action defaults to
`mode: historical`; use `mode: heldout` and `thresholds` for saved-score evaluation.
Its existing `threshold` input supplies the reference value. Download/filter
options are not supported in held-out mode.

Model and score caches are separate, not atomic. Use fresh suffixes; delete both
entries before retrying a partial save. Caches can be evicted, and run artifacts
follow repository retention settings. Download matching model/scores before
expiry for longer-lived evidence.

After building the tool, evaluate a downloaded model/scores pair locally without
a GitHub token, source dataset, or network:

```powershell
dotnet run --no-build --project .\IssueLabeler\src\Tester -- --evaluation .\issues-model.evaluation.json --issues-model .\issues-model.zip --label-prefix area- --threshold 0.15 --thresholds "0.05,0.15,0.40,0.60,0.80" --report .\evaluation-report
```

Use `--pulls-model` for pull request models. This repository's training workflow
checks out its selected revision to run local train/test actions. Other
repositories should reference a published action SHA containing this feature,
not copy the local-action paths. v2.2.0 does not contain it. Production prediction
and promotion are unchanged.
