// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Common.Tests
{
    public class LabelUtilsTests
    {
        [Fact]
        public void NormalizeLabels_RemovesWhitespaceAndDuplicates_CaseInsensitive()
        {
            string[] result = LabelUtils.NormalizeLabels([
                " area-foo ",
                "area-foo",
                "AREA-FOO",
                "area-bar",
                ""
            ]);

            Assert.Equal(2, result.Length);
            Assert.Contains("area-foo", result, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("area-bar", result, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void NormalizeLabels_ReturnsEmptyArray_WhenInputIsNull()
        {
            string[] result = LabelUtils.NormalizeLabels(null);
            Assert.Empty(result);
        }
    }
}
