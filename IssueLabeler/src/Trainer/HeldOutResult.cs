// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

internal sealed class HeldOutPrediction
{
    public string Label { get; set; } = string.Empty;
    public float[] Score { get; set; } = [];
}

internal sealed record PrecisionTargetResult(int TargetPrecision, float? Threshold, int Correct, int Accepted)
{
    public double? Precision => Accepted == 0 ? null : (double)Correct / Accepted;
}

internal sealed record HeldOutResult(int Total, PrecisionTargetResult[] Targets)
{
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

        int[] precisionTargets = [60, 70, 80, 90, 95];
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

    public string ToMarkdown()
    {
        var rows = Targets.Select(target => target.Threshold is float threshold
            ? FormattableString.Invariant($"| {target.TargetPrecision}% | {threshold:R} | {target.Precision:P2} | {(double)target.Accepted / Total:P2} | {target.Accepted} |")
            : FormattableString.Invariant($"| {target.TargetPrecision}% | Not reached | N/A | N/A | N/A |"));
        return FormattableString.Invariant($"""
            ### Held-out evaluation

            Evaluated {Total} items from the random 20% test split, excluded from model fitting.
            Each row aims to label as many test items as possible, with at least the target percentage labeled correctly.
            Threshold is the minimum score required to use a prediction. Accepted is the number of predictions kept at this cutoff, including incorrect predictions.

            | Target precision | Threshold | Observed precision | Coverage | Accepted |
            |---:|---:|---:|---:|---:|
            {string.Join(Environment.NewLine, rows)}

            Precision is the percentage of accepted predictions that are correct. Coverage is the percentage of all test items accepted.
            Thresholds are selected and evaluated on this same split.
            """);
    }
}
