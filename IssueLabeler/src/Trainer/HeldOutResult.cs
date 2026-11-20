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

        int[] precisionTargets = [40, 50, 60, 70, 80, 85, 90, 95, 98, 99];
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
        string activeNotice = (active, activeSkipReason) switch
        {
            (not null, _) => "> Note: ACTIVE may have trained on these items, so its results may be optimistic.",
            (null, not null) => $"> Warning: ACTIVE comparison skipped. {activeSkipReason}",
            _ => "No ACTIVE model was provided."
        };
        string activeHeader = active is null ? string.Empty : " ACTIVE threshold | ACTIVE precision | ACTIVE coverage |";
        string activeSeparator = active is null ? string.Empty : "---:|---:|---:|";
        var rows = Targets.Select((target, index) =>
        {
            string activeCells = active is null ? string.Empty : $" {FormatResult(active.Targets[index])} |";
            return $"| {target.TargetPrecision}% | {FormatResult(target)} |{activeCells}";
        });

        return FormattableString.Invariant($"""
            ### Held-out evaluation

            Evaluated {Total} items from the random 20% test split, excluded from fitting the new model. Items are scored with the Label column blank, as in prediction.

            {activeNotice}

            Each row maximizes coverage at or above the minimum precision. Threshold is the minimum prediction score.

            | Minimum precision | New threshold | New precision | New coverage |{activeHeader}
            |---:|---:|---:|---:|{activeSeparator}
            {string.Join(Environment.NewLine, rows)}

            Only the top-scoring label of each item is evaluated. Precision is the percentage of accepted predictions that are correct. Coverage is the percentage of test items accepted (count in parentheses).
            Not reached means no cutoff with accepted predictions met the minimum precision.
            Thresholds are selected and evaluated on this same split. Promotion does not update the prediction threshold.
            """);

        string FormatResult(PrecisionTargetResult target) => target.Threshold is float threshold
            ? FormattableString.Invariant($"{threshold:R} | {target.Precision:P2} | {(double)target.Accepted / Total:P2} ({target.Accepted})")
            : "Not reached | N/A | N/A";
    }
}
