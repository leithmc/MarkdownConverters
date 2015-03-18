***Readme***
Md2HxS takes a directory tree of markdown files and converts them to .htm files, creates a TOC, and compiles the results into an HxS file.

***Syntax***
md2hxs.exe [source directory] [output file] [options]
source directory: The absolute or relative path to the directory that contains markdown files to convert.

output file: (optional) The name of the output HxS file, including path. If not specified, the file will be generated in the same folder as the source directory, with a name derived from the RLTitle attribute in the metadata of the markdown file.

Options:
  -m [metadata root]			Specifies the location of the root folder to contain metadata files. The default value is a folder named "meta" in the same location as [source directory]. The metadata root must be outside of [source directory].

  -global [global metadata filepath]	Specifies the location of global.json, which holds the shared metadata values for the docset. The default value is [metadata root]\global.json.

  -hx [HxComp path]			Specifies the location of HxComp.exe, which compiles html files into an HxS file. The default value is "hxcomp.exe", which assumes it is in your %PATH%.

  -pd [pandoc path]			Specifies the location of Pandoc.exe, which converts markdown files to html. The default value is "pandoc.exe", which assumes it is in your %PATH%.
  
  -hxtx [path]       			Points to an .hxtx file that that contains TOC information, or to where it should be created if there is not one. The default value is the first .hxtx file found in [source driectory], if any, or [source directory]\\[outputfilename].hxtx otherwise.

  -strict				Specifies that the markdown should be interpreted as "strict" (standard) markdown. By default, markdown is assumed to be of the Github flavor. Consider using -strict when your markdown uses manual line breaks for word wrapping in paragraphs.  

  -generate				Tells the program to automatically generate missing metadata without prompting the user. Only use this option if you know that there is no metadata to be found (such as in another location). To automate this function and avoid prompts, make sure there is a global metadata file, as those values will otherwise have to be entered by the user.

  -q					Runs the program in quiet mode, supressing most messages and any prompts that are not required for execution.

  -allowrootless			Tells md2hxs to allow rootless markdown directory trees. By default, the source directory must have exactly one markdown file to serve as the top node of the TOC.

  -?					Displays help information.

***Requirements***
The source directory must contain exactly one .md file, and may optionally contain a /resources/ directory, and one directory with the same name as the .md file, minus file extension, that contains the rest of the tree. Each subdirectory within the tree must have a sibling file with the same name. All .md files must be valid github-flavored markdown. 

Within the markdown documents, relative URIs are supported, but only to files that will be included in the same HxS.

You must have Pandoc.exe and hxcomp.exe installed to run md2hxs, and if they are not in your %PATH%, you must specify their location with the -pd and -hx switches. 
    Pandoc installer: https://github.com/jgm/pandoc/releases 
    HxComp: Install the Visual Studio 2008 sdk (v1) from http://www.microsoft.com/en-us/download/details.aspx?id=508 
    Note: The VS2008 sdk installer requires that you have VS2008 installed. Alternatively, you can unzip the installer and run the MSI directly, but then you have to apply some hacks to get all of the components to register correctly.

***Metadata***
The metadata required for publication by MSDN can be stored in one of two ways:

  JSON (the preferred way)
In this system, the [metadata root] folder mirrors [source directory] but without the /resources/ folder, and with .json files instead of .md files. Otherwise, all file and folder names are the same. Each .json file has a single top-level object that contains all of the document-specific metadata as properties. The global file, at [metadata root]\global.json by default, contains all of the shared metadata that applies across the docset.

 Internal Key/Value Pairs (the old way)
The old system is still supported, but if found, the program will convert your docset to the new system. In the old system, each file ends with a commented section that contains metadata formatted as key:value pairs, with [space]:[space] as a delimiter, as in the following example:
 <!--
   TOCTitle : Kinect for Windows SDK 2.0 Public Preview
   RLTitle : Kinect for Windows SDK
   KeywordA : O:Microsoft.Kinect.atoc_k4w_v2
   KeywordA : 5d9b1697-d20a-bfc6-7a8a-92522b365b59
   KeywordK : Kinect for Windows SDK
   KeywordK : Kinect for Windows SDK, introduction
   AssetID : 5d9b1697-d20a-bfc6-7a8a-92522b365b59
   Locale : en-us
   CommunityContent : 1
   TopicType : kbOrient
   DocSet : K4Wv2
   ProjType : K4Wv2Proj
   Technology : Kinect for Windows
   Product : Kinect for Windows SDK v2
   productversion : 20
 -->
Note that some of the metadata items are simplified from their XML representation. For example, <Keyword Index="A" Term="O:Microsoft.Kinect.atoc_k4w_v2" /> becomes "KeywordA : O:Microsoft.Kinect.atoc_k4w_v2".

If neither style of metadata is present, the program will generate document-specific metadata based on the document content and prompt you for the global metadata values. If you opt not to generate metadata, the program will exit.

***Output***
Md2hxs creates a folder named “Compile”, in the same directory as the source directory, which contains all the converted html files plus images and supporting files. It then compiles the contents of this folder into an HxS file, in the same directory as the source directory and the compile folder.
Log information is written to [outputfilename].log
If there are no .json files for metadata, it will create those as well.

***Remarks***
File names are generated based on the RLTitle attribute of the file and adding path segments as necessary to ensure uniqueness. If the file does not have an RLTitle attribute, the filename will be based on the original file name instead.

Md2HxS preserves external links and updates links to files included in the same HxS. If you have multiple doc sets that are likely to link to each other with a high frequency, you should consider rolling them into the same HxS. (Or if there is sufficient need, we will implement support for HxC collections.)

