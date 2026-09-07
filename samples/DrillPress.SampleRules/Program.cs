using DrillPress.Engine;
using DrillPress.SampleRules;

return (int)await new RuleApplication().RunAsync(SampleRuleSet.Create(), args);
