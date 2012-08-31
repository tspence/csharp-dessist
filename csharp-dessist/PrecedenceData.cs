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

        public PrecedenceData(SsisObject o)
        {
            // Retrieve the prior guid
            SsisObject prior = o.GetChildByTypeAndAttr("DTS:Executable", "DTS:IsFrom", "-1");
            BeforeGuid = Guid.Parse(prior.Attributes["IDREF"]);
            SsisObject posterior = o.GetChildByTypeAndAttr("DTS:Executable", "DTS:IsFrom", "0");
            AfterGuid = Guid.Parse(posterior.Attributes["IDREF"]);
        }
    }
}
