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
        private string _namespaceName;
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
            var name = StringUtilities.CleanNamespaceName(Path.GetFileNameWithoutExtension(ssis_filename));
            var project = new SsisProject
            {
                Name = name,
                _namespaceName = name
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
            var obj = new SsisObject(this, parent);
            parent?.Children.Add(obj);
            obj.DtsObjectType = element.Name;
            foreach (XmlAttribute xa in element.Attributes)
            {
                obj.Attributes.Add(xa.Name, xa.Value);
            }

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
        /// <param name="g"></param>
        /// <returns></returns>
        public SsisObject? GetObjectByGuid(Guid g)
        {
            if (_guid_lookup.TryGetValue(g, out var v))
            {
                return v;
            }
            Log($"Can't find SSIS object matching GUID {g}");
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

   }
}