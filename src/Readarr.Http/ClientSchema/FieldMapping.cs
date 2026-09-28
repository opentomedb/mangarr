using System;
using System.Collections.Generic;

namespace Readarr.Http.ClientSchema
{
    public class FieldMapping
    {
        public Field Field { get; set; }
        public Type PropertyType { get; set; }
        public Func<object, object> GetterFunc { get; set; }
        public Action<object, object> SetterFunc { get; set; }

        // UI translations v1 (2026-09-25): the raw Label/HelpText/HelpTextWarning stay on Field; ToSchema
        // localizes them per request with these tokens.
        public Dictionary<string, object> LabelTokens { get; set; }
        public Dictionary<string, object> HelpTextTokens { get; set; }
        public Dictionary<string, object> HelpTextWarningTokens { get; set; }
    }
}
