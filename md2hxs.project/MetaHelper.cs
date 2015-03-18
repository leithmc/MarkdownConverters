using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace md2hxs
{
    /// <summary>
    /// A helper class that handles metadata operations.
    /// </summary>
    internal class MetaHelper
    {
        /// <summary>
        /// The full path to the directory that hosts the top level metadata file.
        /// </summary>
        internal string MetaRoot;
        /// <summary>
        /// The full path to the global metadata file.
        /// </summary>
        internal string GlobalJson;
        /// <summary>
        /// True if the user should be prompted when a piece of expected metadata is missing.
        /// </summary>
        internal bool PromptOnMissingMetadata;
        /// <summary>
        /// The MSHelp XML namespace.
        /// </summary>
        internal XNamespace NS = "http://msdn.microsoft.com/mshelp";            
        Dictionary<string, string> GlobalAttrs = new Dictionary<string, string> ();
        string[] DefaultGlobalAttrNames =  {"Locale", "DocSet", "ProjType", "Technology", "Product", "productversion", "CommunityContent"};
        bool HasGlobals;

        /// <summary>
        /// The constructor for the MetaHelper class.
        /// </summary>
        /// <param name="rootDir">The full path to the directory that will host the top level metadata file.</param>
        /// <param name="promptOnMissing">True if the user should be prompted when a piece of expected metadata is missing; false otherwise.</param>
        internal MetaHelper(string rootDir, bool promptOnMissing)
        {
            MetaRoot = rootDir;
            GlobalJson = rootDir + "\\global.json";
            PromptOnMissingMetadata = promptOnMissing;
            HasGlobals = tryReadGlobalsFile();
        }
        
        int attempts = 0;
        private bool tryReadGlobalsFile()
        {
            if (!File.Exists(GlobalJson)) return false;
            
            try
            {
                string content = File.ReadAllText(GlobalJson);
                JObject j = JObject.Parse(content);
                foreach (var prop in j.Properties())
                    GlobalAttrs.Add(prop.Name, (string)prop.Value);
                if (GlobalAttrs.Count == 0) return false;
            }
            catch (Exception ex)
            {
                bool success = false;
                attempts ++;
                if (attempts < 3) success = tryReadGlobalsFile();
                if (attempts == 3 && !success)
                    Console.WriteLine("Warning: Couldn't read global metadata from {0}.", ex.Message);
                return success;
            }

            HasGlobals = true;
            return true;
        }

        /// <summary>
        /// Attempts to load metadata from the .json file that corresponds to filePath. If not found, attempts to load from comments in the markdown. If still not found, generates metadata frommarkdown content, and if needed, prompts user for global attributes.
        /// </summary>
        /// <param name="filePath">The full path to the file to find metadata for.</param>
        /// <param name="assetId">Optional. The asset ID of the file to find metadata for.</param>
        /// <returns>An XElement object that contains the metadata for the specified file.</returns>
        internal XElement GetXml(string filePath, string assetId = null)
        {
            string jsonFilePath = mapToJson(filePath);
            XElement MsHelp;

            // Try to load XML content from JSON file.
            bool success = tryLoadXmlFromJSON(jsonFilePath, out MsHelp);
            if (!success || MsHelp.Element(NS + "RLTitle") == null)
            {
                // If failed...
                if (PromptOnMissingMetadata)
                    Console.WriteLine("No valid metadata file found at {0}. Checking for internal metadata in the markdown...", jsonFilePath);
                // Try to find it in the MD file.
                if (tryBuildMsHelpFromInternal(filePath, out MsHelp)) Console.WriteLine("...Found.");
                else
                {
                    // If failed, determine user intent.
                    if (PromptOnMissingMetadata)
                    {
                        Console.WriteLine("No valid metadata available for file {0}.", filePath);
                        if (Program.isFirstMissingMetadata)
                        {
                            Console.WriteLine("Metadata is required to host this content on MSDN, but if the file already exists on MSDN, generating new metadata may break navigation."
                            + "\r\nPlease make sure there is no existing metadata, such as a mis-named or poorly formed JSON file, before generating new.");
                            Program.isFirstMissingMetadata = false;
                        }
                        string choice = "";
                        string[] options = { "y", "n", "all" };
                        while (!options.Contains(choice))
                        {
                            Console.WriteLine("Generate the metadata? (y | n | all)");
                            choice = Console.ReadLine();
                        }
                        if (choice == "n") Utils.Die("Conversion cancelled.");
                        PromptOnMissingMetadata = (choice == "y");
                    }
                    // Then generate the metadata.
                    MsHelp = generateMetadata(filePath, assetId);
                }

                // There was no local JSON file, so write one.
                if (!Directory.Exists(Path.GetDirectoryName(jsonFilePath))) Directory.CreateDirectory(Path.GetDirectoryName(jsonFilePath));
                File.WriteAllText(jsonFilePath, JsonConvert.SerializeXNode(MsHelp, Newtonsoft.Json.Formatting.Indented));
            }

            // Add global metadata values.
            foreach (var attr in GlobalAttrs)
                MsHelp.Add(new XElement("Attr", new XAttribute("Name", attr.Key), new XAttribute("Value", attr.Value)));

            // Update the names to include the MSHelp: prefix if they don't already.
            applyMsHelpPrefix(MsHelp);
            
            return MsHelp;
        }

        private XElement applyMsHelpPrefix(XElement MsHelp)
        {
            if (!MsHelp.HasAttributes)
            {
                var x = new XAttribute(XNamespace.Xmlns + "MSHelp", NS);
                MsHelp.Add(x);
            }
            foreach (XElement node in MsHelp.Descendants())
                node.Name = NS + node.Name.LocalName;
            return MsHelp;
        }

        private string mapToJson(string filePath)
        {
            string s = filePath.Substring(Program.MdRoot.Length);
            return MetaRoot + s.Substring(0, s.LastIndexOf(".")) + ".json";
        }

        /// <summary>
        /// Attempts to retrieve the RLTitle attribute for the specified file.
        /// </summary>
        /// <param name="mdFilePath">The full path to the file to retrieve the RLTitle of.</param>
        /// <returns>The value of the RLTitle attribute, or if not found, an empty string.</returns>
        internal string GetRLTitle(string mdFilePath)
        {
            // Try to get it from the JSON.
            string jsonFilePath = mapToJson(mdFilePath);
            JObject j;
            try 
            { 
                j = JObject.Parse(File.ReadAllText(jsonFilePath)).First.First as JObject;
                var prop = j.Property("RLTitle").First.First as JProperty;
                return prop.Value.ToString().Trim("#".ToCharArray());
            }
            catch (Exception) { }

            // That failed, so try to get it from the metadata block in the markdown.
            string[] lines = File.ReadAllLines(mdFilePath);
            foreach (var line in lines)
                if (line.Contains("RLTitle : "))    // Get the file name from the RLTitle attribute.
                {
                    string s = Regex.Replace(line, @".*RLTitle : (.*)", "$1");
                    if (s.Trim() != "") return s.Trim("#".ToCharArray());
                }

            // Uh-oh. No RLTitle to be found. Have to build the metadata before we continue.
            var x = GetXml(mdFilePath);
            var node = x.Element(NS + "RLTitle");
            if (node != null && node.Attribute("Title") != null) return node.Attribute("Title").Value.Trim("#".ToCharArray());

            // That didn't work either.
            return "";
        }

        private XElement generateMetadata(string mdPath, string assetID = null)
        {
            var MsHelp = new XElement("xml");            

            if (!HasGlobals) generateGlobalsFromConsole();
            
            string[] lines = File.ReadAllLines(mdPath);

            // Get the document title.
            string rlTitle = "";
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith("#")
                    || (lines.Length > i + 1 &&
                        (lines[i + 1].StartsWith("==") || lines[i].StartsWith("--"))))
                {
                    rlTitle = lines[i].Trim().Trim("#".ToCharArray());
                    break;
                }
            MsHelp.Add(new XElement("RLTitle", new XAttribute("Title", rlTitle)));
            
            // Generate TOCTitle if there is an obvious need for one.
            string TOCTitle = "";
            string[] members = { "property", "method", "event", "field", "interface", "enumeration", "structure", "function" };
            string[] groupers = { "members", "properties", "methods", "events", "fields", "interfaces", "enumerations", "structures", "functions" };
            string lastWord = (rlTitle.Contains(" ")) ? rlTitle.Substring(rlTitle.LastIndexOf(" ")).Trim() : rlTitle.Trim(); ;
            if (members.Contains(lastWord.ToLower()))
            {
                TOCTitle = rlTitle.Substring(0, rlTitle.LastIndexOf(" "));
                if (TOCTitle.IndexOf(".") < TOCTitle.IndexOf("("))
                    TOCTitle = TOCTitle.Substring(TOCTitle.IndexOf(".") + 1);
            }
            else if (groupers.Contains(lastWord.ToLower())) TOCTitle = lastWord;
            if (TOCTitle != "")
                MsHelp.AddFirst(new XElement(NS + "TOCTitle", new XAttribute("Title", TOCTitle)));
            
            // Create an asset ID.
            if (assetID == null) assetID = Guid.NewGuid().ToString();
            MsHelp.Add(new XElement("Keyword", new XAttribute("Index", "A"), new XAttribute("Term", assetID)));
            MsHelp.Add(new XElement("Attr", new XAttribute("Name", "AssetID"), new XAttribute("Value", assetID)));

            // Determine topic type.
            string topicType = "";
            string content = string.Join("\r\n", lines);
            if (Regex.IsMatch(content, @"(\n\#*Syntax$)|(<span ^>+>\s*Syntax\s*</span>)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                topicType = "kbSyntax";
            else topicType = "kbOrient";
            MsHelp.Add(new XElement("Attr", new XAttribute("Name", "TopicType"), new XAttribute("Value", topicType)));

            return applyMsHelpPrefix(MsHelp);
        }

        private void generateGlobalsFromConsole()
        {
            if (Program.QuietMode) Utils.Die(
                string.Format("***Global metadata not found at {0}.***\r\n Re-run this program with quiet mode turned off to generate metadata, or create the file manually."
                , Program.Meta.GlobalJson));

            // Inform the user how this works.
            Console.WriteLine("The global metadata for this docset was not found. Please check to see if there are any .json files in the metadata folder, "
                + "or metadata blocks at the bottoms of the markdown files before continuing.");
            Console.WriteLine("If you find metadata in either of those locations, you must enter the exact same values at the prompts. If not found, set appropriate values.");
            Console.WriteLine("All of the following fields take text values except for CommunityContent, which takes 1 or 0.");
            Console.WriteLine("Leaving an attribute blank will remove it from the docset.");
            Console.WriteLine("Press ENTER to continue or CTRL+C to cancel.");
            Console.ReadLine();

            // Put in the standard attributes.
            string s = "";
            foreach (var item in DefaultGlobalAttrNames)
            {
                Console.WriteLine("Enter a value for the global \"{0}\" attribute of this docset.", item);
                s = Console.ReadLine();
                if (s != "") GlobalAttrs.Add(item, s);
            }

            // Add any custom attributes.
            string name = "tmp", value = "";
            Console.WriteLine("Now you may create additional custom attributes for the docset. To do so, enter names and values at the prompts.");
            Console.WriteLine("Entering a blank attribute name will end the custom attribute creation process and resume file conversion.");
            while (name != "")
            {
                name = "";
                Console.Write("Name: ");
                name = Console.ReadLine();
                Console.Write("Value: ");
                value = Console.ReadLine();
                if (name != "" && value != "") GlobalAttrs.Add(name, value);
            }
            HasGlobals = true;
            writeGlobalsToFile();
        }

        private bool tryBuildMsHelpFromInternal(string filePath, out XElement MsHelp)
        {
            MsHelp = new XElement("xml");
            string fullText = File.ReadAllText(filePath);
            if (!Regex.IsMatch(fullText, @"<!--.* : .*-->", RegexOptions.Singleline)) return false;
            string[] lines = File.ReadAllLines(filePath);
            //string[] lines = fullText.Substring(fullText.LastIndexOf("<!--")).Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string assetID = "";
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(" : "))
                {
                    lines[i] = lines[i].Trim();
                    string[] s = lines[i].Split(new string[] { " : " }, StringSplitOptions.None);
                    if (s.Length < 2) continue;
                    if (Regex.IsMatch(s[0], "AssetID")) assetID = s[1];
                    if (Regex.IsMatch(s[0], "Title"))
                    {
                        MsHelp.Add(new XElement(s[0], new XAttribute("Title", s[1]))); 
                    }
                    else if (Regex.IsMatch(s[0], @"Keyword\w"))
                    {
                        MsHelp.Add(new XElement(s[0].Substring(0, 7), new XAttribute("Index", s[0].Substring(7, 1)), new XAttribute("Term", s[1])));
                    }
                    else if (!GlobalAttrs.Keys.Contains(s[0].Trim()))
                    {
                        MsHelp.Add(new XElement("Attr", new XAttribute("Name", s[0]), new XAttribute("Value", s[1])));
                    }
                }
            }
            if (MsHelp.Descendants().Count() < 2 || assetID == "") return false;

            // If the globals have not been set, pull them from here, then rerun this method so they aren't included in the locals.
            if (!HasGlobals)
            {
                checkForStandardGlobalAttributes(MsHelp); 
                if(HasGlobals) return tryBuildMsHelpFromInternal(filePath, out MsHelp);
            }

            return true;
        }

        private bool tryLoadXmlFromJSON(string metaPath, out XElement MsHelp)
        {
            string standardWarning = "\r\nDO NOT generate new metadata if the existing metadata is unavailable, as this will generate a new copy of the file on MSDN and cause a world of problems.";
            XDocument xdoc = new XDocument();
            MsHelp = null;
            string content = "";
            try { content = File.ReadAllText(metaPath); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception)
            {
                // Have to exit here because otherwise we will generate a new asset ID.
                Utils.Die(string.Format("Could not read metadata file {0}. The file may be invalid or you may not have access. Please fix the file and try again.", metaPath));
            }

            try { xdoc = JsonConvert.DeserializeXNode(content); }
            catch (Exception)
            {
                Utils.Die(string.Format("The content of {0} could not be deserialized by the Json converter. Fix the content and re-run the program.{1}", metaPath, standardWarning));
            }
            if (xdoc.FirstNode == null) return false;
            MsHelp = xdoc.FirstNode as XElement; 
            if (!HasGlobals) checkForStandardGlobalAttributes(MsHelp);

            return true;
        }

        /// <summary>
        /// Checks an XElement for properties listed in the DefaultGlobalAttrNames array, and if found, adds them to GlobalAttrs.
        /// </summary>
        /// <param name="content">An XElement containing properties.</param>
        private void checkForStandardGlobalAttributes(XElement x)
        {
            foreach (XElement item in x.Elements("Attr"))
            {
                string name = (item.Attribute("Name") != null && item.Attribute("Value") != null) 
                    ? item.Attribute("Name").Value : "";
                if (DefaultGlobalAttrNames.Contains(name) && !GlobalAttrs.ContainsKey(name))
                    GlobalAttrs.Add(name, item.Attribute("Value").Value);
            }

            if (GlobalAttrs.Count > 0)
            {
                HasGlobals = true;
                writeGlobalsToFile();
            }
            else if (!Program.QuietMode) generateGlobalsFromConsole();
        }

        private void writeGlobalsToFile()
        {
            var j = new JObject();
            foreach (var kvp in GlobalAttrs) j.Add(kvp.Key, kvp.Value);
            while (!Directory.Exists(Path.GetDirectoryName(GlobalJson)))
            {
                var parent = new DirectoryInfo(Path.GetDirectoryName(GlobalJson));
                while (!Directory.Exists(parent.Parent.FullName))
                    parent = parent.Parent;
                parent.Create();
            }
            File.WriteAllText(GlobalJson, j.ToString());
        }
    }
}
