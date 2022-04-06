/*
 * 2012-2015 Ted Spence, http://tedspence.com
 * License: http://www.apache.org/licenses/LICENSE-2.0 
 * Home page: https://github.com/tspence/csharp-dessist
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dessist
{
    public class PrecedenceData
    {
        public Guid BeforeGuid;
        public Guid AfterGuid;
        public string Expression;
        private SsisProject _project;

        public SsisObject? Target
        {
            get
            {
                return _project.GetObjectByGuid(AfterGuid);
            }
        }

        public PrecedenceData(SsisProject project, SsisObject o)
        {
            _project = project;
            var prior = o.GetChildByTypeAndAttr("DTS:Executable", "DTS:IsFrom", "-1");
            BeforeGuid = Guid.Parse(prior.Attributes["IDREF"]);
            var posterior = o.GetChildByTypeAndAttr("DTS:Executable", "DTS:IsFrom", "0");
            AfterGuid = Guid.Parse(posterior.Attributes["IDREF"]);
            o.Properties.TryGetValue("Expression", out Expression);
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Expression)) {
                return
                    $"After \"{_project.GetObjectByGuid(BeforeGuid).GetFunctionName()}\" execute \"{_project.GetObjectByGuid(AfterGuid).GetFunctionName()}\"";
            } else {
                return
                    $"After \"{_project.GetObjectByGuid(BeforeGuid).GetFunctionName()}\", if ({Expression}), \"{_project.GetObjectByGuid(AfterGuid).GetFunctionName()}\"";
            }
        }
    }
}
