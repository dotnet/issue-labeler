// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.DotNet.Github.IssueLabeler.Helpers
{
    public class DiffHelper
    {
        /// <summary>
        /// name of files taken from fileDiffs
        /// </summary>
        public IEnumerable<string> FilenamesOf(string[] fileDiffs) => fileDiffs.Select(fileWithDiff => Path.GetFileNameWithoutExtension(fileWithDiff));

        /// <summary>
        /// file extensions taken from fileDiffs
        /// </summary>
        public IEnumerable<string> ExtensionsOf(string[] fileDiffs) => fileDiffs.Select(file => Path.GetExtension(file)).
                Select(extension => string.IsNullOrEmpty(extension) ? "no_extension" : extension);

        public (string[] fileDiffs, IEnumerable<string> filenames, IEnumerable<string> extensions, Dictionary<string, int> folders, Dictionary<string, int> folderNames, bool addDocInfo) SegmentDiff(string[] fileDiffs)
        {
            if (fileDiffs == null || string.IsNullOrEmpty(string.Join(';', fileDiffs)))
            {
                throw new ArgumentNullException(nameof(fileDiffs));
            }
            var folderNames = new Dictionary<string, int>();
            var folders = new Dictionary<string, int>();
            bool addDocInfo = false;
            string folderWithDiff, subfolder;
            string[] folderNamesInPr;
            foreach (var fileWithDiff in fileDiffs)
            {
                folderWithDiff = Path.GetDirectoryName(fileWithDiff) ?? string.Empty;
                folderNamesInPr = folderWithDiff.Split(Path.DirectorySeparatorChar);
                subfolder = string.Empty;
                if (!string.IsNullOrEmpty(folderWithDiff))
                {
                    foreach (var folderNameInPr in folderNamesInPr)
                    {
                        if (folderNameInPr.Equals("ref") &&
                            subfolder.StartsWith("src" + Path.DirectorySeparatorChar + "libraries") &&
                            Path.GetExtension(fileWithDiff).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                        {
                            addDocInfo = true;
                        }
                        subfolder += folderNameInPr;
                        if (folderNames.ContainsKey(folderNameInPr))
                        {
                            folderNames[folderNameInPr] += 1;
                        }
                        else
                        {
                            folderNames.Add(folderNameInPr, 1);
                        }
                        if (folders.ContainsKey(subfolder))
                        {
                            folders[subfolder] += 1;
                        }
                        else
                        {
                            folders.Add(subfolder, 1);
                        }
                        subfolder += Path.DirectorySeparatorChar;
                    }
                }
            }
            return (fileDiffs, FilenamesOf(fileDiffs), ExtensionsOf(fileDiffs), folders, folderNames, addDocInfo);
        }
    }
}
