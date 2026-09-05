using DrillPress.Engine;
using DrillPress.SampleRules;

return (int)await RuleApplication.RunAsync(SampleRuleSet.Create(), args);
