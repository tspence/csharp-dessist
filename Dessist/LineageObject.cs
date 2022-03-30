/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
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
        public string FieldName;
        public string Transform;

        public LineageObject(string lineage_id, string table_name, string field_name)
        {
            LineageId = lineage_id;
            DataTableName = table_name;
            FieldName = field_name;
        }

        public LineageObject(string lineage_id, string lineage_transform)
        {
            LineageId = lineage_id;
            Transform = lineage_transform;
            if (Transform[Transform.Length - 1] == ';') {
                Transform = Transform.Substring(0, Transform.Length - 1);
            }
        }

        public override string ToString()
        {
            if (DataTableName == null) {
                return Transform;
            } else {
                return String.Format(@"{0}.Rows[row][""{1}""]", DataTableName, FieldName);
            }
        }
    }
}
