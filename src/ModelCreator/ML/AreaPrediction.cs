using Microsoft.ML.Data;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace ModelCreator.ML;

public class AreaPrediction
{
    [ColumnName("PredictedLabel")]
    public string Area;

    public float[] Score;
}
