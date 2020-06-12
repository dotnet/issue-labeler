// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Data;
using System;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    public class PrModel : IssueModel
    {
        [LoadColumn(7)]
        public Single FileCount;

        [LoadColumn(8)]
        public string Files;

        [LoadColumn(9)]
        public string Filenames;

        [LoadColumn(10)]
        public string FileExtensions;

        [LoadColumn(11)]
        public string FolderNames;

        [LoadColumn(12)]
        public string Folders;

        [NoColumn]
        public bool ShouldAddDoc { get; set; } = false;
    }
}
