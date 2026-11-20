// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ML;
using Microsoft.ML.Data;

internal sealed class HeldOutPrediction
{
    [ColumnName(nameof(ReferenceLabelColumn.ReferenceLabel))]
    public string Label { get; set; } = string.Empty;
    public float[] Score { get; set; } = [];
}

internal sealed class ReferenceLabelColumn
{
    public string ReferenceLabel { get; set; } = string.Empty;
}

internal sealed class BlankLabelColumn
{
    public string Label { get; set; } = string.Empty;
}

internal sealed record PrecisionTargetResult(int TargetPrecision, float? Threshold, int Correct, int Accepted)
{
    public double? Precision => Accepted == 0 ? null : (double)Correct / Accepted;
}

internal sealed record HeldOutResult(int Total, PrecisionTargetResult[] Targets)
{
    internal static HeldOutResult Evaluate(MLContext context, ITransformer model, IDataView testSet)
    {
        // Predictions have no label, so score without one. A model that reads the Label column can't see the answer.
        IDataView unlabeledSet = context.Transforms.CopyColumns(nameof(ReferenceLabelColumn.ReferenceLabel), nameof(BlankLabelColumn.Label))
            .Append(context.Transforms.DropColumns(nameof(BlankLabelColumn.Label)))
            .Append(context.Transforms.CustomMapping<ReferenceLabelColumn, BlankLabelColumn>(
                (_, blank) => blank.Label = string.Empty, contractName: null))
            .Fit(testSet)
            .Transform(testSet);
        IDataView predictions = model.Transform(unlabeledSet);

        VBuffer<ReadOnlyMemory<char>> slots = default;
        predictions.Schema[nameof(HeldOutPrediction.Score)].GetSlotNames(ref slots);
        string[] labels = [.. slots.DenseValues().Select(label => label.ToString())];

        return Evaluate(context.Data.CreateEnumerable<HeldOutPrediction>(predictions, reuseRowObject: false), labels);
    }

    internal static HeldOutResult Evaluate(IEnumerable<HeldOutPrediction> predictions, string[] labels)
    {
        List<(float Score, bool Correct)> ranked = [];
        foreach (var prediction in predictions)
        {
            if (string.IsNullOrWhiteSpace(prediction.Label) || labels.Length == 0 || prediction.Score.Length != labels.Length ||
                prediction.Score.Any(score => !float.IsFinite(score)))
            {
                throw new InvalidDataException("A held-out row has an invalid reference label or score vector.");
            }

            int index = Array.IndexOf(prediction.Score, prediction.Score.Max());
            ranked.Add((prediction.Score[index], labels[index].Equals(prediction.Label, StringComparison.OrdinalIgnoreCase)));
        }

        if (ranked.Count == 0)
        {
            throw new InvalidOperationException("The held-out test set is empty. Download more data before training.");
        }

        int[] precisionTargets = [.. Enumerable.Range(40, 56)];
        PrecisionTargetResult[] results = [.. precisionTargets.Select(target => new PrecisionTargetResult(target, null, 0, 0))];
        ranked.Sort((left, right) => right.Score.CompareTo(left.Score));
        int correct = 0;
        for (int index = 0; index < ranked.Count; index++)
        {
            if (ranked[index].Correct)
            {
                correct++;
            }

            // A cutoff must accept all predictions with the same score.
            if (index + 1 < ranked.Count && ranked[index].Score == ranked[index + 1].Score)
            {
                continue;
            }

            int accepted = index + 1;
            for (int targetIndex = 0; targetIndex < results.Length; targetIndex++)
            {
                int target = results[targetIndex].TargetPrecision;
                if ((long)correct * 100 >= (long)target * accepted)
                {
                    results[targetIndex] = new(target, ranked[index].Score, correct, accepted);
                }
            }
        }

        return new(ranked.Count, results);
    }

    public string ToMarkdown(HeldOutResult? active = null, string? activeSkipReason = null)
    {
        int[] highlightedTargets = [40, 50, 60, 70, 80, 85, 90, 95];
        string activeNotice = (active, activeSkipReason) switch
        {
            (not null, _) => "> Note: ACTIVE may have trained on these items, so its results may be optimistic.",
            (null, not null) => $"> Warning: ACTIVE comparison skipped. {activeSkipReason}",
            _ => "No ACTIVE model was provided."
        };
        string chartLines = string.Join(Environment.NewLine, new[] { this, active }
            .OfType<HeldOutResult>()
            .Select((result, index) => $"    line \"{(index == 0 ? "New" : "ACTIVE")}\" [{FormatValues(result, index == 0)}]"));
        string seriesKey = active is null ? "Blue: New." : "Blue: New. Orange: ACTIVE.";

        string FormatValues(HeldOutResult result, bool highlight) => string.Join(", ", result.Targets.Select(target =>
        {
            string coverage = FormatCoverage(target, result.Total);
            return highlight && highlightedTargets.Contains(target.TargetPrecision)
                ? $"{coverage} \"{target.TargetPrecision}%\""
                : coverage;
        }));

        return FormattableString.Invariant($"""
            ### Held-out evaluation

            Evaluated {Total} items from the random 20% test split, excluded from fitting the new model. Items are scored with the Label column blank, as in prediction.

            {activeNotice}

            ```mermaid
            ---
            config:
              themeVariables:
                xyChart:
                  plotColorPalette: "#0969da, #bc4c00"
            ---
            xychart-beta
                title "Held-out coverage by minimum precision"
                x-axis "Minimum precision (%)" {Targets[0].TargetPrecision} --> {Targets[^1].TargetPrecision}
                y-axis "Coverage (%)" 0 --> 100
            {chartLines}
            ```

            {seriesKey} Each point shows the maximum test-set coverage while meeting its minimum precision target. Labels on the New curve mark selected targets.
            Thresholds are selected and evaluated on this same split, so the curve may be optimistic. Promotion does not update the prediction threshold.
            Only the top-scoring label per item is evaluated.
            """);

        // A target that is not reached accepts no items.
        static string FormatCoverage(PrecisionTargetResult target, int total) => target.Threshold is null
            ? "0"
            : FormattableString.Invariant($"{100.0 * target.Accepted / total:0.#}");
    }
}
