using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace csharp_dessist
{
    public class PrecedenceData
    {
        public Guid BeforeGuid;
        public Guid AfterGuid;
        public string Expression;

        public PrecedenceData(SsisObject o)
        {
            // Retrieve the two guids
            SsisObject prior = o.GetChildByTypeAndAttr("DTS:Executable", "DTS:IsFrom", "-1");
            BeforeGuid = Guid.Parse(prior.Attributes["IDREF"]);
            SsisObject posterior = o.GetChildByTypeAndAttr("DTS:Executable", "DTS:IsFrom", "0");
            AfterGuid = Guid.Parse(posterior.Attributes["IDREF"]);

            // Retrieve the expression to evaluate
            o.Properties.TryGetValue("Expression", out Expression);
        }

        public override string ToString()
        {
            if (String.IsNullOrEmpty(Expression)) {
                return String.Format(@"Precedence: ""{0}"" before ""{1}""", SsisObject.GetObjectByGuid(BeforeGuid).GetFunctionName(), SsisObject.GetObjectByGuid(AfterGuid).GetFunctionName());
            } else {
                return String.Format(@"IF ({2}) ""{0}"" before ""{1}""", SsisObject.GetObjectByGuid(BeforeGuid).GetFunctionName(), SsisObject.GetObjectByGuid(AfterGuid).GetFunctionName(), Expression);
            }
        }
    }
}
