// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace CreateMikLabelModel.DL
{
    public static class LabelHelper
    {
        public static bool IsAreaLabel(string labelName) =>
            labelName.StartsWith("area-", StringComparison.OrdinalIgnoreCase) ||
            labelName.StartsWith("area/", StringComparison.OrdinalIgnoreCase);
    }
}
