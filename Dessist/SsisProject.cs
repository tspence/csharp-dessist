using System.Xml;

// ReSharper disable SuggestBaseTypeForParameter

namespace Dessist
{
    /// <summary>
    /// Represents an SSIS project as read from disk
    /// </summary>
    public class SsisProject
    {
        private readonly List<string> _logs = new List<string>();
        private SsisObject _ssis;
        public string Name { get; private set; }
        public string Namespace { get; private set; }
        private Dictionary<string, ProgramVariable> _var_dict = new Dictionary<string, ProgramVariable>();
        private Dictionary<Guid, SsisObject> _guid_lookup = new Dictionary<Guid, SsisObject>();

        /// <summary>
        /// Log something
        /// </summary>
        /// <param name="message"></param>
        internal void Log(string message = "")
        {
            _logs.Add(message);
        }

        /// <summary>
        /// Retrieve logs from this Ssis Project
        /// </summary>
        /// <returns></returns>
        public string[] GetLog()
        {
            return _logs.ToArray();
        }

        /// <summary>
        /// The root object of this SSIS package
        /// </summary>
        public SsisObject RootObject
        {
            get { return _ssis; }
        }
        
        /// <summary>
        /// Attempt to read an SSIS package into memory
        /// </summary>
        /// <param name="ssis_filename"></param>
        public static SsisProject LoadFromFile(string ssis_filename)
        {
            var name = Path.GetFileNameWithoutExtension(ssis_filename);
            var project = new SsisProject
            {
                Name = name,
                Namespace = StringUtilities.CleanNamespaceName(name)
            };

            // Read in the file, one element at a time
            // TODO: Should read the dtproj file instead of the dtsx file, then produce multiple classes, one for each .DTSX file
            var xd = new XmlDocument();
            xd.Load(ssis_filename);
            project._ssis = project.ReadObject(xd.DocumentElement, null);
            return project;
        }

        /// <summary>
        /// Recursive read object
        /// </summary>
        /// <param name="element"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        private SsisObject? ReadObject(XmlElement? element, SsisObject? parent)
        {
            if (element == null) return null;
            var obj = new SsisObject(this, parent, element);

            // Iterate through all children of this element
            foreach (XmlNode child in element.ChildNodes)
            {
                switch (child)
                {
                    case XmlElement childElement:
                    {
                        if (childElement.Name == "DTS:Property" || childElement.Name == "DTS:PropertyExpression")
                        {
                            obj.ReadDtsProperty(childElement);
                        }
                        else
                        {
                            ReadObject(childElement, obj);
                        }
                        break;
                    }
                    case XmlText _:
                    case XmlCDataSection _:
                        obj.ContentValue = child.InnerText;
                        break;
                    default:
                        Log($"Help: I don't understand XML element type {child.GetType().Name}");
                        break;
                }
            }

            return obj;
        }

        /// <summary>
        /// Find an SSIS object within this project by its GUID
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public SsisObject? GetObjectByGuid(Guid guid)
        {
            if (_guid_lookup.TryGetValue(guid, out var v))
            {
                return v;
            }
            Log($"Can't find SSIS object matching GUID {guid}");
            return null;
        }

        /// <summary>
        /// Find an SSIS object within this project by its GUID
        /// </summary>
        /// <param name="guidString"></param>
        /// <returns></returns>
        public SsisObject? GetObjectByGuid(string? guidString)
        {
            if (string.IsNullOrWhiteSpace(guidString))
            {
                return null;
            }

            if (Guid.TryParse(guidString, out var guid))
            {
                return GetObjectByGuid(guid);
            }

            return null;
        }

        /// <summary>
        /// Register an object by its GUID
        /// </summary>
        /// <param name="g"></param>
        /// <param name="o"></param>
        public void RegisterObject(Guid g, SsisObject o)
        {
            _guid_lookup[g] = o;
        }

        /// <summary>
        /// Register a variable by its name
        /// </summary>
        /// <param name="vdVariableName"></param>
        /// <param name="vd"></param>
        public void RegisterVariable(string vdVariableName, ProgramVariable vd)
        {
            _var_dict[vdVariableName] = vd;
        }

        /// <summary>
        /// Find a variable by its name
        /// </summary>
        /// <param name="varname"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public ProgramVariable? GetVariable(string varname)
        {
            if (_var_dict.TryGetValue(varname, out var variable))
            {
                return variable;
            }
            Log($"Can't find SSIS variable matching name {varname}");
            return null;
        }
        
        public readonly List<string> FunctionNames = new List<string>();

        public readonly List<string> FolderNames = new List<string>();


        public IEnumerable<SsisObject> Variables()
        {
            return from c in RootObject.Children where c.DtsObjectType == "DTS:Variable" select c;
        }

        public IEnumerable<SsisObject> Functions()
        {
            // Next, write all the executable functions to the main file
            var functions = (from SsisObject c in RootObject.Children where c.DtsObjectType == "DTS:Executable" select c).ToList();
            if (functions.Count > 0)
            {
                return functions;
            }
            
            // Search through "Executables" root object second
            var executables = from SsisObject c in RootObject.Children where c.DtsObjectType == "DTS:Executables" select c;
            var functionList = new List<SsisObject>();
            foreach (var exec in executables)
            {
                functionList.AddRange(from e in exec.Children where e.DtsObjectType == "DTS:Executable" select e);
            }
            if (functionList.Count == 0)
            {
                Log("No functions ('DTS:Executable') objects found in the specified file.");
            }

            return functionList;
        }

        public IEnumerable<SsisObject> ConnectionStrings()
        {
            return from SsisObject c in RootObject.Children where c.DtsObjectType == "DTS:ConnectionManager" select c;
        }
   }
}