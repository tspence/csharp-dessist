/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */

using System.Text;
using System.Xml;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo

namespace Dessist
{
    /// <summary>
    /// An undifferentiated SSIS object that has not been matched with a known type
    /// </summary>
    public class SsisObject
    {
        /// <summary>
        /// The XML node type of this object
        /// </summary>
        public string? DtsObjectType;

        /// <summary>
        /// The human readable name of this object
        /// </summary>
        public string? DtsObjectName;

        /// <summary>
        /// A user-readable explanation of what this is
        /// </summary>
        public string? Description;

        /// <summary>
        /// The GUID for this object
        /// </summary>
        public Guid DtsId;

        /// <summary>
        /// Attributes, if any
        /// </summary>
        public readonly Dictionary<string, string> Attributes = new Dictionary<string, string>();

        /// <summary>
        /// All the properties defined in the SSIS
        /// </summary>
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>();

        /// <summary>
        /// List of child elements in SSIS
        /// </summary>
        public readonly List<SsisObject> Children = new List<SsisObject>();

        private readonly SsisObject? Parent;
        public string? ContentValue;
        public string? ID;
        public string? Name;

        public SsisProject Project { get; private set; }
        private string? _functionName;
        private string? _folderName;
        public List<ProgramVariable> _scopeVariables = new List<ProgramVariable>();
        public List<LineageObject> _lineageColumns = new List<LineageObject>();

        /// <summary>
        /// Construct a new SSIS object within a project
        /// </summary>
        /// <param name="project"></param>
        /// <param name="parent"></param>
        /// <param name="element"></param>
        public SsisObject(SsisProject project, SsisObject? parent, XmlElement element)
        {
            Project = project;
            Parent = parent;
            DtsId = Guid.Empty;
            parent?.Children.Add(this);
            DtsObjectType = element.Name;
            foreach (XmlAttribute xa in element.Attributes)
            {
                switch (xa.Name)
                {
                    case "id": ID = xa.Value;
                        break;
                    case "name": Name = xa.Value;
                        break;
                    default:
                        Attributes.Add(xa.Name, xa.Value);
                        break;
                }
            }
        }

        /// <summary>
        /// Shortcut to find a lineage object
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public LineageObject? GetLineageObjectById(string? id)
        {
            return (from LineageObject l in _lineageColumns where l.LineageId == id select l).FirstOrDefault();
        }

        /// <summary>
        /// Set a property
        /// </summary>
        /// <param name="prop_name"></param>
        /// <param name="prop_value"></param>
        private void SetProperty(string prop_name, string prop_value)
        {
            switch (prop_name)
            {
                case "ObjectName":
                    DtsObjectName = prop_value;
                    break;
                case "DTSID":
                    DtsId = Guid.Parse(prop_value);
                    Project.RegisterObject(DtsId, this);
                    break;
                case "Description":
                    Description = prop_value;
                    break;
                default:
                    Properties[prop_name] = prop_value;
                    break;
            }
        }

        /// <summary>
        /// Retrieve a child with the specific name
        /// </summary>
        /// <param name="objectname"></param>
        public SsisObject? GetChildByType(string objectname)
        {
            return (from SsisObject o in Children where o.DtsObjectType == objectname select o).FirstOrDefault();
        }

        /// <summary>
        /// Retrieve a child with the specific name
        /// </summary>
        /// <param name="objectname"></param>
        /// <param name="attribute"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public SsisObject? GetChildByTypeAndAttr(string objectname, string attribute, string value)
        {
            return (from SsisObject o in Children
                where (o.DtsObjectType == objectname)
                      && (o.Attributes[attribute] == value)
                select o).FirstOrDefault();
        }

        public string GetParentDtsName()
        {
            var obj = this;
            while (obj is { DtsObjectName: null })
            {
                obj = obj.Parent;
            }

            return obj == null ? "Unnamed" : obj.DtsObjectName;
        }

        public List<SsisObject> GetChildrenByTypeAndAttr(string attr_key, string value)
        {
            var list = new List<SsisObject>();
            foreach (var child in Children)
            {
                if (child.Attributes.TryGetValue(attr_key, out var attr) && string.Equals(attr, value))
                {
                    list.Add(child);
                }
            }

            return list;
        }

        public string FixExpression(string? expected_type, List<LineageObject> list, string? expression,
            bool inline)
        {
            var ed = new ExpressionData(this.Project, expected_type, list, expression);
            return ed.ToCSharp(inline);
        }

        public string GetScopeVariables(bool include_type)
        {
            // Do we have any variables to pass?
            var p = new StringBuilder();
            if (include_type)
            {
                foreach (var vd in _scopeVariables)
                {
                    p.Append($"ref {vd.CSharpType} {vd.VariableName}");
                }
            }
            else
            {
                foreach (var vd in _scopeVariables)
                {
                    p.Append($"ref {vd.VariableName}, ");
                }
            }

            if (p.Length > 0) p.Length -= 2;
            return p.ToString();
        }

        public string GetFunctionName()
        {
            if (_functionName == null)
            {
                _functionName = StringUtilities.Uniqueify(GetParentDtsName(), Project.FunctionNames);
            }

            return _functionName;
        }

        public string GetFolderName()
        {
            if (_folderName == null)
            {
                _folderName = StringUtilities.Uniqueify(GetParentDtsName(), Project.FolderNames);
            }

            return _folderName;
        }

        public Guid GetNearestGuid()
        {
            var o = this;
            while (o != null && (o.DtsId == Guid.Empty))
            {
                o = o.Parent;
            }

            return o?.DtsId ?? Guid.Empty;
        }

        /// <summary>
        /// Read in a DTS property from the XML stream
        /// </summary>
        /// <param name="element"></param>
        public void ReadDtsProperty(XmlElement element)
        {
            var prop_name =
                (from XmlAttribute xa in element.Attributes
                    where string.Equals(xa.Name, "DTS:Name", StringComparison.CurrentCultureIgnoreCase)
                    select xa.Value).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(prop_name))
            {
                SetProperty(prop_name, element.InnerText);
            }
        }
    }
}