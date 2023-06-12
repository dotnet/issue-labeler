// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace IssueLabelerService.Models;

// Meant to deserialize a JSON response like this:
//{
//    "labelScores":
//    [
//        {
//            "labelName": "area-infrastructure",
//            "score": 0.988357544
//        },
//        {
//            "labelName": "area-mvc",
//            "score": 0.008182112
//        },
//        {
//            "labelName": "area-servers",
//            "score": 0.002301987
//        }
//    ]
//}

public sealed class RemoteLabelPrediction
{
    public List<RemoteLabelPredictionScore> LabelScores { get; set; }
}

public sealed class RemoteLabelPredictionScore
{
    public string LabelName { get; set; }
    public float Score { get; set; }
}
