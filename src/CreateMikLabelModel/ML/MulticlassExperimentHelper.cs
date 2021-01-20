// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using Microsoft.ML;
using Microsoft.ML.AutoML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CreateMikLabelModel.ML
{
    public static class MulticlassExperimentHelper
    {
        public static ExperimentResult<MulticlassClassificationMetrics> RunAutoMLExperiment(
            MLContext mlContext, string labelColumnName, MulticlassExperimentSettings experimentSettings,
            MulticlassExperimentProgressHandler progressHandler, IDataView dataView)
        {
            ConsoleHelper.ConsoleWriteHeader("=============== Running AutoML experiment ===============");
            Trace.WriteLine($"Running AutoML multiclass classification experiment for {experimentSettings.MaxExperimentTimeInSeconds} seconds...");
            var experimentResult = mlContext.Auto()
                .CreateMulticlassClassificationExperiment(experimentSettings)
                .Execute(dataView, labelColumnName, progressHandler: progressHandler);

            Trace.WriteLine(Environment.NewLine);
            Trace.WriteLine($"num models created: {experimentResult.RunDetails.Count()}");

            // Get top few runs ranked by accuracy
            var topRuns = experimentResult.RunDetails
                .Where(r => r.ValidationMetrics != null && !double.IsNaN(r.ValidationMetrics.MicroAccuracy))
                .OrderByDescending(r => r.ValidationMetrics.MicroAccuracy).Take(3);

            Trace.WriteLine("Top models ranked by accuracy --");
            CreateRow($"{"",-4} {"Trainer",-35} {"MicroAccuracy",14} {"MacroAccuracy",14} {"Duration",9}", Width);
            for (var i = 0; i < topRuns.Count(); i++)
            {
                var run = topRuns.ElementAt(i);
                CreateRow($"{i,-4} {run.TrainerName,-35} {run.ValidationMetrics?.MicroAccuracy ?? double.NaN,14:F4} {run.ValidationMetrics?.MacroAccuracy ?? double.NaN,14:F4} {run.RuntimeInSeconds,9:F1}", Width);
            }
            return experimentResult;
        }

        public static ExperimentResult<MulticlassClassificationMetrics> Train(
            MLContext mlContext, string labelColumnName, MulticlassExperimentSettings experimentSettings,
            MulticlassExperimentProgressHandler progressHandler, DataFilePaths paths, TextLoader textLoader)
        {
            var trainData = textLoader.Load(paths.TrainPath);
            var validateData = textLoader.Load(paths.ValidatePath);
            var experimentResult = RunAutoMLExperiment(mlContext, labelColumnName, experimentSettings, progressHandler, trainData);
            EvaluateTrainedModelAndPrintMetrics(mlContext, experimentResult.BestRun.Model, experimentResult.BestRun.TrainerName, validateData);
            SaveModel(mlContext, experimentResult.BestRun.Model, paths.ModelPath, trainData);
            return experimentResult;
        }

        public static ITransformer Retrain(ExperimentResult<MulticlassClassificationMetrics> experimentResult,
            string trainerName, MultiFileSource multiFileSource, string dataPath, string modelPath, TextLoader textLoader, MLContext mlContext)
        {
            var dataView = textLoader.Load(dataPath);

            ConsoleHelper.ConsoleWriteHeader("=============== Re-fitting best pipeline ===============");
            var combinedDataView = textLoader.Load(multiFileSource);

            var bestRun = experimentResult.BestRun;
            var refitModel = bestRun.Estimator.Fit(combinedDataView);

            EvaluateTrainedModelAndPrintMetrics(mlContext, refitModel, trainerName, dataView);
            SaveModel(mlContext, refitModel, modelPath, dataView);
            return refitModel;
        }

        public static ITransformer Retrain(MLContext mlContext, ExperimentResult<MulticlassClassificationMetrics> experimentResult,
            ColumnInferenceResults columnInference, DataFilePaths paths, bool fixedBug = false)
        {
            ConsoleHelper.ConsoleWriteHeader("=============== Re-fitting best pipeline ===============");
            var textLoader = mlContext.Data.CreateTextLoader(columnInference.TextLoaderOptions);
            var combinedDataView = textLoader.Load(new MultiFileSource(paths.TrainPath, paths.ValidatePath, paths.TestPath));
            var bestRun = experimentResult.BestRun;
            if (fixedBug)
            {
                // TODO: retry: below gave error but I thought it would work:
                //refitModel = MulticlassExperiment.Retrain(experimentResult, 
                //    "final model", 
                //    new MultiFileSource(paths.TrainPath, paths.ValidatePath, paths.FittedPath), 
                //    paths.TestPath, 
                //    paths.FinalPath, textLoader, mlContext);
                // but if failed before fixing this maybe the problem was in *EvaluateTrainedModelAndPrintMetrics*

            }
            var refitModel = bestRun.Estimator.Fit(combinedDataView);

            EvaluateTrainedModelAndPrintMetrics(mlContext, refitModel, "production model", textLoader.Load(paths.TestPath));
            // Save the re-fit model to a.ZIP file
            SaveModel(mlContext, refitModel, paths.FinalModelPath, textLoader.Load(paths.TestPath));

            Trace.WriteLine("The model is saved to {0}", paths.FinalModelPath);
            return refitModel;
        }

        private const int Width = 114;

        private static void CreateRow(string message, int width)
        {
            Trace.WriteLine("|" + message.PadRight(width - 2) + "|");
        }

        /// <summary>
        /// Evaluate the model and print metrics.
        /// </summary>
        private static void EvaluateTrainedModelAndPrintMetrics(MLContext mlContext, ITransformer model, string trainerName, IDataView dataView)
        {
            Trace.WriteLine("===== Evaluating model's accuracy with test data =====");
            var predictions = model.Transform(dataView);
            var metrics = mlContext.MulticlassClassification.Evaluate(predictions, labelColumnName: "Area", scoreColumnName: "Score");

            Trace.WriteLine($"************************************************************");
            Trace.WriteLine($"*    Metrics for {trainerName} multi-class classification model   ");
            Trace.WriteLine($"*-----------------------------------------------------------");
            Trace.WriteLine($"    MacroAccuracy = {metrics.MacroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
            Trace.WriteLine($"    MicroAccuracy = {metrics.MicroAccuracy:0.####}, a value between 0 and 1, the closer to 1, the better");
            Trace.WriteLine($"    LogLoss = {metrics.LogLoss:0.####}, the closer to 0, the better");
            Trace.WriteLine($"    LogLoss for class 1 = {metrics.PerClassLogLoss[0]:0.####}, the closer to 0, the better");
            Trace.WriteLine($"    LogLoss for class 2 = {metrics.PerClassLogLoss[1]:0.####}, the closer to 0, the better");
            Trace.WriteLine($"    LogLoss for class 3 = {metrics.PerClassLogLoss[2]:0.####}, the closer to 0, the better");
            Trace.WriteLine($"************************************************************");
        }

        private static void SaveModel(MLContext mlContext, ITransformer model, string modelPath, IDataView dataview)
        {
            // Save the re-fit model to a.ZIP file
            ConsoleHelper.ConsoleWriteHeader("=============== Saving the model ===============");
            mlContext.Model.Save(model, dataview.Schema, modelPath);
            Trace.WriteLine("The model is saved to {0}", modelPath);
            Trace.WriteLine("The model is saved to {0}", modelPath);
        }

        public static void TestPrediction(MLContext mlContext, DataFilePaths files, bool forPrs, double threshold = 0.6)
        {
            var trainedModel = mlContext.Model.Load(files.FittedModelPath, out _);
            IEnumerable<(string knownLabel, GitHubIssuePrediction predictedResult, string issueNumber)> predictions = null;
            string Legend1 = $"(includes not labeling issues with confidence lower than threshold. (here {threshold * 100.0f:#,0.00}%))";
            const string Legend2 = "(includes items that could be labeled if threshold was lower.)";
            const string Legend3 = "(those incorrectly labeled)";
            if (forPrs)
            {
                var testData = GetPullRequests(mlContext, files.TestPath);
                Trace.WriteLine($"{Environment.NewLine}Number of PRs tested: {testData.Length}");
                var prEngine = mlContext.Model.CreatePredictionEngine<GitHubPullRequest, GitHubIssuePrediction>(trainedModel);
                predictions = testData
                   .Select(x => (
                        knownLabel: x.Area, 
                        predictedResult: prEngine.Predict(x),
                        issueNumber: x.ID.ToString()
                   ));
            }
            else
            {
                var testData = GetIssues(mlContext, files.TestPath);
                Trace.WriteLine($"{Environment.NewLine}\tNumber of issues tested: {testData.Length}");
                var issueEngine = mlContext.Model.CreatePredictionEngine<GitHubIssue, GitHubIssuePrediction>(trainedModel);
                predictions = testData
                   .Select(x => (
                        knownLabel: x.Area, 
                        predictedResult: issueEngine.Predict(x),
                        issueNumber: x.ID.ToString()
                   ));
            }

            var analysis =
                predictions.Select(x =>
                (
                    knownLabel: x.knownLabel,
                    predictedArea: x.predictedResult.Area,
                    maxScore: x.predictedResult.Score.Max(),
                    confidentInPrediction: x.predictedResult.Score.Max() >= threshold,
                    issueNumber: x.issueNumber
                ));

            var countSuccess = analysis.Where(x =>
                    (x.confidentInPrediction && x.predictedArea.Equals(x.knownLabel, StringComparison.Ordinal)) ||
                    (!x.confidentInPrediction && !x.predictedArea.Equals(x.knownLabel, StringComparison.Ordinal))).Count();

            var missedOpportunity = analysis
                .Where(x => !x.confidentInPrediction && x.knownLabel.Equals(x.predictedArea, StringComparison.Ordinal)).Count();

            var mistakes = analysis
                .Where(x => x.confidentInPrediction && !x.knownLabel.Equals(x.predictedArea, StringComparison.Ordinal))
                .Select(x => new { Pair = $"\tPredicted: {x.predictedArea}, Actual:{x.knownLabel}", IssueNumbers = x.issueNumber, MaxConfidencePercentage = x.maxScore * 100.0f })
                .GroupBy(x => x.Pair)
                .Select(x => new
                {
                    Count = x.Count(),
                    PerdictedVsActual = x.Key,
                    Items = x,
                })
                .OrderByDescending(x => x.Count);
            int remaining = predictions.Count() - countSuccess - missedOpportunity;

            Trace.WriteLine($"{Environment.NewLine}\thandled correctly: {countSuccess}{Environment.NewLine}\t{Legend1}{Environment.NewLine}");
            Trace.WriteLine($"{Environment.NewLine}\tmissed: {missedOpportunity}{Environment.NewLine}\t{Legend2}{Environment.NewLine}");
            Trace.WriteLine($"{Environment.NewLine}\tremaining: {remaining}{Environment.NewLine}\t{Legend3}{Environment.NewLine}");
            foreach (var mismatch in mistakes.AsEnumerable())
            {
                Trace.WriteLine($"{mismatch.PerdictedVsActual}, NumFound: {mismatch.Count}");
                var sampleIssues = string.Join(Environment.NewLine, mismatch.Items.Select(x => $"\t\tFor #{x.IssueNumbers} was {x.MaxConfidencePercentage:#,0.00}% confident"));
                Trace.WriteLine($"{Environment.NewLine}{ sampleIssues }{Environment.NewLine}");
            }
        }

        public static GitHubIssue[] GetIssues(MLContext mlContext, string dataFilePath)
        {
            var dataView = mlContext.Data.LoadFromTextFile<GitHubIssue>(
                                            path: dataFilePath,
                                            hasHeader: true,
                                            separatorChar: '\t',
                                            allowQuoting: true,
                                            allowSparse: false);

            return mlContext.Data.CreateEnumerable<GitHubIssue>(dataView, false).ToArray();
        }

        public static GitHubPullRequest[] GetPullRequests(MLContext mlContext, string dataFilePath)
        {
            var dataView = mlContext.Data.LoadFromTextFile<GitHubPullRequest>(
                                            path: dataFilePath,
                                            hasHeader: true,
                                            separatorChar: '\t',
                                            allowQuoting: true,
                                            allowSparse: false);

            return mlContext.Data.CreateEnumerable<GitHubPullRequest>(dataView, false).ToArray();
        }
    }
}
