using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace csharp_dessist
{
    public class LineageObject
    {
        public string LineageId;
        public string DataTableName;
        public int DataTableColumn;

        public LineageObject(SsisObject outcol, SsisObject component)
        {
            // TODO: Complete member initialization
            this.LineageId = outcol.Attributes["lineageId"];
            this.DataTableName = "component" + component.Attributes["id"];
        }

        public override string ToString()
        {
            return String.Format("{0}.Rows[row][{1}]", DataTableName, DataTableColumn);
        }
    }
}
