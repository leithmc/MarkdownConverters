using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections.Generic;

namespace _2md
{
    class TOCBuilder
    {
        /// <summary>
        /// A dictionary that maps source html file path keys to destination markdown file path values.
        /// </summary>
        internal Dictionary<string, string> FileMap = new Dictionary<string, string>();
        int MaxNameSegmentLength = 50;
        internal bool IsRootSet;
        internal string RootCollectionName = "";

        /// <summary>
        /// An internal representation of the .hxtx file which extends the .hxt file to include markdown path and asset id.
        /// </summary>
        internal XDocument HxtxDoc;
                
        /// <summary>
        /// The constructor for the TOCBuilder class.
        /// </summary>
        /// <param name="maxSegmentLength">The maximum length of a file path segment.</param>
        /// <param name="rootCollectionName">The name of the top level file and directory in the markdown file tree.</param>
        internal TOCBuilder(int maxSegmentLength, string rootCollectionName)
        {
            MaxNameSegmentLength = maxSegmentLength;
            RootCollectionName = rootCollectionName;
        }

        /// <summary>
        /// Answers whether the longest file path in the TOC is short enough. MaxSegmentLength is shortened with each attempt.
        /// </summary>
        /// <param name="offset">The maximum length of host directory path to account for when calculating maximum path length.</param>
        /// <returns>True if MaxNameSegmentLength plus the length of the longest file path in the TOC is greater than 250, false otherwise.</returns>
        internal bool fixingPathLengthInTOC(int offset)
        {
            if (FileMap.Count == 0) return true;
            int longest = 0, segmentCount = 0;
            string worstPath = "";
            foreach (var item in FileMap.Values)
                if (item.Length > longest)
                {
                    longest = item.Length;
                    segmentCount = item.Split("\\".ToCharArray()).Length;
                    worstPath = item;
                }
            if (longest + offset < 250) return false;
            MaxNameSegmentLength -= (longest + offset - 250) / segmentCount + 2;
            return true;
        }


        /// <summary>
        /// Uses a TOC node from an HxT file to map source html file paths to the corresponding
        /// destination markdown file paths, adds the mapping to TOC_Map, and then runs recursively 
        /// through all descendant nodes.
        /// </summary>
        /// <param name="element">An XElement that represents the current TOC node.</param>
        /// <param name="currentDir">A string that represents the current directory path.</param>
        internal void buildToc(XElement element, string currentDir)
        {
            string outputFileName = "";

            // If the element points to a file, copy it in and update currentDir
            if (element.Attribute("Url") != null)
            {
                // Set paths.
                string sourceFileName = element.Attribute("Url").Value;
                string sourceFilePath = Program.HtmlFileDir + "\\" + sourceFileName;
                if (!IsRootSet && RootCollectionName != "")
                {
                    outputFileName = RootCollectionName;
                    IsRootSet = true;
                }
                else outputFileName = getOutputFileName(sourceFilePath);

                // If the file couldn't be found or read, skip to the next one.
                if (outputFileName == "") return;

                // Make any fileName adjustments that require knowing the path.
                if (outputFileName.EndsWith("onstructor") && Regex.IsMatch(currentDir, outputFileName.Replace("Constructor", "Class"), RegexOptions.IgnoreCase))
                    outputFileName = Regex.Replace(outputFileName, @".*Constructor$", "Constructor");


                string outputFilePath = currentDir + "\\" + outputFileName + ".md";

                // Add to TOC.
                if (!FileMap.ContainsKey(sourceFilePath)) FileMap.Add(sourceFilePath, outputFilePath);
                File.AppendAllText(Program.LogPath, string.Format("Mapped {0} to {1}.\r\n", sourceFilePath, outputFilePath));

                // Update currentDir.
                currentDir += "\\" + outputFileName;
            }

            // Recurse through the children.
            foreach (XElement e in element.Elements()) buildToc(e, currentDir);
        }

        
        internal void AddAssetIdToHxtx(XDocument xdoc, string fileName)
        {
            string assetId = xdoc.Descendants().First(
                xe => xe.Attribute("Name") != null 
                    && xe.Attribute("Name").Value == "AssetID")
                .Attribute("Value").Value;
            var currentNode = HxtxDoc.Descendants().First(
                node => node.Attribute("Url") != null
                && node.Attribute("Url").Value == Path.GetFileName(fileName));
            currentNode.SetAttributeValue("AssetID", assetId);               
        }

        /// <summary>
        /// Reads the TOCTitle and RLTitle attributes from the <head> of the source file and uses them to 
        /// generate a concise, readable, and unique filename for the resulting markdown.
        /// </summary>
        /// <param name="sourceFilePath">The full path to the source html file.</param>
        /// <returns>The formatted name of the destination markdown file, excluding path and file extension.</returns>
        string getOutputFileName(string sourceFilePath)
        {
            // Deal with missing or invalid file paths.
            while (!File.Exists(sourceFilePath))
            {
                if (!Program.Quietmode)
                {
                    Console.WriteLine("Could not read source file {0}. If the file exists and is misnamed, enter the correct path of the file in the {1} folder. "
                        + "Otherwise, hit ENTER to skip this file and its children", sourceFilePath, Program.HtmlFileDir);
                    sourceFilePath = Console.ReadLine();
                }
                if (sourceFilePath == "")
                {
                    File.AppendAllText(Program.LogPath, "2md failed to read html source file " + sourceFilePath + ".\r\n\tFile will skipped. To convert the file anyway, "
                        + "run 2md against the file and then examine the .hxt file in the " + Program.HtmlFileDir + " folder to find where it belongs in the directory structure.");
                    return "";
                }
            }

            // Set the output file name to a Title attribute defined in the file.
            string content = File.ReadAllText(sourceFilePath);

            string s;
            if (Regex.IsMatch(content, @"MSHelp:TOCTitle"))
                s = Regex.Replace(content, ".*?<MSHelp:TOCTitle.*?Title=\\\"(.+?)\\\".*", "$1", RegexOptions.Singleline); // Grab TOC title.
            else if (Regex.IsMatch(content, @"MSHelp:RLTitle"))
                s = Regex.Replace(content, ".*?<MSHelp:RLTitle.*?Title=\\\"(.+?)\\\".*", "$1", RegexOptions.Singleline);  // If no TOC title, grab RLTitle.
            else s = Path.GetFileNameWithoutExtension(sourceFilePath).Split("_".ToCharArray())[0]; // If no titles, grab everything before the first underscore of the file name.

            // Turn white space and separators to underscores.
            s = Regex.Replace(s, "[:, -]+", "_");

            // If there's a method signature, replace it with "(0)".
            s = Regex.Replace(s, @"\((.*?)\)", "0");

            // Remove method signatures and resolve duplicate file names.
            for (int i = 0; FileMap.ContainsValue(s); i++)
                if (Regex.IsMatch(s, @"\(\d+\)")) s = Regex.Replace(s, @"\((.*)\)", @"\(" + i + @"\)"); // replace method signature with (i).
                else if (Regex.IsMatch(s, @"~\d+\.md")) s = Regex.Replace(s, @"\((\d+)\)", @"\(" + i + @"\)"); // if filename ends in ~[number], increment.
                else s = Regex.Replace(s, @"(.*).md$", "$1~0.md"); // If not method sig and no "~", append "~0" to end of filename.

            // Remove member specification suffix.
            string[] suffixes = { "_Method", "_Property", "_Event", "_Field", "_Class", "_Structure", "_Interface", "_Enumeration", "_Function" };
            foreach (var suffix in suffixes)
                if (s.EndsWith(suffix, StringComparison.CurrentCultureIgnoreCase)) s = s.Substring(0, s.LastIndexOf("_"));

            // Standard replacements regardless of length.
            if (Regex.IsMatch(s, ".*_Methods$", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, ".*_Methods$", "Methods", RegexOptions.IgnoreCase);
            else if (Regex.IsMatch(s, ".*_Properties$", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, ".*_Properties$", "Properties", RegexOptions.IgnoreCase);
            else if (Regex.IsMatch(s, ".*_Events$", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, ".*_Events$", "Events", RegexOptions.IgnoreCase);
            else if (Regex.IsMatch(s, ".*_Members$", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, ".*_Members$", "Members", RegexOptions.IgnoreCase);
            else if (Regex.IsMatch(s, @"^Microsoft\..+", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, @"^Microsoft\.(.+)", "$1", RegexOptions.IgnoreCase);
            else if (Regex.IsMatch(s, @"^Windows\..+", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, @"^Windows\.(.+)", "$1", RegexOptions.IgnoreCase);
            else if (Regex.IsMatch(s, @"^WindowsPreview\..+", RegexOptions.IgnoreCase))
                s = Regex.Replace(s, @"^WindowsPreview\.(.+)", "$1", RegexOptions.IgnoreCase);
            //while (Regex.IsMatch(s, @"windows.*windows", RegexOptions.IgnoreCase))
            //    s = Regex.Replace(s, @"(.*)windows(.*)windows(.*)", @"$1Windows$2$3", RegexOptions.IgnoreCase);

            // Further reductions if still too long.
            while (s.Length > MaxNameSegmentLength)
                if (s.Contains("_")) s = s.Substring(0, s.LastIndexOf("_"));
                else s = s.Substring(0, MaxNameSegmentLength - 2);

            return s;
        }
    }
}
