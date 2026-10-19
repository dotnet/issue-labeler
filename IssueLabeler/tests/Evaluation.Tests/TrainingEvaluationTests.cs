// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

extern alias Tester;

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Actions.Core.Extensions;
using Actions.Core.Services;
using IssueLabeler.Evaluation;
using IssueLabeler.Training;
using Microsoft.ML;
using Microsoft.ML.Transforms.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Evaluation.Tests;

[TestClass]
public sealed class TrainingEvaluationTests
{
    private string directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), $"issue-labeler-training-evaluation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [DataRow(ModelType.Issue, false)]
    [DataRow(ModelType.PullRequest, false)]
    [DataRow(ModelType.Issue, true)]
    [TestMethod]
    public void SavedScoresMatchReloadedModelAndIgnoreGoldLabels(ModelType type, bool includeTargetFeature)
    {
        var context = new MLContext(seed: 42);
        Sample[] samples = [.. Enumerable.Range(0, 40).Select(index => new Sample
        {
            Label = index % 2 == 0 ? "area-A" : "area-B",
            Title = index % 2 == 0 ? "alpha prediction" : "beta example",
            Body = index % 2 == 0 ? "alpha model trainer" : "beta data frame",
            FileNames = index % 2 == 0 ? "alpha.cs" : "beta.cs",
            FolderNames = index % 2 == 0 ? "alpha" : "beta"
        })];
        var trainingData = context.Data.LoadFromEnumerable(samples);
        string[] features = type == ModelType.Issue
            ? [nameof(Sample.Title), nameof(Sample.Body)]
            : [nameof(Sample.Title), nameof(Sample.Body), nameof(Sample.FileNames), nameof(Sample.FolderNames)];
        if (includeTargetFeature)
        {
            features = [.. features, nameof(Sample.Label)];
        }
        var pipeline = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(Sample.Label))
            .Append(context.Transforms.Text.FeaturizeText("Features", new TextFeaturizingEstimator.Options(), features))
            .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"))
            .Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        var model = pipeline.Fit(trainingData);
        string modelPath = Path.Combine(directory, "model.zip");
        context.Model.Save(model, trainingData.Schema, modelPath);
        string datasetPath = Path.Combine(directory, "data.tsv");
        File.WriteAllLines(datasetPath, samples.Select(sample => DataFileUtils.FormatIssueRecord(sample.Label, sample.Title, sample.Body)));
        Sample[] heldOut =
        [
            new() { Label = "area-A", Title = "alpha", Body = "alpha prediction", FileNames = "alpha.cs", FolderNames = "alpha" },
            new() { Label = "area-B", Title = "beta", Body = "beta example", FileNames = "beta.cs", FolderNames = "beta" }
        ];
        var artifact = HeldOutEvaluation.Save(context, trainingData, context.Data.LoadFromEnumerable(heldOut),
            type, datasetPath, modelPath, EvaluationArtifact.HashFile(datasetPath));
        Assert.AreEqual(40, artifact.TrainingCount);
        Assert.AreEqual(2, artifact.Rows.Length);
        Assert.AreEqual(type.ToString(), artifact.ModelType);
        var loaded = EvaluationArtifact.Load(EvaluationArtifact.GetPath(modelPath), modelPath);
        CollectionAssert.AreEqual(artifact.Rows[0].Scores, loaded.Rows[0].Scores);

        (heldOut[0].Label, heldOut[1].Label) = (heldOut[1].Label, heldOut[0].Label);
        var shuffled = HeldOutEvaluation.Save(context, trainingData, context.Data.LoadFromEnumerable(heldOut),
            type, datasetPath, modelPath, EvaluationArtifact.HashFile(datasetPath));
        for (int index = 0; index < artifact.Rows.Length; index++)
        {
            CollectionAssert.AreEqual(artifact.Rows[index].Scores, shuffled.Rows[index].Scores);
            Assert.AreNotEqual(artifact.Rows[index].Label, shuffled.Rows[index].Label);
        }
    }

    [TestMethod]
    public async Task TrainerAndThresholdEvaluationCompleteWithoutGitHubAccess()
    {
        string datasetPath = Path.Combine(directory, "data.tsv");
        string modelPath = Path.Combine(directory, "model.zip");
        string[] records = [DataFileUtils.FormatIssueRecord("Label", "Title", "Body"),
            .. Enumerable.Range(0, 100).Select(index => index % 2 == 0
                ? DataFileUtils.FormatIssueRecord("area-A", $"Alpha trainer {index}", "Alpha training pipeline")
                : DataFileUtils.FormatIssueRecord("area-B", $"Beta data frame {index}", "Beta data processing"))];
        File.WriteAllLines(datasetPath, records);
        var training = await RunTool("Trainer", "--issues-data", datasetPath, "--issues-model", modelPath);
        Assert.AreEqual(0, training.ExitCode, training.Output);
        var artifact = EvaluationArtifact.Load(EvaluationArtifact.GetPath(modelPath), modelPath);
        Assert.AreEqual(100, artifact.TrainingCount + artifact.Rows.Length);
        Assert.AreEqual(EvaluationArtifact.HashFile(datasetPath), artifact.DatasetSha256);
        File.Delete(datasetPath);
        var evaluation = await RunTester("--evaluation", EvaluationArtifact.GetPath(modelPath),
            "--issues-model", modelPath, "--label-prefix", "area-", "--threshold", "0.4");
        Assert.AreEqual(0, evaluation.ExitCode, evaluation.Output);
        StringAssert.Contains(evaluation.Output, $"Total: **{artifact.Rows.Length}**");
    }

    [TestMethod]
    public async Task TrainerSupportsCaseVariantLabelsAndSeparatesTheSummaryNote()
    {
        string datasetPath = Path.Combine(directory, "data.tsv");
        string modelPath = Path.Combine(directory, "model.zip");
        string[] labels = ["area-Foo", "area-foo", "area-Bar"];
        string[] records = [DataFileUtils.FormatIssueRecord("Label", "Title", "Body"),
            .. Enumerable.Range(0, 300).Select(index => DataFileUtils.FormatIssueRecord(
                labels[index % labels.Length], $"Training example {index}", $"Content for {labels[index % labels.Length]}"))];
        File.WriteAllLines(datasetPath, records);
        var training = await RunTool("Trainer", "--issues-data", datasetPath, "--issues-model", modelPath);
        Assert.AreEqual(0, training.ExitCode, training.Output);
        var artifact = EvaluationArtifact.Load(EvaluationArtifact.GetPath(modelPath), modelPath);
        Assert.HasCount(3, artifact.Labels);
        Assert.HasCount(3, artifact.TrainingSupport);
        Assert.AreEqual(300, artifact.TrainingCount + artifact.Rows.Length);
        var result = ThresholdEvaluation.Evaluate(artifact, [0.4f],
            label => label.StartsWith("area-", StringComparison.OrdinalIgnoreCase)).Single();
        Assert.HasCount(2, result.Areas);
        Assert.AreEqual(artifact.TrainingCount, result.Areas.Sum(area => area.TrainingSupport));
        Assert.AreEqual(artifact.Rows.Length, result.Areas.Sum(area => area.HeldOutSupport));
        string summary = File.ReadAllText(Directory.GetFiles(directory, "training-summary-*.md").Single());
        Assert.IsTrue(Regex.IsMatch(summary, @"\r?\n\r?\n> \[!NOTE\]"), summary);
    }

    [TestMethod]
    public async Task OfflineCliWorksWithoutGitHubTokenAndLeavesModelUnchanged()
    {
        string modelPath = Path.Combine(directory, "model.zip");
        File.WriteAllText(modelPath, "model identity; the evaluator must not load or refit weights");
        string hash = EvaluationArtifact.HashFile(modelPath);
        string scoresPath = EvaluationArtifact.GetPath(modelPath);
        CreateArtifact(modelPath).Save(scoresPath);
        var result = await RunTester("--evaluation", scoresPath, "--issues-model", modelPath,
            "--label-prefix", "area-", "--threshold", "0.15");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "Held-out evaluation");
        StringAssert.Contains(result.Output, "Threshold: **0.15**");
        StringAssert.Contains(result.Output, "Total: **1**");
        StringAssert.Contains(result.Output, "**100.00 %** precision, **100.00 %** coverage");
        StringAssert.Contains(result.Output, "1 correct, 0 wrong, 0 abstained");
        Assert.AreEqual(hash, EvaluationArtifact.HashFile(modelPath));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "data.tsv")));
    }

    [TestMethod]
    public async Task OfflineCliDoesNotClaimPerfectPrecisionForAbstention()
    {
        string modelPath = Path.Combine(directory, "model.zip");
        File.WriteAllText(modelPath, "model identity");
        var artifact = CreateArtifact(modelPath);
        artifact.Rows[0] = new() { Index = 0, Label = "area-B", Scores = [0.7f, 0.3f] };
        string scoresPath = EvaluationArtifact.GetPath(modelPath);
        artifact.Save(scoresPath);
        var evaluation = await RunTester("--evaluation", scoresPath, "--issues-model", modelPath,
            "--label-prefix", "area-", "--threshold", "1");
        Assert.AreEqual(0, evaluation.ExitCode, evaluation.Output);
        StringAssert.Contains(evaluation.Output, "**N/A** precision");
        StringAssert.Contains(evaluation.Output, "0 correct, 0 wrong, 1 abstained");
    }

    [TestMethod]
    public void OfflineReferenceThresholdParsingUsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var provider = new ServiceCollection().AddGitHubActionsCore().BuildServiceProvider();
            var action = provider.GetRequiredService<ICoreService>();
            var parsed = Tester::Args.Parse(
                ["--evaluation", "scores.json", "--issues-model", "model.zip", "--label-prefix", "area-",
                 "--threshold", "0.15"], action)
                ?? throw new AssertFailedException("Valid offline arguments were rejected.");
            Assert.AreEqual(0.15f, parsed.Threshold);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void HistoricalThresholdParsingKeepsCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        string? previousToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "test-token");
            using var provider = new ServiceCollection().AddGitHubActionsCore().BuildServiceProvider();
            var action = provider.GetRequiredService<ICoreService>();
            var parsed = Tester::Args.Parse(
                ["--repo", "dotnet/example", "--issues-model", "model.zip", "--label-prefix", "area-", "--threshold", "0,15"], action)
                ?? throw new AssertFailedException("Valid historical arguments were rejected.");
            Assert.AreEqual(0.15f, parsed.Threshold);
            Assert.IsNull(parsed.EvaluationPath);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousToken);
        }
    }

    [TestMethod]
    public async Task OfflineCliRejectsMismatchedModelsAndHistoricalOptions()
    {
        string modelPath = Path.Combine(directory, "model.zip");
        File.WriteAllText(modelPath, "first model");
        string scoresPath = EvaluationArtifact.GetPath(modelPath);
        CreateArtifact(modelPath).Save(scoresPath);
        var wrongType = await RunTester("--evaluation", scoresPath, "--pulls-model", modelPath, "--label-prefix", "area-");
        Assert.AreEqual(1, wrongType.ExitCode, wrongType.Output);
        StringAssert.Contains(wrongType.Output, "different model type");
        File.WriteAllText(modelPath, "second model");
        var mismatch = await RunTester("--evaluation", scoresPath, "--issues-model", modelPath, "--label-prefix", "area-");
        Assert.AreEqual(1, mismatch.ExitCode, mismatch.Output);
        StringAssert.Contains(mismatch.Output, "does not match");
        var mixed = await RunTester("--evaluation", scoresPath, "--issues-model", modelPath,
            "--label-prefix", "area-", "--repo", "dotnet/example");
        Assert.AreEqual(1, mixed.ExitCode);
        StringAssert.Contains(mixed.Output, "historical download/filter options are not allowed");
    }

    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-0.1")]
    [DataRow("1.1")]
    [DataRow("0,5")]
    [DataRow("0.2,0.8")]
    [TestMethod]
    public async Task OfflineCliRejectsInvalidThresholds(string threshold)
    {
        var result = await RunTester("--evaluation", "scores.json", "--issues-model", "model.zip",
            "--label-prefix", "area-", "--threshold", threshold);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "one invariant decimal value between 0 and 1");
    }

    [TestMethod]
    public async Task OfflineCliRequiresExactlyOneModelAndRejectsPartialPrefixes()
    {
        var noModel = await RunTester("--evaluation", "scores.json", "--label-prefix", "area-");
        Assert.AreEqual(1, noModel.ExitCode);
        StringAssert.Contains(noModel.Output, "exactly one model");
        var bothModels = await RunTester("--evaluation", "scores.json", "--issues-model", "model.zip",
            "--pulls-model", "model.zip", "--label-prefix", "area-");
        Assert.AreEqual(1, bothModels.ExitCode);
        StringAssert.Contains(bothModels.Output, "exactly one model");
        string modelPath = Path.Combine(directory, "model.zip");
        File.WriteAllText(modelPath, "model identity");
        string scoresPath = EvaluationArtifact.GetPath(modelPath);
        CreateArtifact(modelPath).Save(scoresPath);
        var prefix = await RunTester("--evaluation", scoresPath, "--issues-model", modelPath, "--label-prefix", "other-");
        Assert.AreEqual(1, prefix.ExitCode);
        StringAssert.Contains(prefix.Output, "prefix must include every model and reference label");
    }

    [TestMethod]
    public async Task OfflineCliReportsAnEmptyPrefixExplicitly()
    {
        var result = await RunTester("--evaluation", "scores.json", "--issues-model", "model.zip", "--label-prefix", "");
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Output, "requires a valid, nonempty label prefix");
    }

    [TestMethod]
    public async Task HistoricalCliStillRequiresCredentials()
    {
        var historical = await RunTester("--repo", "dotnet/example", "--label-prefix", "area-", "--issues-model", "model.zip");
        Assert.AreEqual(1, historical.ExitCode);
        StringAssert.Contains(historical.Output, "GITHUB_TOKEN is empty");
    }

    private static EvaluationArtifact CreateArtifact(string modelPath) => new()
    {
        ModelSha256 = EvaluationArtifact.HashFile(modelPath),
        DatasetSha256 = EvaluationArtifact.HashFile(modelPath),
        ToolVersion = "test",
        ModelType = "Issue",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        TrainingCount = 20,
        TrainingSupport = new() { ["area-A"] = 10, ["area-B"] = 10 },
        Labels = ["area-A", "area-B"],
        Rows = [new() { Index = 0, Label = "area-A", Scores = [0.7f, 0.3f] }]
    };

    private Task<(int ExitCode, string Output)> RunTester(params string[] arguments) => RunTool("Tester", arguments);

    private async Task<(int ExitCode, string Output)> RunTool(string tool, params string[] arguments)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "IssueLabeler.sln")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root, "Cannot locate the solution's test outputs.");
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string executable = Path.Combine(root.FullName, "artifacts", "bin", tool, configuration, $"{tool}.dll");
        Assert.IsTrue(File.Exists(executable), $"{tool} was not built at '{executable}'.");
        using var process = new Process
        {
            StartInfo = new("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add(executable);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.StartInfo.Environment.Remove("GITHUB_TOKEN");
        process.StartInfo.Environment.Remove("GITHUB_STEP_SUMMARY");
        if (tool == "Trainer")
        {
            string summaryPath = Path.Combine(directory, $"training-summary-{Guid.NewGuid():N}.md");
            File.WriteAllText(summaryPath, string.Empty);
            process.StartInfo.Environment["GITHUB_STEP_SUMMARY"] = summaryPath;
        }
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    public sealed class Sample
    {
        public string Label { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string FileNames { get; set; } = string.Empty;
        public string FolderNames { get; set; } = string.Empty;
    }
}
