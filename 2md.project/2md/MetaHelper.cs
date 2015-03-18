using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _2md
{
    internal class MetaHelper
    {
        /// The full path to the directory that will host the top level metadata file.
        /// </summary>
        string MetaRoot;
        /// <summary>
        /// The full path to the global metadata file.
        /// </summary>
        string GlobalJson;
        /// <summary>
        /// The MSHelp XML namespace.
        /// </summary>
        XNamespace NS = "http://msdn.microsoft.com/mshelp";            
        Dictionary<string, string> GlobalAttrs = new Dictionary<string, string> ();
        string[] DefaultGlobalAttrNames =  {"Locale", "DocSet", "ProjType", "Technology", "Product", "productversion", "CommunityContent"};
        bool HasGlobals = false;

        /// <summary>
        /// The constructor for the MetaHelper class.
        /// </summary>
        /// <param name="rootDir">The full path to the directory that will host the top level metadata file.</param>
        /// <param name="promptOnMissing">True if the user should be prompted when a piece of expected metadata is missing; false otherwise.</param>
        internal MetaHelper(string rootDir)
        {
            MetaRoot = rootDir;
            GlobalJson = rootDir + "\\global.json";
            HasGlobals = false;
        }

        void CheckForStandardGlobalAttributes(XElement x)
        {
            foreach (XElement item in x.Elements(NS + "Attr"))
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
        }

        private XDocument applyMsHelpPrefix(XDocument MsHelp)
        {
            var xe = MsHelp.Element("xml");
            if (!xe.HasAttributes)
            {
                var x = new XAttribute(XNamespace.Xmlns + "MSHelp", NS);
                xe.Add(x);
            }
            foreach (XElement node in xe.Descendants())
                node.Name = NS + node.Name.LocalName;
            return MsHelp;
        }

        void writeGlobalsToFile()
        {
            string globalJson = Program.MetadataRoot + "\\global.json";
            var j = new JObject();
            foreach (var kvp in GlobalAttrs) j.Add(kvp.Key, kvp.Value);
            if (!Utils.TryWrite(globalJson, j.ToString())) Utils.Die("Failed to write global metadata to file at " + globalJson);
        }
        
        /// <summary>
        /// Extracts important information from the head of an html document and returns it as an XmlDocument.
        /// </summary>
        /// <param name="fileName">The name and path of the html source file.</param>
        /// <returns>An XmlDocument containing important content from the <HEAD> element of the source HTML file</returns>
        internal XDocument GetMetadata(string fileName)
        {
            string content = File.ReadAllText(fileName);
            string MSHelp = " xmlns:MSHelp=\"http://msdn.microsoft.com/mshelp\"";
            XDocument xdoc;
            if (Regex.IsMatch(content, ".*?(<xml>.*?</xml>)", RegexOptions.Singleline))
            {
                xdoc = XDocument.Parse(Regex.Replace(content, ".*?(<xml)(>.*?)</xml>.*", "$1" + MSHelp + "$2</xml>", RegexOptions.Singleline));
                xdoc = applyMsHelpPrefix(xdoc);               
            }
            else xdoc = new XDocument();
            return xdoc;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="xdoc">The metadata for the current markdown file.</param>
        /// <param name="mdFilePath">The path tothe current markdown file.</param>
        /// <returns></returns>
        internal bool ProcessMetadata(XDocument xdoc, string mdFilePath)
        {
            if (!HasGlobals) CheckForStandardGlobalAttributes(xdoc.FirstNode as XElement);

            // Weed out the global attributes from the individual metadata.
            foreach (string attr in GlobalAttrs.Keys.ToList())
            {
                string xPath = "//*[@Name=\"" + attr + "\"]";
                var node = xdoc.XPathSelectElement(xPath);
                if (node != null) node.Remove();
            }

            // Write to JSON.
            string json = JsonConvert.SerializeXNode(xdoc, Formatting.Indented);
            return(Utils.TryWrite(mapToJson(mdFilePath), json));            
        }

        string mapToJson(string filePath)
        {
            string s = filePath.Substring(Program.MdPath.Length);
            return MetaRoot + s.Substring(0, s.LastIndexOf(".")) + ".json";
        }
    }
}
