using System.IO.Abstractions;
using DrillPress.Engine;
using DrillPress.SampleRules;

var fileSystem = new FileSystem();

return (int)await new RuleApplication(fileSystem).RunAsync(SampleRuleSet.Create(), args);
