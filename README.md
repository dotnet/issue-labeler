# issue-labeler
Use a machine learning model to automatically label issues and pull requests.

## Downloader

Download issue and pull request data from GitHub, creating tab-separated (.tsv) data files to be consumed by the Trainer.

## Trainer

Load the tab-separated issue and pull request data that has already been downloaded, and train an ML.NET model over the data to prepare for making label predictions.

## Tester

Perform a comparison test run over GitHub data, predicting labels and comparing the predictions against the actual values. This can be performed either by downloading issue and pull request data from GitHub or loading a tab-separated (.tsv) file created by the Downloader.

## Predictor

Consume the ML.NET model and make predictions for issues and pull requests.

## Reusable GitHub Workflows

The `.github/workflows` folder exposes reusable workflows that can be used from other repositories to integrate automated labeling for issues and pull requests.

### `download-issues.yml` / `download-pulls.yml`

Invokes the Downloader, saving the `.tsv` file to the Actions cache withing the calling repository. Supports storing multiple data files in cache side-by-side using cache key suffixes, which enables building and testing new models without disrupting predictions.

### `train-issues.yml` / `train-pulls.yml`

Invokes the Trainer, consuming a `.tsv` data file from the Actions cache to build a model. The model is persisted into the Actions cache. Supports storing multiple models in cache side-by-side using cache key suffixes, which enables testing and staging new models without disrupting predictions.

### `test-issues.yml` / `test-pulls.yml`

Invokes the Tester, consuming an ML.NET model from the Actions cache to test against actual issue/pull labels.

### `promote-issues.yml` / `promote-pulls.yml`

Promotes a model persisted to cache with a cache key suffix to use the default cache key, thus promoting it into the production cache slot for predictions.

### `predict-issue.yml` / `predict-pull.yml`

Invokes the Predictor, predicting and applying labels to issues and pull requests. This can be called with either a single issue/pull number, or a comma-separated list of number ranges (e.g. `1-1000,2000-3000`). Supports specifying a cache key suffix for making predictions from a model in a test/staging slot.

## Example Usage

The reusable workflows referenced above can be composed together into very simple workflows within a repository. In fact, this repository itself uses the reusable workflows in the prescribed manner. To adopt the modeler in your own repository, you can follow the example set in the 4 `labeler-*.yml` files in the `.github/workflows` folder.

### `labeler-stage.yml`

This single workflow can be manually triggered from the Actions page, and each of the following steps can be enabled or disabled.

1. Download issues from GitHub
2. Download pull requests from GitHub
3. Train an issues model
4. Train a pulls model
5. Test the issues model
6. Test the pulls model

If all of these steps are enabled for the run, the single workflow will do all the work necessary to prepare a repository for predicting labels on issues and pull requests.

By default, the workflow will save the new data and models into `staging` slots within the cache. For initial onboarding of a repository, the `cache_key_suffix` field can be left blank.

### `labeler-promote.yml`

This workflow can promote issue and/or pull request models into the primary cache slot to be used by predictions. The approach of training new models into a `staging` slot is that the new model can be tested without disrupting ongoing labeling in the repository. Once a new model is confirmed to meet expectations, it can be promoted.

### `labeler-predict-issues.yml`

Predict labels for issues as they are opened in the repository. This workflow can also be triggered manually to label ranges of issue numbers.

### `labeler-predict-pulls.yml`

Predict labels for pull requests as they are opened in the repository. This workflow can also be triggered manually to label ranges of pull request numbers.
