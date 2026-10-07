using DrillPress.Engine;
using DrillPress.SampleRules;

return (int)await new RuleApplication().RunAsync(SampleRuleCatalog.Create(), args);
