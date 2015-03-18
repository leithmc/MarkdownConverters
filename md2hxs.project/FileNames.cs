using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text.RegularExpressions;
using System.IO;

namespace md2hxs
{
    /// <summary>
    /// A helper class that reads, creates, updates, and stores TOC information.
    /// </summary>
    internal class TOCBuilder
    {
        /// <summary>
        /// A dictionary that maps markdown file paths to html file names.
        /// </summary>
        internal Dictionary<string, string> FileMap;
        /// <summary>
        /// An internal representation of the .hxt file for the docset.
        /// </summary>
        internal XDocument HxtDoc;
        /// <summary>
        /// An internal representation of the .hxtx file for the docset, which extends the .hxt file to include markdown filepath and asset ID information.
        /// </summary>
        internal XDocument HxtxDoc;
        /// <summary>
        /// The path to the .hxtx file.
        /// </summary>
        internal string HxtxPath;
        /// <summary>
        /// True if the .hxtx file has been loaded into memory; false otherwise.
        /// </summary>
        internal bool HxtxLoaded = false;
        /// <summary>
        /// True if all updates to the HxtxDoc are complete; false otherwise.
        /// </summary>
        internal bool HxtxUpdated = false;

        /// <summary>
        /// The constructor for the TOCBuilder object.
        /// </summary>
        /// <param name="hxtxPath">The full path to the .hxtx file.</param>
        internal TOCBuilder(string hxtxPath)
        {
            FileMap = new Dictionary<string,string>();
            HxtxPath = hxtxPath;
        }

        /// <summary>
        /// Populates the FileMap dictionary with the paths to the markdown files by crawling the directory structure, and assigns them corresponding html file names.
        /// </summary>
        internal void buildFileMapFromDirectoryStructure()
        {
            string[] mdFilePaths = Directory.GetFiles(Program.MdRoot, "*.md", SearchOption.AllDirectories);
            string[] htmlFileNames = new string[mdFilePaths.Length];

            for (int i = 0; i < htmlFileNames.Length; i++) htmlFileNames[i] = getHtmlFileName(mdFilePaths[i]);

            htmlFileNames = fixDuplicates(mdFilePaths, htmlFileNames);

            for (int i = 0; i < mdFilePaths.Length; i++) FileMap.Add(mdFilePaths[i], htmlFileNames[i]);
        }

        void buildFileMapFromHxtx()
        {
            foreach (var x in HxtxDoc.Descendants("HelpTOCNode"))
                FileMap.Add(x.Attribute("MDPath").Value, x.Attribute("Url").Value);
        }

        string getHtmlFileName(string mdFilePath, string rlTitle)
        {
            // Remove any spaces and illegal characters from file name.
            return Regex.Replace(rlTitle, @"[:, -/\\]+", "_").Replace("+", "Plus").Replace("#", "Sharp");
        }

        string getHtmlFileName(string mdFilePath)
        {
            //var f = new FileInfo(mdFilePath);
            string rlTitle = Program.Meta.GetRLTitle(mdFilePath);
            return getHtmlFileName(mdFilePath, rlTitle);
        }

        /// <summary>
        /// Checks a list of filenames for duplicates and adjusts them until all are unique.
        /// </summary>
        /// <param name="mdFilePaths">The full paths of the source markdown files.</param>
        /// <param name="htmlFileNames">The filenames of the destination html files to be fixed.</param>
        /// <returns>The modified list of file names.</returns>
        string[] fixDuplicates(string[] mdFilePaths, string[] htmlFileNames)
        {
            while (true)
            {
                // Build a list of duplicate names.
                string[] sorted = htmlFileNames.Clone() as string[];    // file names as a sorted array
                Array.Sort(sorted);
                List<string> matches = new List<string>();              // duplicates get copied here
                for (int i = 0; i < htmlFileNames.Length; i++)
                {
                    if ((i < htmlFileNames.Length - 1 && sorted[i] == sorted[i + 1])    // If the current name matches the next in the array,
                        || (matches.Count > 0 && sorted[i] == matches.Last<string>()))  // or it matches the last item added to the list of matches,
                        matches.Add(sorted[i]);                                         // add it to the list.
                }
                if (matches.Count == 0) return htmlFileNames;                           // If there are no more matches, we're done.

                foreach (var name in matches)
                {
                    int index = Array.IndexOf(htmlFileNames, name);
                    htmlFileNames[index] = fixDuplicate(name, mdFilePaths[index], htmlFileNames);
                }
            }
        }

        /// <summary>
        /// Checks a single file name for duplicates in the doc set.
        /// </summary>
        /// <param name="fileName">The filename to check.</param>
        /// <returns>The modified file name.</returns>
        string fixDuplicate(string currentName, string mdPath, string[] htmlFileNames)
        {
            // If there's a match, try prepending path segments.
            while (htmlFileNames.Contains(currentName)
                && !Regex.IsMatch(currentName, @"~\d+$"))
                currentName = prependPathSegment(currentName, mdPath);

            // If there's still a match, try appending ~[number]
            while (htmlFileNames.Contains(currentName))
            {
                int i = int.Parse(currentName.Substring(currentName.LastIndexOf("~") + 1)) + 1;
                currentName = Regex.Replace(currentName, @"~\d+$", "~" + i);
            }

            return currentName;
        }

        XDocument CreateNew()
        {
            XDocument doc = new XDocument(
                new XDocumentType("HelpTOC", null, "ms-help://hx/resources/HelpTOC.DTD", null),
                    new XElement("HelpTOC",
                        new XAttribute("DTDVersion", "1.0")));
            doc.Declaration = new XDeclaration("1.0", "utf-8", "no");
            return doc;
        }

        /// <summary>
        /// Reads the contents of the .hxtx file, if present, into HxtxDoc.
        /// </summary>
        /// <returns>True if the .hxtx file was found; false otherwise.</returns>
        internal bool ReadHxtx()
        {
            if (!File.Exists(HxtxPath)) return false;
            try { HxtxDoc = XDocument.Load(HxtxPath); }
            catch (Exception ex)
            {
                Console.WriteLine("Hxtx file found at {0} but failed to read XML content from it. {1}\r\nYou can now...", HxtxPath, ex.Message);
                Console.WriteLine("\tA. Fix the file in question to preserve existing TOC order before running MD2HXS again, -or-");
                Console.WriteLine("\tB. Remove the file and run again, allowing the TOC to revert to alphabetical order within each tree level.");
                Environment.Exit(ex.HResult);
            }
            return true;
        }

        bool WriteHxt()
        {
            Console.WriteLine("\r\nWriting HxT file...");
            string hxtPath = string.Format("{0}\\{1}.hxt", Program.OutputDirectoryPath, Path.GetFileNameWithoutExtension(Program.OutputHxsFilePath));
            HxtDoc = XDocument.Parse(HxtxDoc.ToString());
            HxtDoc.Descendants().Attributes("MDPath").Remove();
            HxtDoc.Descendants().Attributes("AssetID").Remove();
            return Utils.tryWrite(hxtPath, HxtDoc.ToString());
        }

        void WriteHxts()
        {
            if (File.Exists(HxtxPath))
                if (!HxtxUpdated) return;
                else
                {
                    int i = 0;
                    string bkpPath = HxtxPath + ".bkp.";
                    while (File.Exists(bkpPath + i)) i++;
                    File.Move(HxtxPath, bkpPath + i);
                }
            File.WriteAllText(HxtxPath, HxtxDoc.ToString());
        }

        /// <summary>
        /// Compares the content of HxtxDoc to the markdown file structure. If any files are not included in HxtxDoc, their information is added. If there is no HxtxDoc, it is created at this time. When finished, HxtDoc and HxtxDoc are written to file.
        /// </summary>
        /// <param name="hasTOC">True if there is an existing .hxtx file; false otherwise.</param>
        internal void FillInMissingFileEntries(bool hasTOC)
        {
            if (!hasTOC) HxtxDoc = CreateNew();
            string[] mdFilePaths = Directory.GetFiles(Program.MdRoot, "*.md", SearchOption.AllDirectories);
            foreach (var filePath in mdFilePaths)
                if (!hasTOC || !hasEntryInHxtx(filePath)) addEntryToHxts(filePath);
            buildFileMapFromDirectoryStructure();
            WriteHxts();
            WriteHxt();
        }

        bool hasEntryInHxtx(string filePath)
        {
            var node = HxtxDoc.Descendants().FirstOrDefault(item => item.Attribute("MDPath") != null 
                && item.Attribute("MDPath").Value.ToLower() == filePath.ToLower());
            return node != null;
        }

        void addEntryToHxts(string mdFilePath, string htmlFileName, string title, string assetID)
        {
            string parentDir = Path.GetDirectoryName(mdFilePath);
            var parentNode = (parentDir == Program.MdRoot) 
                ? HxtxDoc.Element("HelpTOC")
                : HxtxDoc.XPathSelectElement(string.Format(".//HelpTOCNode[@MDPath='{0}.md']", parentDir));
            if (parentNode == null) Utils.Die("The .hxtx file contains an invalid MDPath attribute. Has "
                + mdFilePath + " been deleted, renamed, or moved?");
            parentNode.Add(new XElement("HelpTOCNode",
                new XAttribute("Url", htmlFileName + ".htm"),
                new XAttribute("Title", title),
                new XAttribute("MDPath", mdFilePath),
                new XAttribute("AssetID", assetID)));
            if (HxtxLoaded) Console.WriteLine("An entry for new file {0} ({1} in the markdown) has been added to the TOC.", htmlFileName, mdFilePath);
            HxtxUpdated = true;
        }

        void addEntryToHxts(string mdFilePath)
        {
            // Get the relevant metadata values.
            XElement x = Program.Meta.GetXml(mdFilePath);
            string title = x.Element(Program.Meta.NS + "RLTitle").Attribute("Title").Value.Trim("#".ToCharArray());
            var assetNode = from el in x.Descendants(Program.Meta.NS + "Attr") where (string)el.Attribute("Name") == "AssetID" select el;
            string assetId = assetNode.First().Attribute("Value").Value;
            
            // If it's in the FileMap, use it to get the html file name. If not, generate a name.
            string htmlFileName;
            if (FileMap.ContainsKey(mdFilePath))
                htmlFileName = FileMap[mdFilePath];
            else
            {
                htmlFileName = getHtmlFileName(mdFilePath, title);
                if (HxtxDoc.XPathSelectElement(".//HelpTOCNode[@Url='" + htmlFileName + "']") != null)
                {
                    // Uh-oh, now we have to get the list so we can check for dupes.
                    if (FileMap.Count == 0) buildFileMapFromHxtx();
                    htmlFileName = fixDuplicate(htmlFileName, mdFilePath, FileMap.Values.ToArray());
                    FileMap.Add(mdFilePath, htmlFileName);
                }
            }

            addEntryToHxts(mdFilePath, htmlFileName, title, assetId);
        }

        /// <summary>
        /// Checks HxtxDoc for any duplicate AssetID attributes. If found, writes to console and exits the program.
        /// </summary>
        internal void CheckForDuplicateGuids()
        {
            Dictionary<string, string> guidMap = new Dictionary<string,string>();
            foreach (var node in HxtxDoc.Descendants("HelpTOCNode"))
                if (guidMap.ContainsKey(node.Attribute("AssetID").Value))
                {
                    string file1 = node.Attribute("MDPath").Value;
                    string file2 = guidMap[node.Attribute("AssetID").Value];
                    string message = string.Format("ERROR: Duplicate AssetID found for {0} and {1}.\r\n"
                        + "Resolve this conflict, then re-run the program.\r\n"
                        + "If a file has been published with that AssetID, it has precedence. Otherwise, precedence goes to whichever document is older.");
                    Utils.Die(message);
                }
                else guidMap.Add(node.Attribute("AssetID").Value, node.Attribute("MDPath").Value);
        }

                ///*******************Utility functions*****************//


        private string prependPathSegment(string currentName, string mdPath)
        {
            var d = new FileInfo(mdPath).Directory;
            if (currentName.StartsWith(d.Name)                         // If the parent directory is something like "Enumerations" skip up a level.
            || currentName.EndsWith(d.Name)
            || currentName.StartsWith(d.Parent.Name)
            || currentName.EndsWith(singularOf(d.Name)))
                d = d.Parent;

            if (d.FullName != Program.MdRoot
                    && d.Parent.FullName != Program.MdRoot
                    && currentName.Split(".".ToCharArray()).Length < 3)   // Stop if you hit the root dir or the name gets too many segments, otherwise prepend parent folder name. 
                currentName = d.Name + "." + currentName;
            else currentName += "~0";

            return currentName;
        }

        private string singularOf(string s) { return (s.EndsWith("ies")) ? s.Substring(0, s.Length - 3) + "y" : s.Substring(0, s.Length - 1); }

    }
}
