# issue-labeler

Use machine learning models to automatically label issues and pull requests, all through GitHub workflows.

[Onboarding](https://github.com/dotnet/issue-labeler/wiki/Onboarding) is entirely self-service, and models can be retrained at any time.

## Training recipe

New training runs use Recipe C: T5 text features with SDCA maximum entropy and explicit `L2Regularization = 3e-6f`. Word n-grams use term frequency (TF), and character n-grams use TF-IDF. The remaining ML.NET 5.0.0 text defaults are unchanged, including word unigrams/bigrams, exact character trigrams, vocabulary caps and preprocessing. ML.NET L2-normalizes the word and character blocks separately before concatenating them. There is no additional combined-vector normalization.

Issues and discussions use joint title and body features. Pull requests also retain file and folder names. `Label` is supervision only, never an input feature. Maximum iterations and the L1/L2 ratio remain automatic, and convergence tolerance stays at the ML.NET default. Changing L2 can therefore also change absolute L1 regularization. This is not an isolated L2-only change.

The completed, unpublished cross-repository benchmark in `matouskozak/dotnet-issue-labeler-benchmark` (local publication commit `486cb72c7c2291ddd728d8c895dcd8f3bdc4034e`, report `experiments/cross-repository/2026-10-19/final-test-report-2026-10-21-r3/README.md`) found higher held-out top-1 accuracy and supported macro-F1 than T5 with automatic regularization in all four tested repositories (MAUI, ASP.NET Core, Roslyn and SDK). Equal-repository mean gains were 1.316 percentage points of row accuracy and 3.265 macro-F1 points (0-100). This is descriptive evidence from four related .NET datasets, recorded export labels, one seed and one execution cohort, not a significance or universal-optimality claim.

The benchmark used seed 42, one thread and frozen grouped splits. Production retains its existing unspecified seed, automatic trainer threading and random 80/20 split, so results are not expected to reproduce the benchmark exactly. Inference thresholds were not retuned. Existing saved models remain loadable and unchanged. The recipe takes effect only when training a new model, which must still be tested and promoted through the existing workflow.
