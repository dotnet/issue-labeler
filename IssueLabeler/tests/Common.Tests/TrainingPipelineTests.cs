// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms.Text;

namespace Common.Tests;

public class TrainingPipelineTests
{
    [Fact]
    public void TrainerOptions_UseExplicitL2AndRetainAutomaticDefaults()
    {
        var options = TrainingPipeline.CreateTrainerOptions();
        var defaults = new SdcaMaximumEntropyMulticlassTrainer.Options();

        Assert.Equal("LabelKey", options.LabelColumnName);
        Assert.Equal("Features", options.FeatureColumnName);
        Assert.Equal(3e-6f, options.L2Regularization);
        Assert.Null(options.MaximumNumberOfIterations);
        Assert.Null(options.L1Regularization);
        Assert.Null(options.NumberOfThreads);
        Assert.Equal(defaults.ConvergenceTolerance, options.ConvergenceTolerance);
        Assert.Equal(defaults.ConvergenceCheckFrequency, options.ConvergenceCheckFrequency);
        Assert.Equal(defaults.Shuffle, options.Shuffle);
    }

    [Theory]
    [InlineData(ModelType.Issue)]
    [InlineData(ModelType.PullRequest)]
    public void Features_UseWordTfAndCharacterTfIdfWithSeparateL2Normalization(ModelType type)
    {
        var context = new MLContext(seed: 42);
        var data = context.Data.LoadFromEnumerable(new[]
        {
            new Input { Title = "aaa aaa bbb bbb", Body = "ccc" },
            new Input { Title = "aaa ddd", Body = "eee" },
            new Input { Title = "aaa fff", Body = "ggg" }
        });
        var features = TrainingPipeline.CreateFeatures(context, type).Fit(data).Transform(data);
        var baseline = context.Transforms.Text.FeaturizeText(
            "Features", new TextFeaturizingEstimator.Options(), nameof(Input.Title), nameof(Input.Body))
            .Fit(data).Transform(data);
        string[] slots = GetSlots(features);
        Assert.Equal(GetSlots(baseline), slots);
        float[] actual = features.GetColumn<float[]>("Features").First();
        float[][] tfRows = baseline.GetColumn<float[]>("Features").ToArray();
        float[] tf = tfRows[0];
        int[] words = Enumerable.Range(0, slots.Length).Where(i => slots[i].StartsWith("Word.", StringComparison.Ordinal)).ToArray();
        int[] characters = Enumerable.Range(0, slots.Length).Where(i => slots[i].StartsWith("Char.", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(words);
        Assert.NotEmpty(characters);
        Assert.All(characters, i => Assert.Equal(3, slots[i]["Char.".Length..].Split('|').Length));
        Assert.Equal(words.Select(i => tf[i]), words.Select(i => actual[i]));
        Assert.Contains(slots, slot => slot == "Word.aaa");
        Assert.Contains(slots, slot => slot == "Word.aaa|bbb");
        Assert.DoesNotContain(slots, slot => slot == "Word.aaa|aaa|bbb");
        int common = Array.IndexOf(slots, "Char.a|a|a");
        int rare = Array.IndexOf(slots, "Char.b|b|b");
        Assert.True(common >= 0);
        Assert.True(rare >= 0);
        Assert.True(tf[common] > 0);
        Assert.True(tf[rare] > 0);
        Assert.Equal(0f, actual[common]);
        Assert.True(actual[rare] > 0);
        float[] weighted = characters.Select(i =>
            tf[i] * MathF.Log((float)tfRows.Length / tfRows.Count(row => row[i] > 0))).ToArray();
        float norm = MathF.Sqrt(weighted.Sum(value => value * value));
        for (int i = 0; i < characters.Length; i++)
        {
            Assert.InRange(MathF.Abs(actual[characters[i]] - weighted[i] / norm), 0f, 1e-6f);
        }
        Assert.InRange(words.Sum(i => actual[i] * actual[i]), 0.99999f, 1.00001f);
        Assert.InRange(characters.Sum(i => actual[i] * actual[i]), 0.99999f, 1.00001f);
    }

    [Theory]
    [InlineData(ModelType.Issue)]
    [InlineData(ModelType.PullRequest)]
    public void Pipeline_RoundTripsWithoutLabelLeakageAndPreservesInputColumns(ModelType type)
    {
        var context = new MLContext(seed: 42);
        var messages = new ConcurrentQueue<string>();
        context.Log += (_, message) => messages.Enqueue(message.Message);
        var data = context.Data.LoadFromEnumerable(Enumerable.Range(0, 12).Select(i => new Input
        {
            Label = i % 2 == 0 ? "area-ui" : "area-build",
            Title = i % 2 == 0 ? "window rendering" : "compiler build",
            Body = i % 2 == 0 ? "button display" : "project compilation",
            FileNames = i % 2 == 0 ? "window.cs" : "compiler.cs",
            FolderNames = i % 2 == 0 ? "graphics" : "toolchain"
        }));
        var model = TrainingPipeline.Create(context, type).Fit(data);
        var transformed = model.Transform(data);
        string[] slots = GetSlots(transformed);
        Assert.DoesNotContain(slots, slot => slot.Contains("area-", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Auto-tuning parameters: maxIterations =", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("Auto-tuning parameters: L1Threshold (L1/L2) =", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains("Auto-tuning parameters: L2 =", StringComparison.Ordinal));

        using var stream = new MemoryStream();
        context.Model.Save(model, data.Schema, stream);
        stream.Position = 0;
        var inferenceContext = new MLContext();
        var loaded = inferenceContext.Model.Load(stream, out var schema);
        Assert.Equal(data.Schema.Select(column => (column.Name, column.Type)), schema.Select(column => (column.Name, column.Type)));
        using var predictor = inferenceContext.Model.CreatePredictionEngine<Input, Output>(loaded);
        using var originalPredictor = context.Model.CreatePredictionEngine<Input, Output>(model);
        var input = new Input { Title = "window rendering", Body = "button display", FileNames = "window.cs", FolderNames = "graphics" };
        var expected = originalPredictor.Predict(input);
        var unlabeled = predictor.Predict(input);

        Assert.Equal(expected.Features, unlabeled.Features);
        Assert.Equal(expected.Score, unlabeled.Score);
        Assert.Equal(expected.PredictedLabel, unlabeled.PredictedLabel);
        Assert.Equal(2, unlabeled.Score.Length);
        Assert.Contains(unlabeled.PredictedLabel, new[] { "area-ui", "area-build" });
        Assert.All(unlabeled.Score, score => Assert.True(float.IsFinite(score)));

        foreach (string label in new[] { "area-ui", "area-build", "unseen-label" })
        {
            input.Label = label;
            var labeled = predictor.Predict(input);
            Assert.Equal(unlabeled.Features, labeled.Features);
            Assert.Equal(unlabeled.Score, labeled.Score);
            Assert.Equal(unlabeled.PredictedLabel, labeled.PredictedLabel);
        }

        input.Body = "project compilation";
        Assert.False(unlabeled.Features.SequenceEqual(predictor.Predict(input).Features));
        input.Body = "button display";
        input.Title = "compiler build";
        Assert.False(unlabeled.Features.SequenceEqual(predictor.Predict(input).Features));
        input.Title = "window rendering";
        input.FileNames = "compiler.cs";
        var fileChanged = predictor.Predict(input);
        Assert.Equal(type == ModelType.Issue, unlabeled.Features.SequenceEqual(fileChanged.Features));
        input.FileNames = "window.cs";
        input.FolderNames = "toolchain";
        var folderChanged = predictor.Predict(input);
        Assert.Equal(type == ModelType.Issue, unlabeled.Features.SequenceEqual(folderChanged.Features));
    }

    private static string[] GetSlots(IDataView data)
    {
        VBuffer<ReadOnlyMemory<char>> slots = default;
        data.Schema["Features"].GetSlotNames(ref slots);
        return slots.DenseValues().Select(slot => slot.ToString()).ToArray();
    }

    public sealed class Input
    {
        public string Label { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string FileNames { get; set; } = "";
        public string FolderNames { get; set; } = "";
    }

    public sealed class Output
    {
        public float[] Features { get; set; } = [];
        public float[] Score { get; set; } = [];
        public string PredictedLabel { get; set; } = "";
    }
}
