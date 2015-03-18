using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Xml.Linq;
using System.Collections.Generic;
using HtmlAgilityPack;

namespace _2md
{
    class Program
    {
        const string PANDOC_PATH = "Pandoc.exe";
        const string HxCOMP_PATH = "HxComp.exe";
        static int DestinationPathLengthOffset = 75;
        static string SourceFilePath = "";

        /// <summary>
        /// The directory to recieve the contents of the target .hxs file.
        /// </summary>
        internal static string HtmlFileDir;

        /// <summary>
        /// The root directory of the markdown file tree.
        /// </summary>
        internal static string MdPath;
        static string ResourceFilePath;

        /// <summary>
        /// The root directory of the metadata file tree.
        /// </summary>
        internal static string MetadataRoot;
        static bool IsTestRun;


        /// <summary>
        /// True if all prompting should be suppressed.
        /// </summary>
        internal static bool Quietmode;

        /// <summary>
        /// The path to the log file.
        /// </summary>
        internal static string LogPath;

        /// <summary>
        /// A TOCBuilder object that manages TOC operations.
        /// </summary>
        internal static TOCBuilder TOC;
        static MetaHelper Meta;
        
        /// <summary>
        /// Processes command line arguments and user input as needed to determine the target 
        /// file. If HxS, decompiles and converts the resulting html files to markdown in a 
        /// directory structure based on the hierarchy defined in the HxT file.
        /// If HTML, converts the individual html file to markdown.
        /// </summary>
        /// <param name="args">Command line arguments. The first argument specifies the target file.
        /// -m, -r, and -? are optional arguments.</param>
        static void Main(string[] args)
        {
            // Process command line arguments.
            ProcessCommandLineArguments(args);

            // If it's pointed at an .HxS, decompile and recurse over the contents. Otherwise process single file.
            string inputType = Path.GetExtension(SourceFilePath).ToLower();
            if (inputType == ".hxs") decompile(SourceFilePath);
            else TOC.FileMap.Add(SourceFilePath, SourceFilePath.Replace(".htm", ".md"));
            Console.WriteLine("Converting {0} HTML files to markdown. (Each \".\" represents 100 files.)", TOC.FileMap.Count);
            int fileCount = 0;
            foreach (var item in TOC.FileMap)
            {
                if (!processFile(item.Key))
                    Console.WriteLine("Could not create file {0} from file {1}", item.Value, item.Key);
                if (fileCount % 100 == 0) Console.Write(".");
                fileCount++;
            }
            string hxtxPath = MdPath + "\\" + Path.GetFileNameWithoutExtension(SourceFilePath) + ".hxtx";
            Utils.TryWrite(hxtxPath, TOC.HxtxDoc.ToString());

            if (File.Exists(MdPath + "\\tmp.md")) File.Delete(MdPath + "\\tmp.md");
            Console.WriteLine("******All done.**********");
        }

        static void ProcessCommandLineArguments(string[] args)
        {
            string rootCollectionName = "";

            if (args.Length == 0 || Regex.IsMatch(SourceFilePath, @"[\-/]\?")) usage();
            SourceFilePath = args[0];
            HtmlFileDir = SourceFilePath + "_files";
            
            // Process options...
            bool overWrite = false;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-d":
                    case "/d":
                        i++;
                        if (args[i] == null || !int.TryParse(args[i], out DestinationPathLengthOffset))
                            Console.WriteLine("Invalid destination path length offset value. Running with the current path length of {0}", DestinationPathLengthOffset);
                        break;

                    case "-n":
                    case "/n":
                        i++;
                        if (args[i] != null) rootCollectionName = args[i];
                        break;
                    case "-?":
                    case "/?":
                        usage();
                        return;
                    case "-test":
                        IsTestRun = true;
                        break;
                    case "-q":
                        Quietmode = true;
                        break;
                    default:
                        break;
                }
            }

            while (!File.Exists(SourceFilePath))
            {
                if (Quietmode) Utils.Die("Source file not found at " + SourceFilePath);
                Console.WriteLine("File: {0} not found. Specify a file to convert:",
                    SourceFilePath);
                SourceFilePath = Console.ReadLine();
            }


            // Expand sourceFilePath to an absolute path.
            SourceFilePath = Path.GetFullPath(SourceFilePath);

            // Set other static paths.
            MdPath = Path.GetDirectoryName(SourceFilePath) + "\\md";
            MetadataRoot = Path.GetDirectoryName(SourceFilePath) + "\\meta";
            ResourceFilePath = MdPath + "\\resources";
            foreach (string dir in new string[] { MdPath, MetadataRoot, ResourceFilePath })
                if (!overWrite) overWrite = verifyDir(dir);
            LogPath = Path.GetDirectoryName(SourceFilePath) + "\\2md.log";

            // Set MaxDirNameLenght if specified.
            int x, maxNameSegmentLength = 50;
            if (args.Length > 1 && int.TryParse(args[1], out x)) maxNameSegmentLength = x;

            TOC = new TOCBuilder(maxNameSegmentLength, rootCollectionName);
            Meta = new MetaHelper(MetadataRoot);

        }

        static bool verifyDir(string dirPath)
        {
            if (!Directory.Exists(Directory.GetParent(dirPath).FullName))
            {
                Console.WriteLine("Couldn't find the parent directory of {0}. Please create it and press ENTER to resume, or else restart the program.", dirPath);
                return false;
            }
            if (!Directory.Exists(dirPath)) return true;
            if (!Quietmode)
            {
                if (Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories).Length > 0)
                    Console.WriteLine("The directory {0} is not empty.\r\n\tPress ENTER to overwrite, or press CTRL + C to exit the program and move the affected file to a safe location.", dirPath);
                Console.ReadLine(); 
            }
            return true;
        }

        #region HxS Processing
        /// <summary>
        /// Sets HtmlFileDir and LogPath variables, calls runHxComp to decompile the target HxS file, reads the 
        /// resulting HxT file as an XmlDocument, and then calls buildToc on the top level node to build TOC.FileMap.
        /// </summary>
        /// <param name="hxsFilePath"></param>
        static void decompile(string hxsFilePath)
        {
            // Create directories to hold the files.
            HtmlFileDir = hxsFilePath + "_files";
            // At some point, should maybe wrap in a try/catch block against UnauthorizedAccess exceptions.
            if (!Directory.Exists(HtmlFileDir)) Directory.CreateDirectory(HtmlFileDir);

            // Run HxComp.
            string error = "";
            if (! IsTestRun && Directory.Exists(HtmlFileDir)) error = runHxComp(hxsFilePath);
            if (error != "") Console.WriteLine("Error: {0}", error);

            // Make sure there's an HxT file and then load it into an XmlDocument.
            string[] hxtFilePaths = Directory.GetFiles(HtmlFileDir, "*.hxt", SearchOption.TopDirectoryOnly);
            if (hxtFilePaths.Length < 1)
            {
                Console.WriteLine("Error: {2} HxT files found in {0} after decompiling {1}. There must be an HxT file.", HtmlFileDir, hxsFilePath, hxtFilePaths.Length);
                return;
            }
            TOC.HxtxDoc = XDocument.Load(hxtFilePaths[0]);

            // If there is a RootCollectionName set, make sure the TOC has a root.
            if (TOC.RootCollectionName != "" && TOC.HxtxDoc.Element("HelpTOC").Elements().Count() > 1)
            {
                Console.WriteLine("You cannot set a root collection name in a rootless TOC. The TOC must have a single top level node to use this option.\r\nResetting to default name.");
                TOC.RootCollectionName = "";
            }


            // If there's an existing md directory, delete it.
            int attempts = 0;
            if (Directory.Exists(MdPath)) try { Directory.Delete(MdPath, true); }
                catch (Exception) { if (attempts > 2) throw; }

            // Make sure we won't hit any PathTooLong exceptions.
            int offset = Path.GetDirectoryName(SourceFilePath).Length;
            if (DestinationPathLengthOffset > offset) offset = DestinationPathLengthOffset;
            Console.WriteLine("Building TOC...");
            while (TOC.fixingPathLengthInTOC(offset))
            {
                // Build the TOC recursively from the top level element.
                TOC.FileMap = new Dictionary<string, string>();
                TOC.IsRootSet = false;
                TOC.buildToc(TOC.HxtxDoc.Element("HelpTOC"), MdPath);
            }

            // Write TOC data to internal .hxtx file.
            //string hxtxPath = MdPath + "\\" + Path.GetFileNameWithoutExtension(hxsFilePath) + ".hxtx"; 
            foreach (var htmlFileName in TOC.FileMap.Keys.ToList<string>())
            {
                var currentNode = TOC.HxtxDoc.Descendants("HelpTOCNode").First(xe =>
                    xe.Attribute("Url") != null
                    && xe.Attribute("Url").Value == Path.GetFileName(htmlFileName));
                currentNode.SetAttributeValue("MDPath", TOC.FileMap[htmlFileName]);
            }
            // Utils.TryWrite(hxtxPath, HxtDoc.ToString());
            Console.WriteLine("done.");

            if (!IsTestRun)
            {
                // Copy images, videos, and other non-html content into Assets folder.
                Console.WriteLine("Copying images and supporting files...");
                Directory.CreateDirectory(ResourceFilePath);
                foreach (string currentFilePath in Directory.GetFiles(HtmlFileDir))
                    if (!Regex.IsMatch(Path.GetExtension(currentFilePath), @"(htm|hx)"))
                        File.Copy(currentFilePath, ResourceFilePath + "\\" + Path.GetFileName(currentFilePath));
                Console.WriteLine("done.");
            }
            return;
        }


        /// <summary>
        /// Decompiles an HxS file into its component files and writes them to HtmlFileDir.
        /// </summary>
        /// <param name="hxsFilePath">The full absolute or relative path to the source HxS file.</param>
        /// <returns>Any errors returned by pandoc.exe, or there are no errors, an empty string.</returns>
        static string runHxComp(string hxsFilePath)
        {
            Console.WriteLine("Decompiling {0} with HxComp.exe...", Path.GetFileName(hxsFilePath));

            var hxc = new Process();
            hxc.StartInfo.FileName = HxCOMP_PATH;
            hxc.StartInfo.Arguments = string.Format("-u {0} -d {1}", hxsFilePath, HtmlFileDir);
            hxc.StartInfo.UseShellExecute = false;
            hxc.StartInfo.RedirectStandardOutput = true;
            hxc.StartInfo.RedirectStandardError = true;
            string err = string.Empty;
            try
            {
                hxc.Start();
                err = hxc.StandardError.ReadToEnd();
                hxc.WaitForExit();
                Console.WriteLine("...Done.");
            }
            catch (Exception e)
            {
                throw new Exception(
                    "Could not run HxComp on " + hxsFilePath + ". Is HxComp.exe in your path?\r\n" + err, e.InnerException);
            }
            return err;
        }
        #endregion

        #region Single file processing
        /// <summary>
        /// Removes extraneous material from the source html file, converts it to markdown by using 
        /// pandoc.exe, and then cleans up the result.
        /// </summary>
        /// <param name="currentFilePath">The full absolute or relative path to the source html file.</param>
        /// <returns>true if the destination file was successfully created in specified path; false otherwise.</returns>
        static bool processFile(string currentFilePath)
        {
            string mdPath = TOC.FileMap[currentFilePath];
            bool success = true;

            // Remove generated content.
            preClean(currentFilePath);

            // Get metadata from html and write it to a JSON file.
            XDocument xdoc = Meta.GetMetadata(currentFilePath);
            TOC.AddAssetIdToHxtx(xdoc, currentFilePath);
            if (!Meta.ProcessMetadata(xdoc, mdPath))
            {
                Console.WriteLine("Couldn't create metadata file for {0}.", mdPath);
                success = false;
            }
            
            // Get the BODY content and convert to markdown.
            string body = runPandoc(currentFilePath + ".clean");

            // Recover links stripped out by Pandoc.
            body = postClean(body);

            // Write to a file.
            if (!Utils.TryWrite(mdPath, body)) success = false;
            
            // Delete the intermediate file.
            File.Delete(currentFilePath + ".clean");

            return success;
        }

        static string postClean(string body)
        {
            // Restore stripped links.
            int index;
            while (body.Contains("@@@link@@@"))
                foreach (var link in StoredLinks)
                {
                    index = body.LastIndexOf("@@@link@@@");
                    body = body.Substring(0, index)
                        + link
                        + body.Substring(index + 10);
                }

            // Add double spaces to ends of lines so they are interpreted correctly.
            // Technically, this is a hack, because the extra spaces should only be at the ends lines that require them,
            // but this seems to work OK for now.
            StringBuilder sb = new StringBuilder();
            bool isFirst = true;
            string[] lines = body.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length - 1; i++)
            {
                // Put extra line before each header.
                if (Regex.IsMatch(lines[i + 1], @"==="))
                {
                    if (!isFirst) lines[i] = "\r\n" + lines[i];
                    isFirst = false;
                }
                sb.AppendLine((lines[i].Length > 0 && !lines[i].EndsWith("|") && !lines[i].EndsWith(">")) ? lines[i] + "  " : lines[i]);
            }

            return sb.ToString();
        }

        static List<string> StoredLinks;
        /// <summary>
        /// Loads an MTPS-formatted html file, extracts the div that contains authored content, retargets
        /// internal links, and removes extranneous content prior to conversion by Pandoc, and then writes
        /// the resulting string to [filename].clean.
        /// </summary>
        /// <param name="sourceFilePath">The full path to the source html file.</param>
        static void preClean(string sourceFilePath)
        {
            // Load text content and remove whitespace before closing tags to reduce conversion errors.
            string textContent = File.ReadAllText(sourceFilePath);
            textContent = Regex.Replace(textContent, @"\s*<\s*/", "</");
            
            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(textContent);
            string content = "";

            ///////////// DOM manipulations here /////////////////////////////////////////////////////////
            // These must happen first to keep text fixes from breaking the OM, or else reload document.//

            // Find the main section, which includes all the authored content, and while we're there, remove the footer.
            var divs = doc.DocumentNode.Descendants("div");
            HtmlNode main = null;
            HtmlNode footer = null;
            foreach (HtmlNode div in divs)
                if (div.Id == "mainSection" && main == null) main = div;
                else if (div.GetAttributeValue("class", "none") == "footer") footer = div;            
            if (main == null)
                if (doc.DocumentNode.Descendants("body") != null)
                    main = doc.DocumentNode.Descendants("body").First<HtmlNode>();
                else main = doc.DocumentNode;
            if (footer != null) main.RemoveChild(footer, false);

            // Make sure each <pre> element has a <p> before it so it is recognized as a code block after conversion.
            HtmlNode[] pres = main.Descendants("pre").ToArray<HtmlNode>();
            for (int i = pres.Length - 1; i > -1; i--)
            {
                if (pres[i].PreviousSibling == null || pres[i].PreviousSibling.Name != "p")
                    pres[i].ParentNode.InsertBefore(doc.CreateElement("p"), pres[i]);
            }
                            
            // Update link paths and remove non-linking anchors.
            // Use reversed For loop to chop from the end of the array so we don't mess up the array order.
            Uri caller = new Uri(TOC.FileMap[sourceFilePath], UriKind.Absolute);            // caller is the full path of the current md file as an absolute URI
            StoredLinks = new List<string>();                                           // This will hold copies of links in code blocks so we can put them back in after Pandoc strips them out.
                      
            HtmlNode[] anchors = main.Descendants("a").ToArray<HtmlNode>();             // anchors is the list of "a" elements in the html doc
            for (int i = anchors.Length - 1; i > -1; i--)
            {
                //if (!anchors[i].Attributes.Contains("href")) anchors[i].Remove();     // Get rid of non-linking anchors. 
                if (anchors[i].Attributes.Contains("href"))                             // Skip non-linking anchors. 
                {
                    string originalLink = anchors[i].Attributes["href"].Value;
                    if (originalLink.Contains("http://") || originalLink.StartsWith("www")) continue;   // Leave external paths untouched
                    string targetFilePath = Path.GetDirectoryName(sourceFilePath) + "\\" 
                        + ((originalLink.Contains("\\")) ? originalLink.Substring(originalLink.LastIndexOf("\\")) : originalLink);
                    if (TOC.FileMap.ContainsKey(targetFilePath))
                    {
                        Uri target;     // Target is the absolute Uri of the destination md file.
                        if (!Uri.TryCreate(TOC.FileMap[targetFilePath], UriKind.Absolute, out target)) anchors[i].Remove();   // Try to get the new relative path to the target.
                        else anchors[i].Attributes["href"].Value = caller.MakeRelativeUri(target).ToString();   // Update the html.
                    }
                }

                // Set aside links within code blocks so we can put them back when Pandoc removes them.
                foreach (HtmlNode ancestor in anchors[i].Ancestors("pre"))
                {
                    StoredLinks.Add(anchors[i].OuterHtml);
                    anchors[i].InnerHtml = "@@@link@@@";
                    break;
                }
            }

            // Update image paths and remove toggles, same as above.
            HtmlNode[] images = main.Descendants("img").ToArray<HtmlNode>();
            for (int i = images.Length - 1 ; i > -1; i--)
            {
                string originalLink = images[i].Attributes["src"].Value;
                if (originalLink.Contains("http://") || originalLink.StartsWith("www.")) continue;          // Skip external paths.
                if (images[i].GetAttributeValue("class", "null").ToLower().Contains("toggle")
                    || images[i].GetAttributeValue("syle", "null").ToLower().Contains("display:none")
                    ) images[i].Remove();           // Get rid of filler images.
                else
                {
                    string imagePath = ResourceFilePath + "\\" + Path.GetFileName(originalLink);
                    Uri target;
                    if (!Uri.TryCreate(imagePath, UriKind.Absolute, out target)) images[i].Remove();
                    else images[i].Attributes["src"].Value = caller.MakeRelativeUri(target).ToString();
                }
            }

            // May want to also remove all tags w/ display:none...

            //Replace styled spans with generic elements.
            HtmlNode[] spans = doc.DocumentNode.Descendants("span").ToArray<HtmlNode>();
            string title = "";
            foreach (var span  in spans)
                if (span.Id == "nsrTitle")
                {
                    span.Name = "h1";
                    title = span.OuterHtml;
                    break;
                }

            //Remove duplicate spans from tops of code blocks.
            for (int i = spans.Length - 1; i >= 0; i--)
            {
                if (spans[i].GetAttributeValue("class", "") == "copyCode")
                    if (spans[i].NextSibling != null || spans[i].ParentNode.Name != "th")
                        spans[i].Remove();
            }

            // Line up the parameters section if there is one.
            var sectionLabel = main.SelectSingleNode(".//h4[text()='Parameters' or text()='Return value']");
            if (sectionLabel != null)
            {
                var section = sectionLabel.NextSibling;

                var v = section.OuterHtml; // remove this after testing

                var spansWithCodeLangAttr = section.SelectNodes(".//span[@codelanguage]");
                if (spansWithCodeLangAttr != null)       // If there are languages specified...
                {
                    //List<int> filledTargets = new List<int>();  // List of Language nodes we have already appended Type info to.
                    var sorted = spansWithCodeLangAttr.OrderBy(node => node.GetAttributeValue("codeLanguage", "")).ToArray();
                    for (int i = 0; i < sorted.Length; i += 2)
                    {
                        sorted[i].InnerHtml += sorted[i + 1].InnerHtml;
                        sorted[i + 1].Remove();
                    }
                }
            }

            // Switch to text.
            content = "<body>" + title + main.InnerHtml + "</body>";

            /////////////// RegEx-based fixes here ////////////////////////////

            // Remove extra line breaks.
            content = Regex.Replace(content, "\r\n(\r\n)+", "\r\n\r\n", RegexOptions.Singleline);

            // Write it to a new file.
            Utils.TryWrite(sourceFilePath + ".clean", content);
        }


        /// <summary>
        /// Runs the Pandoc program to generate markdown from the original source file.
        /// </summary>
        /// <param name="fileName">The path to the file to read.</param>
        /// <returns>The markdown (or error message) returned by pandoc, as a string.</returns>
        static string runPandoc(string fileName)
        {
            var pd = new Process();
            pd.StartInfo.FileName = PANDOC_PATH;
            pd.StartInfo.Arguments = string.Format("-f html -t markdown_github {0}", fileName);
            pd.StartInfo.UseShellExecute = false;
            pd.StartInfo.RedirectStandardOutput = true;
            pd.StartInfo.RedirectStandardError = true;
            pd.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            try
            {
                pd.Start();
                string content = pd.StandardOutput.ReadToEnd();
                string err = pd.StandardError.ReadToEnd();
                pd.WaitForExit();
                pd.Close();
                if (err != "")
                {
                    Console.WriteLine("Pandoc encountered one or more errors:\r\n{0}\r\nPress ENTER to continue or CTRL+C to quit.", err);
                    Environment.Exit(3);
                }
                return content;                
            }
            catch (Exception e)
            {
                throw new Exception(
                    "Could not run Pandoc on " + fileName + ". Is pandoc.exe in your path?", e.InnerException);
            }
        }
        
        #endregion

        #region Utilities
        static void usage()
        {
            Console.WriteLine("*** 2md.exe [inputfile] [options] ***");
            Console.Write("Converts HTML and HxS files to markdown.");
            Console.WriteLine("inputfile: The absolute or relative path to the HxS or html file to convert.");
            Console.WriteLine(":::OPTIONS:::");
            Console.WriteLine("-n [collection name] Overrides the default name for the top level output file and directory. Applies to HxS files only.");
            Console.WriteLine("-d [destination path length] Specifies the path length of the final destination if longer than that of the directory that contains the source file. Used to prevent PathTooLong exceptions when copying the markdown content to a new location.");
            Console.WriteLine("-q Quiet mode. Skips all user prompts except when required parameters are missing or invalid.");
            Console.WriteLine("-? Displays this help text and then exits.\r\n***********************");
            Console.Write("2md writes markdown content to a directory named \"md\" in the same directory that contains the source HxS or HTML file. "
            + "For Hxs files, the file naming and directory structure within the md folder are derived from the HxT file after decompiling the HxS. "
            + "The \"meta\" folder, at the same level as the md folder, contains any MSHelp metadata from the source files in JSON format, with the same "
            + "file naming and directory structure as the markdown files they refer to. An .hxtx file, in the \\md directory, sets the TOC order and shows "
            + "the mapping between html filenames and md file paths.\r\nIf you have multiple HxS files to convert, you should put them "
            + "each in a separate folder to prevent content from getting overwritten\r\n");
            Environment.Exit(3);
        }
        #endregion
    }
}
