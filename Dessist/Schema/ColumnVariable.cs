/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// ReSharper disable StringLiteralTypo

namespace Dessist
{
    public class ColumnVariable
    {
        public int ID;
        public string Name;
        public string SsisTypeName;
        public string LineageID;
        public string Length;
        public string Precision;
        public string Scale;
        public string CodePage;
        
        private SsisProject _project;

        public ColumnVariable(SsisObject o)
        {
            _project = o.Project;
            int.TryParse(o.Attributes["id"], out ID);
            Name = o.Attributes["name"];
            SsisTypeName = o.Attributes["dataType"];
            if (o.Attributes.ContainsKey("lineageId")) LineageID = o.Attributes["lineageId"];
            Precision = o.Attributes["precision"];
            Scale = o.Attributes["scale"];
            CodePage = o.Attributes["codePage"];
            Length = o.Attributes["length"];
            LineageID = string.Empty;
        }

        public string? CsharpType()
        {
            return SsisTypes.LookupSsisTypeName(_project, SsisTypeName);
        }

        public string? SqlDbType()
        {
            return SsisTypes.LookupSsisTypeSqlName(_project, SsisTypeName, Length, int.Parse(Precision), int.Parse(Scale));
        }
    }
}
