extern alias Trainer;

using System.Diagnostics;
using Microsoft.ML;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Tester.Tests;

[TestClass]
public sealed class HeldOutModelTesterTests
{
    private string directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        directory = Directory.CreateTempSubdirectory("issue-labeler-heldout-").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    [DataRow("issues", "Issues", "0.4")]
    [DataRow("issues", "Issues", "0")]
    [DataRow("pulls", "Pull Requests", "0.4")]
    [DataRow("discussions", "Discussions", "0.4")]
    public async Task TrainerSavesFrozenTestSetAndTesterEvaluatesItOffline(string type, string heading, string threshold)
    {
        string dataPath = Path.Combine(directory, $"{type}-data.tsv");
        string modelPath = Path.Combine(directory, $"{type}-model.zip");
        string testDataPath = Path.ChangeExtension(modelPath, ".test-data.bin");
        bool isPull = type == "pulls";
        string[] records = Enumerable.Range(0, 200)
            .Select(index =>
            {
                string label = index % 2 == 0 ? "area-runtime" : "area-ui";
                return isPull
                    ? DataFileUtils.FormatPullRequestRecord(label, $"Item {index}", $"Details for {label}", [$"File{index}.cs"], [$"Folder{index}"])
                    : DataFileUtils.FormatIssueRecord(label, $"Item {index}", $"Details for {label}");
            })
            .Prepend(isPull ? "Label\tTitle\tBody\tFileNames\tFolderNames" : "Label\tTitle\tBody")
            .ToArray();
        await File.WriteAllLinesAsync(dataPath, records);

        var training = await Run(
            typeof(Trainer::Args).Assembly.Location,
            isPull ? "--pulls-data" : "--issues-data", dataPath,
            isPull ? "--pulls-model" : "--issues-model", modelPath);

        Assert.AreEqual(0, training.ExitCode, training.Output);
        Assert.IsTrue(File.Exists(modelPath));
        Assert.IsTrue(File.Exists(testDataPath));

        var context = new MLContext();
        var heldOutData = context.Data.LoadFromBinary(testDataPath);
        string[] heldOutTitles;
        LabelPrediction[] expected;
        using (heldOutData as IDisposable)
        {
            heldOutTitles = context.Data.CreateEnumerable<TestRow>(heldOutData, reuseRowObject: false, ignoreMissingColumns: true)
                .Select(row => row.Title)
                .ToArray();
            Assert.IsGreaterThan(0, heldOutTitles.Length);
            Assert.IsLessThan(200, heldOutTitles.Length);
            Assert.AreEqual(heldOutTitles.Length, heldOutTitles.Distinct().Count());
            Assert.IsTrue(heldOutTitles.All(title => title.StartsWith("Item ", StringComparison.Ordinal)));
            Assert.AreEqual(isPull, heldOutData.Schema.Any(column => column.Name == nameof(TestRow.FileNames)));

            var model = context.Model.Load(modelPath, out _);
            expected = context.Data.CreateEnumerable<LabelPrediction>(
                model.Transform(heldOutData), reuseRowObject: false).ToArray();

            var input = context.Data.CreateEnumerable<TestRow>(heldOutData, reuseRowObject: false, ignoreMissingColumns: true).First();
            using var predictor = context.Model.CreatePredictionEngine<TestRow, FeaturePrediction>(model);
            var original = predictor.Predict(input);
            foreach (string label in new[] { "area-runtime", "area-ui", string.Empty })
            {
                input.Label = label;
                var prediction = predictor.Predict(input);
                CollectionAssert.AreEqual(original.Features, prediction.Features, "The target label must not contribute to features.");
                CollectionAssert.AreEqual(original.Score, prediction.Score, "Scoring must not depend on the target label.");
            }
        }
        var actual = HeldOutModelTester.Predict(modelPath, 0.4f).ToArray();
        Assert.AreEqual(heldOutTitles.Length, actual.Length);
        CollectionAssert.AreEqual(expected.Select(row => row.Label).ToArray(), actual.Select(row => row.Label).ToArray());

        var before = actual.ToArray();
        await File.WriteAllTextAsync(dataPath, "Changed source data must not affect evaluation.");
        CollectionAssert.AreEqual(before, HeldOutModelTester.Predict(modelPath, 0.4f).ToArray());

        List<string> testArgs = [isPull ? "--pulls-model" : "--issues-model", modelPath, "--threshold", threshold];
        if (type == "discussions")
        {
            testArgs.Add("--discussions");
        }
        var testing = await Run(typeof(HeldOutModelTester).Assembly.Location, [.. testArgs]);
        Assert.AreEqual(0, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, $"Finished Testing {heading}");
        StringAssert.Contains(testing.Output, $"**{heldOutTitles.Length}** items were tested");
        if (type == "discussions")
        {
            Assert.IsFalse(testing.Output.Contains("Finished Testing Issues", StringComparison.Ordinal));
        }

        File.Delete(testDataPath);
        using var modelOnlyPredictor = context.Model.CreatePredictionEngine<TestRow, LabelPrediction>(
            context.Model.Load(modelPath, out _));
        Assert.IsGreaterThan(0, modelOnlyPredictor.Predict(new TestRow { Title = "Item 0", Body = "Details" }).Score.Length);
        var missing = await Run(typeof(HeldOutModelTester).Assembly.Location, [.. testArgs]);
        Assert.AreEqual(1, missing.ExitCode, missing.Output);
        StringAssert.Contains(missing.Output, "saved held-out dataset is missing");
        Assert.IsFalse(missing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingTestSetFailsInsteadOfUsingLiveData()
    {
        string modelPath = Path.Combine(directory, "model.zip");

        var exception = Assert.Throws<FileNotFoundException>(() => HeldOutModelTester.Predict(modelPath, 0.4f).ToArray());

        StringAssert.Contains(exception.Message, "Retrain");
        Assert.AreEqual(Path.ChangeExtension(modelPath, ".test-data.bin"), exception.FileName);
    }

    [TestMethod]
    public async Task EmptyTestSetFails()
    {
        string modelPath = CreateModel();
        var context = new MLContext();
        var data = context.Data.LoadFromEnumerable(Array.Empty<TestRow>());
        using (var stream = File.Create(Path.ChangeExtension(modelPath, ".test-data.bin")))
        {
            context.Data.SaveAsBinary(data, stream);
        }

        var exception = Assert.Throws<InvalidOperationException>(() => HeldOutModelTester.Predict(modelPath, 0.4f).ToArray());

        StringAssert.Contains(exception.Message, "empty");
        var testing = await Run(typeof(HeldOutModelTester).Assembly.Location, "--issues-model", modelPath);
        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "empty");
        Assert.IsFalse(testing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CorruptTestSetFails()
    {
        string modelPath = CreateModel();
        await File.WriteAllTextAsync(Path.ChangeExtension(modelPath, ".test-data.bin"), "Not an ML.NET dataset.");

        var testing = await Run(typeof(HeldOutModelTester).Assembly.Location, "--issues-model", modelPath);

        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "Exception occurred");
        Assert.IsFalse(testing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FailedModelDoesNotHideSuccessfulModelResults()
    {
        string modelPath = CreateModel();
        string missingModelPath = Path.Combine(directory, "missing-model.zip");
        var context = new MLContext();
        var data = context.Data.LoadFromEnumerable(new[] { new TestRow { Label = "first", Title = "Item 0" } });
        using (var stream = File.Create(Path.ChangeExtension(modelPath, ".test-data.bin")))
        {
            context.Data.SaveAsBinary(data, stream);
        }

        var testing = await Run(
            typeof(HeldOutModelTester).Assembly.Location,
            "--issues-model", modelPath, "--pulls-model", missingModelPath);

        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "saved held-out dataset is missing");
        StringAssert.Contains(testing.Output, "Finished Testing Issues");
        StringAssert.Contains(testing.Output, "**1** items were tested");
        Assert.IsFalse(testing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("-0.01")]
    [DataRow("1.01")]
    [DataRow("NaN")]
    [DataRow("1e100")]
    public async Task InvalidThresholdFailsExplicitly(string threshold)
    {
        var testing = await Run(
            typeof(HeldOutModelTester).Assembly.Location,
            "--issues-model", Path.Combine(directory, "model.zip"), "--threshold", threshold);

        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "threshold must be between 0 and 1, inclusive");
    }

    [TestMethod]
    [DataRow(0.0f)]
    [DataRow(0.4f)]
    public void PredictionUsesHighestScoreAndItsSlotLabel(float threshold)
    {
        var prediction = HeldOutModelTester.SelectPrediction([0.2f, 0.7f, 0.1f], ["first", "second", "third"], threshold);

        Assert.AreEqual("second", prediction.Label);
        Assert.AreEqual(0.7f, prediction.Score);
    }

    [TestMethod]
    public void PredictionIncludesThresholdBoundaryAndPreservesTieOrder()
    {
        var prediction = HeldOutModelTester.SelectPrediction([0.5f, 0.5f], ["first", "second"], 0.5f);

        Assert.AreEqual("first", prediction.Label);
        Assert.AreEqual(0.5f, prediction.Score);
    }

    [TestMethod]
    public void PredictionBelowThresholdReturnsNoPrediction()
    {
        var prediction = HeldOutModelTester.SelectPrediction([0.2f, 0.3f], ["first", "second"], 0.4f);

        Assert.IsNull(prediction.Label);
        Assert.IsNull(prediction.Score);
    }

    [TestMethod]
    public void PredictionWithNoScoresReturnsNoPrediction()
    {
        var prediction = HeldOutModelTester.SelectPrediction([], [], 0.4f);

        Assert.IsNull(prediction.Label);
        Assert.IsNull(prediction.Score);
    }

    [TestMethod]
    public void InconsistentScoreSchemaFails()
    {
        Assert.Throws<InvalidOperationException>(() => HeldOutModelTester.SelectPrediction([0.5f], [], 0.4f));
    }

    private string CreateModel()
    {
        string modelPath = Path.Combine(directory, "model.zip");
        var context = new MLContext(seed: 1);
        var data = context.Data.LoadFromEnumerable(Enumerable.Range(0, 20)
            .Select(index => new TestRow { Label = index % 2 == 0 ? "first" : "second", Title = $"Item {index}" }));
        var pipeline = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(TestRow.Label))
            .Append(context.Transforms.Text.FeaturizeText("Features", nameof(TestRow.Title)))
            .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"));
        var model = pipeline.Fit(data);
        context.Model.Save(model, data.Schema, modelPath);

        return modelPath;
    }

    private async Task<(int ExitCode, string Output)> Run(string assemblyPath, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        start.ArgumentList.Add(assemblyPath);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment.Remove("GITHUB_TOKEN");
        start.Environment.Remove("GITHUB_REPOSITORY");
        string summaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(summaryPath, string.Empty);
        start.Environment["GITHUB_STEP_SUMMARY"] = summaryPath;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the labeler.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        string summary = File.Exists(summaryPath) ? await File.ReadAllTextAsync(summaryPath) : string.Empty;

        return (process.ExitCode, await output + await error + summary);
    }

    public sealed class TestRow
    {
        public string Label { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string FileNames { get; set; } = string.Empty;
        public string FolderNames { get; set; } = string.Empty;
    }

    public sealed class FeaturePrediction
    {
        public float[] Features { get; set; } = [];
        public float[] Score { get; set; } = [];
    }
}
