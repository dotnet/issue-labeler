extern alias Trainer;

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace IssueLabeler.Tests;

[TestClass]
public sealed class TesterTests
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
    public async Task TrainerAndTesterUseTheDatasetSplitOffline(string type, string heading, string threshold)
    {
        string dataPath = Path.Combine(directory, $"{type}-data.tsv");
        string modelPath = Path.Combine(directory, $"{type}-model.zip");
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
        var split = DatasetSplit.Create(dataPath);
        if (type == "issues" && threshold == "0.4")
        {
            int rowIndex = split.TestRows[0] + 1;
            records[rowIndex] = DataFileUtils.FormatIssueRecord("area-heldout-only", $"Item {rowIndex - 1}", "Held-out reference");
            await File.WriteAllLinesAsync(dataPath, records);
            split = DatasetSplit.Create(dataPath);
        }
        byte[] manifest = File.ReadAllBytes(DatasetSplit.GetPath(dataPath));

        var training = await Run(
            typeof(Trainer::Args).Assembly.Location,
            isPull ? "--pulls-data" : "--issues-data", dataPath,
            isPull ? "--pulls-model" : "--issues-model", modelPath);

        Assert.AreEqual(0, training.ExitCode, training.Output);
        Assert.IsTrue(File.Exists(modelPath));
        Assert.IsFalse(File.Exists(Path.ChangeExtension(modelPath, ".test-data.bin")));
        CollectionAssert.AreEqual(manifest, File.ReadAllBytes(DatasetSplit.GetPath(dataPath)));

        var context = new MLContext();
        var testRows = split.ReadRows(dataPath, test: true).ToArray();
        var heldOutData = isPull
            ? context.Data.LoadFromEnumerable(testRows.Cast<PullRequestDatasetRow>())
            : context.Data.LoadFromEnumerable(testRows);
        string[] heldOutTitles;
        LabelPrediction[] expected;
        using (heldOutData as IDisposable)
        {
            heldOutTitles = context.Data.CreateEnumerable<TestRow>(heldOutData, reuseRowObject: false, ignoreMissingColumns: true)
                .Select(row => row.Title)
                .ToArray();
            Assert.AreEqual(40, heldOutTitles.Length);
            Assert.AreEqual(heldOutTitles.Length, heldOutTitles.Distinct().Count());
            Assert.IsTrue(heldOutTitles.All(title => title.StartsWith("Item ", StringComparison.Ordinal)));
            Assert.AreEqual(isPull, heldOutData.Schema.Any(column => column.Name == nameof(TestRow.FileNames)));
            string[] trainingTitles = [.. split.ReadRows(dataPath, test: false).Select(row => row.Title)];
            Assert.HasCount(160, trainingTitles);
            Assert.IsFalse(trainingTitles.Intersect(heldOutTitles).Any());
            Assert.AreEqual(200, trainingTitles.Concat(heldOutTitles).Distinct().Count());

            var model = context.Model.Load(modelPath, out _);
            VBuffer<ReadOnlyMemory<char>> slots = default;
            model.GetOutputSchema(heldOutData.Schema)[nameof(LabelPrediction.Score)].GetSlotNames(ref slots);
            string[] scoreLabels = [.. slots.DenseValues().Select(label => label.ToString())];
            Assert.IsFalse(scoreLabels.Contains("area-heldout-only"), "A held-out-only label must not enter the fitted model.");
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
        var modelType = isPull ? ModelType.PullRequest : ModelType.Issue;
        var actual = Tester.Predict(modelPath, dataPath, 0.4f, modelType).ToArray();
        Assert.AreEqual(heldOutTitles.Length, actual.Length);
        CollectionAssert.AreEqual(expected.Select(row => row.Label).ToArray(), actual.Select(row => row.Label).ToArray());

        CollectionAssert.AreEqual(actual, Tester.Predict(modelPath, dataPath, 0.4f, modelType).ToArray());

        List<string> testArgs = [isPull ? "--pulls-model" : "--issues-model", modelPath,
            isPull ? "--pulls-data" : "--issues-data", dataPath, "--threshold", threshold];
        if (type == "discussions")
        {
            testArgs.Add("--discussions");
        }
        var testing = await Run(typeof(Tester).Assembly.Location, [.. testArgs]);
        Assert.AreEqual(0, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, $"Finished Testing {heading}");
        StringAssert.Contains(testing.Output, $"**{heldOutTitles.Length}** items were tested");
        if (type == "discussions")
        {
            Assert.IsFalse(testing.Output.Contains("Finished Testing Issues", StringComparison.Ordinal));
        }

        if (type == "issues" && threshold == "0.4")
        {
            string secondModel = Path.Combine(directory, "second-model.zip");
            var secondTraining = await Run(typeof(Trainer::Args).Assembly.Location,
                "--issues-data", dataPath, "--issues-model", secondModel);
            Assert.AreEqual(0, secondTraining.ExitCode, secondTraining.Output);
            CollectionAssert.AreEqual(manifest, File.ReadAllBytes(DatasetSplit.GetPath(dataPath)));
            Assert.HasCount(40, Tester.Predict(secondModel, dataPath, 0.4f).ToArray());

            string promotedModel = Path.Combine(directory, "active-model.zip");
            File.Copy(modelPath, promotedModel);
            CollectionAssert.AreEqual(actual, Tester.Predict(promotedModel, dataPath, 0.4f).ToArray());
        }

        File.Delete(dataPath);
        File.Delete(DatasetSplit.GetPath(dataPath));
        using var modelOnlyPredictor = context.Model.CreatePredictionEngine<TestRow, LabelPrediction>(
            context.Model.Load(modelPath, out _));
        Assert.IsGreaterThan(0, modelOnlyPredictor.Predict(new TestRow { Title = "Item 0", Body = "Details" }).Score.Length);
        var missing = await Run(typeof(Tester).Assembly.Location, [.. testArgs]);
        Assert.AreEqual(1, missing.ExitCode, missing.Output);
        StringAssert.Contains(missing.Output, "original dataset or its split manifest is unavailable");
        Assert.IsFalse(missing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingTestSetFailsInsteadOfUsingLiveData()
    {
        string modelPath = Path.Combine(directory, "model.zip");
        string dataPath = Path.Combine(directory, "data.tsv");

        var exception = Assert.Throws<FileNotFoundException>(() => Tester.Predict(modelPath, dataPath, 0.4f).ToArray());

        StringAssert.Contains(exception.Message, "unavailable");
        Assert.AreEqual(dataPath, exception.FileName);
    }

    [TestMethod]
    public async Task EmptyTestSetFails()
    {
        string modelPath = CreateModel();
        string dataPath = Path.Combine(directory, "data.tsv");
        File.WriteAllText(dataPath, "Label\tTitle\tBody\r\n");
        DatasetSplit.Create(dataPath).WriteModelReference(modelPath);

        var exception = Assert.Throws<InvalidOperationException>(() => Tester.Predict(modelPath, dataPath, 0.4f).ToArray());

        StringAssert.Contains(exception.Message, "no held-out rows");
        var testing = await Run(typeof(Tester).Assembly.Location, "--issues-model", modelPath, "--issues-data", dataPath);
        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "no held-out rows");
        Assert.IsFalse(testing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CorruptTestSetFails()
    {
        string modelPath = CreateModel();
        string dataPath = Path.Combine(directory, "data.tsv");
        await File.WriteAllTextAsync(DatasetSplit.GetPath(dataPath), "Not a split manifest.");

        var testing = await Run(typeof(Tester).Assembly.Location, "--issues-model", modelPath, "--issues-data", dataPath);

        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "Exception occurred");
        Assert.IsFalse(testing.Output.Contains("Unhandled exception", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FailedModelDoesNotHideSuccessfulModelResults()
    {
        string modelPath = CreateModel();
        string missingModelPath = Path.Combine(directory, "missing-model.zip");
        string dataPath = Path.Combine(directory, "data.tsv");

        var testing = await Run(
            typeof(Tester).Assembly.Location,
            "--issues-model", modelPath, "--issues-data", dataPath,
            "--pulls-model", missingModelPath, "--pulls-data", Path.Combine(directory, "missing-data.tsv"));

        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "original dataset or its split manifest is unavailable");
        StringAssert.Contains(testing.Output, "Finished Testing Issues");
        StringAssert.Contains(testing.Output, "**4** items were tested");
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
            typeof(Tester).Assembly.Location,
            "--issues-model", Path.Combine(directory, "model.zip"), "--issues-data", Path.Combine(directory, "data.tsv"),
            "--threshold", threshold);

        Assert.AreEqual(1, testing.ExitCode, testing.Output);
        StringAssert.Contains(testing.Output, "threshold must be between 0 and 1, inclusive");
    }

    [TestMethod]
    public void DatasetAndManifestMustMatchTheModel()
    {
        string modelPath = CreateModel();
        string dataPath = Path.Combine(directory, "data.tsv");
        byte[] originalData = File.ReadAllBytes(dataPath);
        File.AppendAllText(dataPath, "first\tExtra item\tDetails\r\n");
        Assert.Throws<InvalidDataException>(() => Tester.Predict(modelPath, dataPath, 0.4f).ToArray());
        File.WriteAllBytes(dataPath, originalData);

        string manifestPath = DatasetSplit.GetPath(dataPath);
        var json = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        json["testRows"] = new JsonArray(0, 1, 2, 3);
        File.WriteAllText(manifestPath, json.ToJsonString());
        var mismatch = Assert.Throws<InvalidDataException>(() => Tester.Predict(modelPath, dataPath, 0.4f).ToArray());
        StringAssert.Contains(mismatch.Message, "different dataset or split");
    }

    [TestMethod]
    public void LegacyModelCannotGuessADatasetSplit()
    {
        string modelPath = CreateModel();
        using (var archive = ZipFile.Open(modelPath, ZipArchiveMode.Update))
        {
            archive.GetEntry("DatasetSplit.sha256")!.Delete();
        }
        var exception = Assert.Throws<InvalidDataException>(() => Tester.Predict(
            modelPath, Path.Combine(directory, "data.tsv"), 0.4f).ToArray());
        StringAssert.Contains(exception.Message, "no dataset split reference");
    }

    [TestMethod]
    public void InvalidMembershipAndSchemaAreRejected()
    {
        CreateModel();
        string dataPath = Path.Combine(directory, "data.tsv");
        string path = DatasetSplit.GetPath(dataPath);
        string original = File.ReadAllText(path);
        foreach (int[] rows in new[] { new[] { -1 }, new[] { 20 }, new[] { 1, 1 }, new[] { 2, 1 } })
        {
            var json = JsonNode.Parse(original)!.AsObject();
            json["testRows"] = new JsonArray([.. rows.Select(row => JsonValue.Create(row))]);
            File.WriteAllText(path, json.ToJsonString());
            Assert.Throws<InvalidDataException>(() => DatasetSplit.Load(dataPath));
        }
        var wrongSchema = JsonNode.Parse(original)!.AsObject();
        wrongSchema["schemaVersion"] = 2;
        File.WriteAllText(path, wrongSchema.ToJsonString());
        Assert.Throws<InvalidDataException>(() => DatasetSplit.Load(dataPath));
        wrongSchema.Remove("schemaVersion");
        File.WriteAllText(path, wrongSchema.ToJsonString());
        Assert.Throws<System.Text.Json.JsonException>(() => DatasetSplit.Load(dataPath));
    }

    [TestMethod]
    public void SplitRowsAreRepeatableAndPreserveTsvFields()
    {
        string dataPath = Path.Combine(directory, "fields.tsv");
        File.WriteAllText(dataPath, "Label\tTitle\tBody\r\n first \t Title `quoted` \t \r\nsecond\tOther\tBody\r\n");
        var split = DatasetSplit.Create(dataPath);
        var loaded = DatasetSplit.Load(dataPath);
        DatasetRow[] allRows = [.. loaded.ReadRows(dataPath, test: false).Concat(loaded.ReadRows(dataPath, test: true))];
        Assert.HasCount(2, allRows);
        var first = allRows.Single(row => row.Label == "first ");
        Assert.AreEqual("Title `quoted`", first.Title);
        Assert.AreEqual("", first.Body);
        var context = new MLContext();
        var loader = context.Data.CreateTextLoader(new TextLoader.Options
        {
            AllowQuoting = false,
            AllowSparse = false,
            HasHeader = true,
            ReadMultilines = false,
            Separators = ['\t'],
            TrimWhitespace = true,
            Columns = [new("Label", DataKind.String, 0), new("Title", DataKind.String, 1), new("Body", DataKind.String, 2)]
        });
        foreach (var original in context.Data.CreateEnumerable<DatasetRow>(loader.Load(dataPath), reuseRowObject: false))
        {
            var row = allRows.Single(row => row.Label == original.Label);
            Assert.AreEqual(original.Label, row.Label);
            Assert.AreEqual(original.Title, row.Title);
            Assert.AreEqual(original.Body, row.Body);
        }
        CollectionAssert.AreEqual(split.TestRows, loaded.TestRows);
        CollectionAssert.AreEqual(loaded.ReadRows(dataPath, test: true).Select(row => row.Title).ToArray(),
            loaded.ReadRows(dataPath, test: true).Select(row => row.Title).ToArray());
        File.AppendAllText(dataPath, "\r\n");
        Assert.Throws<InvalidDataException>(() => DatasetSplit.Create(dataPath));
    }

    [TestMethod]
    public async Task MissingDatasetArgumentFailsExplicitly()
    {
        var testing = await Run(typeof(Tester).Assembly.Location, "--issues-model", "model.zip");
        Assert.AreEqual(1, testing.ExitCode);
        StringAssert.Contains(testing.Output, "model and its original dataset path");
    }

    [TestMethod]
    public void EvaluationDoesNotHoldDatasetFilesOpen()
    {
        string modelPath = CreateModel();
        string dataPath = Path.Combine(directory, "data.tsv");
        using var predictions = Tester.Predict(modelPath, dataPath, 0.4f).GetEnumerator();
        Assert.IsTrue(predictions.MoveNext());
        File.Delete(dataPath);
        File.Delete(DatasetSplit.GetPath(dataPath));
    }

    [TestMethod]
    [DataRow(0.0f)]
    [DataRow(0.4f)]
    public void PredictionUsesHighestScoreAndItsSlotLabel(float threshold)
    {
        var prediction = Tester.SelectPrediction([0.2f, 0.7f, 0.1f], ["first", "second", "third"], threshold);

        Assert.AreEqual("second", prediction.Label);
        Assert.AreEqual(0.7f, prediction.Score);
    }

    [TestMethod]
    public void PredictionIncludesThresholdBoundaryAndPreservesTieOrder()
    {
        var prediction = Tester.SelectPrediction([0.5f, 0.5f], ["first", "second"], 0.5f);

        Assert.AreEqual("first", prediction.Label);
        Assert.AreEqual(0.5f, prediction.Score);
    }

    [TestMethod]
    public void PredictionBelowThresholdReturnsNoPrediction()
    {
        var prediction = Tester.SelectPrediction([0.2f, 0.3f], ["first", "second"], 0.4f);

        Assert.IsNull(prediction.Label);
        Assert.IsNull(prediction.Score);
    }

    [TestMethod]
    public void PredictionWithNoScoresReturnsNoPrediction()
    {
        var prediction = Tester.SelectPrediction([], [], 0.4f);

        Assert.IsNull(prediction.Label);
        Assert.IsNull(prediction.Score);
    }

    [TestMethod]
    public void InconsistentScoreSchemaFails()
    {
        Assert.Throws<InvalidOperationException>(() => Tester.SelectPrediction([0.5f], [], 0.4f));
    }

    private string CreateModel()
    {
        string modelPath = Path.Combine(directory, "model.zip");
        var context = new MLContext(seed: 1);
        string dataPath = Path.Combine(directory, "data.tsv");
        File.WriteAllLines(dataPath, new[] { "Label\tTitle\tBody" }.Concat(Enumerable.Range(0, 20)
            .Select(index => DataFileUtils.FormatIssueRecord(index % 2 == 0 ? "first" : "second", $"Item {index}", "Details"))));
        var split = DatasetSplit.Create(dataPath);
        var data = context.Data.LoadFromEnumerable(split.ReadRows(dataPath, test: false).ToArray());
        var pipeline = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(TestRow.Label))
            .Append(context.Transforms.Text.FeaturizeText("Features", nameof(TestRow.Title)))
            .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey"));
        var model = pipeline.Fit(data);
        context.Model.Save(model, data.Schema, modelPath);
        split.WriteModelReference(modelPath);

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
