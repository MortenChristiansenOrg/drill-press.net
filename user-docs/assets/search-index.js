window.DRILLPRESS_SEARCH = [
  {
    "title": "The rule author’s manual",
    "url": "index.html",
    "text": "A small language for your team’s conventions. Turn a convention your team keeps explaining into a small, repeatable check. Drill Press rules are ordinary C#: you choose some code, describe a requirement, and tell the reader how to fix a violation. Write your first rule → Explore the API field gu"
  },
  {
    "title": "Choose your next step · The rule author’s manual",
    "url": "index.html#choose",
    "text": "01 / START SMALL A working first rule Create a bundle, run it against a project, and understand its output. 02 / SAY WHAT YOU MEAN Readable declarations Selections, requirements, exceptions, and named conditions. 03 / LOOK DEEPER Calls and compiler facts Overloads, arguments, nullable values, and call paths. 04 / BUILD CONFIDENCE Test before you share Small source examples, precise findings, and safe-fix checks."
  },
  {
    "title": "Three ideas are enough to start · The rule author’s manual",
    "url": "index.html#mental-model",
    "text": "A candidate is something to inspect. A file, a method, a type, or a particular call. It carries the source location needed to report a useful finding. A query chooses candidates. Where narrows the selection. Reuse the same query in several rules when they share a scope. A rule explains a convention. Forbid reports everything selected. Require reports only the selected candidates that fail its condition."
  },
  {
    "title": "You do not need to know Roslyn first · The rule author’s manual",
    "url": "index.html#knowledge",
    "text": "You should be comfortable with C# methods, lambdas, and basic LINQ. Roslyn is the C# compiler’s API: it lets a rule distinguish a real framework call from an unrelated method with the same name. Start with Drill Press’s wrappers."
  },
  {
    "title": "Write your first rule",
    "url": "first-rule.html",
    "text": "From a C# project to your first useful finding. We’ll prevent direct console logging in production code. The first version only reports violations; it does not change source files. 1. Create a rule bundle Install a .NET 10 SDK and use project references from a checkout of Drill Press. Register you"
  },
  {
    "title": "1. Create a rule bundle · Write your first rule",
    "url": "first-rule.html#project",
    "text": "Install a .NET 10 SDK and use project references from a checkout of Drill Press. Register your rules explicitly in a console application; this application becomes the rule bundle. Create samples/MyRules/MyRules.csproj with the following content. These relative references assume that location inside the Drill Press checkout. samples/MyRules/MyRules.csproj <Project Sdk=\"Microsoft.NET.Sdk\"> <PropertyGroup> <OutputType>Exe</OutputType> <TargetFramework>net10.0</TargetFramework> <ImplicitUsings>enable</ImplicitUsings> <Nullable>enable</Nullable> </PropertyGroup> <ItemGroup> <ProjectReference Include=\"../../src/DrillPress.RuleAuthoring/DrillPress.RuleAuthoring.csproj\" /> <ProjectReference Include=\"../../src/DrillPress.Engine/DrillPress.Engine.csproj\" /> </ItemGroup> </Project> The authoring library supplies the building blocks. The engine supplies RuleApplication , which hosts your compiled rules so the CLI can run them. You do not need NativeAOT to get started."
  },
  {
    "title": "2. Declare and host the rule · Write your first rule",
    "url": "first-rule.html#declaration",
    "text": "samples/MyRules/Program.cs using DrillPress; using DrillPress.Engine; using DrillPress.Projects; using DrillPress.Queries; using DrillPress.Semantics; var rules = new RuleSet(); var writeLine = CodeType.Named(\"System.Console\").Member(\"WriteLine\"); rules.For(ProjectFacts.ProductionFiles.Invocations() .Where(call => call.Calls(writeLine))) .Forbid(\"TEAM001\", \"Use the application logger instead of Console.WriteLine.\"); return (int)await new RuleApplication().RunAsync(rules, args); TEAM001 is your stable rule identifier. Choose a unique identifier for every registered rule. The message should tell someone what to do next—not just say “bad code.” Both must be nonblank and single-line."
  },
  {
    "title": "3. Build and check a target · Write your first rule",
    "url": "first-rule.html#run",
    "text": "Create a tiny target in samples/MyRuleTarget . Its console call is an intentional violation. samples/MyRuleTarget/MyRuleTarget.csproj <Project Sdk=\"Microsoft.NET.Sdk\"> <PropertyGroup> <TargetFramework>net10.0</TargetFramework> </PropertyGroup> </Project> samples/MyRuleTarget/Worker.cs public class Worker { public void Run() => System.Console.WriteLine(\"starting\"); } Run these commands from the repository root: Terminal · repository root dotnet build DrillPress.slnx dotnet build samples/MyRules/MyRules.csproj dotnet restore samples/MyRuleTarget/MyRuleTarget.csproj dotnet src/DrillPress.Cli/bin/Debug/net10.0/DrillPress.Cli.dll check --build-host src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll --rules samples/MyRules/bin/Debug/net10.0/MyRules.dll samples/MyRuleTarget/MyRuleTarget.csproj Replace the last path to check your own project or solution. Quote paths with spaces. The target must be restored first; generated assemblies needed by the target must also be built. The target’s own global.json controls its SDK selection. The bundle is not the project loader Run the CLI with --rules , not your rule DLL with a project path. The CLI loads the project and passes a captured compilation to your bundle. Direct bundle output is an internal protocol, not the human-readable findings."
  },
  {
    "title": "4. Read the result · Write your first rule",
    "url": "first-rule.html#output",
    "text": "For the target above, the finding points directly to the console call: Expected CLI output TEAM001 Use the application logger instead of Console.WriteLine. samples/MyRuleTarget/Worker.cs 3:26 Output groups findings by rule and file, followed by physical line/column locations. A clean run prints nothing. Exit codes are 0 for clean, 1 for findings, and 2 when the check could not finish. A + before a location means a proposed fix survived validation. This first rule offers none. Only the CLI’s fix command applies changes; check never does."
  },
  {
    "title": "5. Keep a growing bundle understandable · Write your first rule",
    "url": "first-rule.html#organize",
    "text": "Move registrations into a class such as LoggingRules with a Register(RuleSet rules) method. Put reusable selections and named predicates next to their domain rules. Keep compiler details in small helpers, not inside a long registration expression. Register every rule explicitly before calling RuleApplication.RunAsync . This keeps the bundle easy to inspect and avoids runtime assembly scanning. Next, write a small test before adding more policies."
  },
  {
    "title": "Select and compose",
    "url": "selections.html",
    "text": "Choose the code you mean, then say what it must do. A good declaration separates where the policy applies from what it requires . Keep these decisions visible in the code. Start with the smallest useful selection Start here You get Code.Methods Ordinary method declarations; not constructors, accessors"
  },
  {
    "title": "Start with the smallest useful selection · Select and compose",
    "url": "selections.html#roots",
    "text": "Start here You get Code.Methods Ordinary method declarations; not constructors, accessors, or local functions. Code.Types / Code.Interfaces Named types / interfaces. Partial type definitions appear once per evaluated project context. Code.MemberReferences Source expressions referring to fields, properties, and methods. Sources.Files / Sources.Projects Source files / evaluated project contexts. files.Invocations() Bound calls inside an existing file selection. files.Nodes<T>() / files.Declarations() Syntax of a chosen kind / resolved declarations, including locals and fields. The Code roots live in DrillPress ; file and syntax helpers use DrillPress.Queries . See the field guide for all namespaces."
  },
  {
    "title": "Forbid or require? · Select and compose",
    "url": "selections.html#require",
    "text": "Require a naming convention for async methods using DrillPress; var rules = new RuleSet(); var asyncMethods = Code.Methods.Where(method => method.IsAsync); rules.For(asyncMethods).Require( method => method.Name.AsSpan().EndsWith(\"Async\"), \"TEAM002\", \"Give asynchronous methods an Async suffix.\"); This selects async methods, then reports the ones whose names lack the suffix. By contrast, Forbid reports every candidate already selected. Neither method throws away the useful source location."
  },
  {
    "title": "Name conditions and exceptions · Select and compose",
    "url": "selections.html#conditions",
    "text": "Compose a convention and its exception using DrillPress; using DrillPress.Analysis; using DrillPress.Semantics; var rules = new RuleSet(); var asynchronous = new RuleCondition<CodeMethod>(method => method.IsAsync); var eventHandler = new RuleCondition<CodeMethod>(method => method.HasAttribute(CodeType.Named(\"Product.EventHandlerAttribute\"))); var applicationMethods = Code.Methods.Where(method => !method.Source.Project.IsTestProject); rules.For(applicationMethods.Where(asynchronous.ExceptWhen(eventHandler))) .Require(method => method.Name.AsSpan().EndsWith(\"Async\"), \"TEAM003\", \"Give asynchronous application methods an Async suffix.\"); And , Or , Not , and condition-level ExceptWhen compose Boolean tests with short-circuit behavior. Query-level ExceptWhen excludes matches. Both Where and Require also accept ordinary lambdas."
  },
  {
    "title": "Transform and connect selections · Select and compose",
    "url": "selections.html#composition",
    "text": "CodeQuery<T> is the reusable selection type. It resembles LINQ, but it belongs to an analysis rather than an arbitrary list. Operation Use it when Select Each candidate becomes another value. SelectMany Each candidate has several children to inspect. Join You need matching pairs, using explicit keys and an optional equality comparer. WithoutMatching You need owners missing a counterpart. Supply keys or a predicate comparing the two candidates. At A tuple or calculated value needs an existing code element as its diagnostic anchor. Create / In(solution) You need a custom query, or need to read a query in a supplied analysis. See counterpart matching and custom queries for complete examples. A plain project or number has no automatic source location; add an anchor or a location selector before reporting it."
  },
  {
    "title": "Reuse queries; keep predicates pure · Select and compose",
    "url": "selections.html#reuse",
    "text": "Drill Press materializes a query once per query instance and analysis. Reuse the instance to share work. Do not increment counters, write files, log to standard output, or depend on evaluation order in predicates. A new analysis gets fresh caches. Prefer files.Invocations() to finding calls globally and filtering them afterwards: the narrow file selection avoids compiler work outside your policy’s scope. Generated files supply compiler information but ordinary roots exclude them; see generated and linked source ."
  },
  {
    "title": "Types and members",
    "url": "identities.html",
    "text": "Match what code refers to—not how someone spelled it. Code can spell the same operation in different ways: a fully qualified name, a using alias, or a static import. Semantic identity means matching the actual type or member chosen by the compiler. Describe a type Types you know—and types your rule proj"
  },
  {
    "title": "Describe a type · Types and members",
    "url": "identities.html#types",
    "text": "Types you know—and types your rule project does not reference using DrillPress.Semantics; var text = CodeType.Of<string>(); var textLists = CodeType.Of<List<string>>(); var console = CodeType.Named(\"System.Console\"); var contract = CodeType.Named(\"Product.IStorage\", \"Product.Contracts\"); Of<T>() records a type your rule project can reference, including exact generic arguments. Named describes a target type by its namespace-qualified name, without taking a project dependency on it. The optional second argument restricts the declaring assembly; omitting it permits any assembly. CodeType exposes MetadataName , AssemblyName , and TypeArguments . These describe compiler identity, not a display name. Use Matches to test a compiler type; record equality is not a substitute for its optional-assembly and framework-facade matching rules."
  },
  {
    "title": "Open and constructed generics · Types and members",
    "url": "identities.html#generics",
    "text": "Empty slots match any constructed arguments using DrillPress.Semantics; var anyPool = CodeType.Named(\"System.Buffers.ArrayPool<>\"); var dictionaries = CodeType.Named(\"System.Collections.Generic.Dictionary<,>\"); var nested = CodeType.Named(\"Product.Outer<>.Inner<,>\"); var listArrays = CodeType.Named(\"System.Collections.Generic.List<>[]\"); var onlyStringLists = CodeType.Of<List<string>>(); var onlyTwoDimensionalArrays = CodeType.Of<List<string>[,]>(); <> means one generic parameter; <,> means two. Empty slots normalize to the compiler’s metadata representation. Keep array suffixes such as [] or [,] . After an open generic type, use dots for nested types: Product.Outer<>.Inner<,> selects Inner<U, V> declared inside Outer<T> , regardless of their type arguments. This is not Outer<Inner<,>> , which puts a type argument inside Outer . Existing metadata names using + still work. If the first containing type is not generic, use + to distinguish it from a namespace, as in Product.Container+Item<> . Names are not C# type expressions Named(\"List<string>\") and Named(\"List<T>\") are not accepted. Use a fully qualified open name, or Of<List<string>>() for an exact constructed type."
  },
  {
    "title": "Build members from their declaring type · Types and members",
    "url": "identities.html#members",
    "text": "A family, a parameterless overload, and an exact overload using DrillPress.Semantics; var console = CodeType.Named(\"System.Console\"); var allWriteLines = console.Member(\"WriteLine\"); var blankLine = allWriteLines.WithParameters(); var textLine = allWriteLines.WithParameters(CodeType.Of<string>()); var trim = CodeType.Of<string>().Member(nameof(string.Trim), []); var emptyStringReferences = CodeType.Of<string>().Member(nameof(string.Empty)).References; Omitting parameters matches every method overload. Passing an empty list—or calling WithParameters() —matches only a parameterless method. A nonempty list matches the parameter types in order. WithParameters creates a new descriptor; it does not narrow the original one. CodeMember also has a constructor taking declaring type, name, and optional parameters. Its DeclaringType and Name are readable properties. Matches accepts compiler methods, fields, or properties; a parameter-constrained descriptor matches methods only. Reduced extension methods are compared with their original static declaration, so an exact parameter list includes the extension receiver."
  },
  {
    "title": "References or calls? · Types and members",
    "url": "identities.html#references",
    "text": "member.References selects named source expressions referring to that member. It includes method groups as well as invoked method names. It does not represent object creation, indexer syntax, or implicit uses. For the actual invocation—including its arguments—use files.Invocations() and call.Calls(member) . The lower-level Members.Are<T>(name) and Members.Are(type, name) return reusable conditions for Code.MemberReferences . These and member.References preserve name-based discovery optimizations. A MemberReference exposes ContainingType , MemberName , Location , and optional Source , Syntax , and Symbol ."
  },
  {
    "title": "Configure a family of restricted APIs · Types and members",
    "url": "identities.html#sets",
    "text": "Keep non-repeatable values out of production code using DrillPress; using DrillPress.Configuration; using DrillPress.Projects; using DrillPress.Queries; using DrillPress.Semantics; var rules = new RuleSet(); var randomValues = new ApiSet( CodeType.Of<Guid>().Member(nameof(Guid.NewGuid)), CodeType.Of<Random>().Member(nameof(Random.Next))); rules.For(ProjectFacts.ProductionFiles.Invocations() .Where(call => randomValues.Contains(call.Target))) .Forbid(\"TEAM004\", \"Pass reproducible values into the application core.\"); ApiSet.Contains matches compiler methods against any configured descriptor. Scope this policy to the deterministic part of your application; randomness is appropriate in many other places."
  },
  {
    "title": "Attributes, inheritance, and symbols · Types and members",
    "url": "identities.html#attributes",
    "text": "method.HasAttribute(type) and declaration.HasAttribute(type) inspect resolved attributes. declaration.Implements(contract) follows inherited interfaces, not just the interface names written beside the class. For raw compiler symbols, Symbols.HasAttribute can include or exclude derived attribute types; Symbols.IsOrDerivesFrom follows base classes; Symbols.Implements follows interfaces. Unresolved types are not guessed."
  },
  {
    "title": "Inspect source code",
    "url": "syntax.html",
    "text": "Files, declarations, syntax, and precise locations. Use file and declaration wrappers first. Reach for syntax when your convention is about an exact piece of source: a modifier, a switch expression, or a particular literal. Files and path patterns Keep console output inside a tracing adapter using Dri"
  },
  {
    "title": "Files and path patterns · Inspect source code",
    "url": "syntax.html#files",
    "text": "Keep console output inside a tracing adapter using DrillPress; using DrillPress.Configuration; using DrillPress.Projects; using DrillPress.Queries; using DrillPress.Semantics; var rules = new RuleSet(); var adapters = new PathPattern(\"**/Tracing/*.cs\"); var writeLine = CodeType.Named(\"System.Console\").Member(\"WriteLine\"); rules.For(ProjectFacts.ProductionFiles.Invocations() .Where(call => call.Calls(writeLine))) .Require(call => adapters.Matches(call.Source.Document.Path), \"TEAM005\", \"Keep direct console output inside the Tracing adapter.\"); CodeFile provides a slash-normalized Path , Name , Folder , Source , and a location at the start of the file. Its Nodes<T>() enumerates syntax in that file; the same method on a file query builds a reusable selection. PathPattern.Matches compares the entire path, case-sensitively, without accessing the filesystem. * stays within one path segment; ? matches one character; ** crosses directories; **/ also matches no directory. It normalizes slashes but does not guess a repository root. Supply an explicitly relative path if your policy needs one."
  },
  {
    "title": "A small compiler vocabulary · Inspect source code",
    "url": "syntax.html#vocabulary",
    "text": "Syntax node A structured piece of the written code, such as a class declaration or a string literal. It is useful even when the code does not compile. Token A small language element such as an identifier, keyword, or punctuation mark. Tokens carry exact source spans. Trivia Whitespace, comments, and directives attached to tokens. The name does not mean it is unimportant—formatting rules need it. Symbol The resolved identity of a declaration: which method, type, field, or parameter a piece of code means. Semantic model The compiler’s bridge from written syntax to symbols, types, and other meaning."
  },
  {
    "title": "Choose a syntax kind · Inspect source code",
    "url": "syntax.html#nodes",
    "text": "Find repeated format-dispatch expressions using DrillPress; using DrillPress.Collections; using DrillPress.Projects; using DrillPress.Queries; using Microsoft.CodeAnalysis.CSharp.Syntax; var rules = new RuleSet(); var switches = ProjectFacts.ProductionFiles.Nodes<SwitchExpressionSyntax>(); rules.For(DuplicateSyntax.In(switches, minimumTokens: 20)) .Forbid(\"TEAM006\", \"Share the repeated format-dispatch expression.\"); SwitchExpressionSyntax represents an expression such as format switch { ... } . Other useful types in Microsoft.CodeAnalysis.CSharp.Syntax include LiteralExpressionSyntax , AttributeSyntax , TypeDeclarationSyntax , and MemberDeclarationSyntax . Each CodeNode<T> has Syntax , Source , and Location , plus Operation , TypeInfo , and Constant . A constant result distinguishes “no constant value” from a constant whose value is null: check HasValue before reading Value ."
  },
  {
    "title": "Methods, types, and all other declarations · Inspect source code",
    "url": "syntax.html#declarations",
    "text": "CodeMethod exposes Name , IsAsync , HasAttribute , Body , Flow , Reaches , Syntax , Symbol , Source , Solution , and Location . The symbol can be null when binding fails. CodeDeclaration is a named type with Name , Namespace , Implements , HasAttribute , and its source, syntax, symbol, solution, and location. Use files.Declarations() or SymbolQueries.Declarations for fields, properties, events, parameters, locals, accessors, and separate partial declarations. Those return CodeSymbol , with Symbol , Syntax , Source , and Location . SymbolQueries.References selects resolved simple-name references to types, namespaces, and members. ReferencesTo(symbol) follows compiler identity or matching loaded source declarations across compilations. Neither guesses ambiguous bindings; implicit uses belong to operation queries."
  },
  {
    "title": "Report the smallest useful location · Inspect source code",
    "url": "syntax.html#locations",
    "text": "Report the internal keyword, not the entire type using DrillPress; using DrillPress.Queries; using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp; using Microsoft.CodeAnalysis.CSharp.Syntax; using RuleSet = DrillPress.RuleSet; var rules = new RuleSet(); var internalTypes = Sources.Nodes<TypeDeclarationSyntax>() .Where(node => node.Syntax.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax) .Where(node => node.Syntax.Modifiers.Any(SyntaxKind.InternalKeyword)); rules.For(internalTypes).Forbid( \"TEAM007\", \"Use implicit assembly visibility for top-level types.\", location: node => node.Source.Locate( node.Syntax.Modifiers.First(token => token.IsKind(SyntaxKind.InternalKeyword)).Span)); AnalysisSource.Locate(TextSpan) converts an exact character range into a SourceLocation : file path, zero-based Start , Length , and one-based Line / Column . Columns use UTF-16 character positions, not byte offsets. Physical positions ignore #line remapping. Locations and fix factories run only for violating candidates. A custom candidate can implement ICodeElement with Location and optional Source , or use query-level At to retain an existing candidate’s source membership."
  },
  {
    "title": "Generated and linked source · Inspect source code",
    "url": "syntax.html#generated",
    "text": "Ordinary roots exclude generated files. Sources.FilesIncludingGenerated lets advanced facts inspect them, but the evaluator still suppresses findings anchored there. Linked files and alternative target frameworks remain separate compilation memberships; do not collapse them just because the path is the same. AnalysisSource.Document exposes captured text, path, generated classification, and edit eligibility. Tree is the original syntax tree; Model is the shared semantic model; Project owns that compilation context. Prefer these captured values to opening source files yourself."
  },
  {
    "title": "Calls and flow",
    "url": "analysis.html",
    "text": "Ask what a call does and what the compiler knows. A method name alone does not tell you which overload is called, how named arguments map to parameters, or whether a value may be null. These APIs use the compiler’s answers. Inspect calls and arguments Disallow an infinite timeout on a specific API u"
  },
  {
    "title": "Inspect calls and arguments · Calls and flow",
    "url": "analysis.html#invocations",
    "text": "Disallow an infinite timeout on a specific API using DrillPress; using DrillPress.Operations; using DrillPress.Semantics; var rules = new RuleSet(); var send = CodeType.Named(\"Product.Transport\").Member(\"Send\"); rules.For(OperationQueries.Invocations .Where(call => call.Calls(send)) .Where(call => call.Argument(\"timeoutMilliseconds\")?.Value.ConstantValue is { HasValue: true, Value: -1 })) .Forbid(\"TEAM008\", \"Pass a bounded timeout to Transport.Send.\"); CodeInvocation.Target is the chosen method overload. Calls compares it to your CodeMember . Argument(\"name\") returns the argument mapped to that declared parameter, including optional defaults; it returns null when the parameter is absent. A named argument appearing first in the source is not necessarily the first declared parameter. Operation exposes the compiler’s invocation object. Its Instance is the receiver (the object before the dot), if any. Source and Location retain the original context and complete call span."
  },
  {
    "title": "Look beyond ordinary calls · Calls and flow",
    "url": "analysis.html#operations",
    "text": "An operation describes what bound code does. A new expression is an object-creation operation; a method call is an invocation operation. Operations can include things the compiler adds implicitly, such as conversions. Flag a legacy serializer being constructed using DrillPress; using DrillPress.Operations; using DrillPress.Semantics; using Microsoft.CodeAnalysis.Operations; var rules = new RuleSet(); var legacy = CodeType.Named(\"Product.LegacySerializer\"); rules.For(OperationQueries.Of<IObjectCreationOperation>() .Where(item => item.Operation.Constructor is { } constructor && legacy.Matches(constructor.ContainingType))) .Forbid(\"TEAM009\", \"Create the supported serializer instead.\"); OperationQueries.All , Of<T>() , and Invocations share ordinary-source roots. InFiles(files) and InvocationsIn(files) restrict discovery; the latter is also available as files.Invocations() . Discovery covers bodies, accessors, initializers, attributes, top-level code, and nested functions—not just methods selected by Code.Methods . CodeOperation<T> gives you the typed Operation , ContainingSymbol when resolvable, Source , and Location . Implicit operations may share a source span. Invalid operations can remain visible; check the facts your conclusion depends on."
  },
  {
    "title": "Follow a call path · Calls and flow",
    "url": "analysis.html#paths",
    "text": "Find blocking sleeps beneath async entry points using DrillPress; using DrillPress.Semantics; var rules = new RuleSet(); var sleep = CodeType.Named(\"System.Threading.Thread\").Member(\"Sleep\"); rules.For(Code.Methods.Where(method => method.IsAsync && method.Reaches(sleep))) .Forbid(\"TEAM010\", \"Use an awaited delay along asynchronous call paths.\"); method.Reaches(member) follows statically bound calls through loaded source, including helpers in referenced projects and generated implementations. It terminates cycles. It does not infer reflection, runtime interface dispatch, dependency injection, or delegate dispatch into a lambda. An uncalled local function is not attributed to its enclosing method. “Not found” is not a proof of safety A negative result only says the modeled source call paths did not reach that target. It does not prove that runtime execution cannot call it."
  },
  {
    "title": "Use method-local flow facts · Calls and flow",
    "url": "analysis.html#flow",
    "text": "method.Flow returns shared MethodFlow analysis. The older MethodFlow.For(solution, method) entry point shares that cache; constructing new MethodFlow(method) creates independent lazy analysis. Data Reads, writes, captured variables, and other compiler data-flow sets. Check Succeeded before trusting them. A captured variable is one referenced by a nested lambda or local function. Graph A control-flow graph: blocks of operations connected by possible branches. It can be null for a method without a supported executable body. NullState(expression) The compiler’s nullable state at an expression. MaybeNull , NotNull , or None (no answer). It is not a runtime guarantee. A query exposing methods with captured variables using DrillPress; var methodsWithClosures = Code.Methods.Where(method => method.Flow.Data is { Succeeded: true } data && data.Captured.Length > 0); This is evidence to build on, not a rule forbidding every closure. In the pooled-buffer example , a captured local initialized by ArrayPool<T>.Rent indicates a concrete ownership concern. The rule does not claim to prove delegate escape or lease lifetime."
  },
  {
    "title": "Read nullable state at a call site · Calls and flow",
    "url": "analysis.html#nullable",
    "text": "Find potentially null text receivers using DrillPress; using DrillPress.Operations; using DrillPress.Semantics; using Microsoft.CodeAnalysis; using Microsoft.CodeAnalysis.CSharp; using RuleSet = DrillPress.RuleSet; var rules = new RuleSet(); var trim = CodeType.Of<string>().Member(nameof(string.Trim)).WithParameters(); rules.For(OperationQueries.Invocations.Where(call => call.Calls(trim) && call.Operation.Instance is { } receiver && call.Source.Model.GetTypeInfo(receiver.Syntax, call.Source.Project.CancellationToken).Nullability.FlowState == NullableFlowState.MaybeNull)) .Forbid(\"TEAM011\", \"Handle missing input before trimming text.\"); Here the semantic model connects the receiver’s written expression to its compiler type information. The pattern check handles static calls or other cases without a receiver. Keep these details in a named helper when the registration becomes difficult to read. The RuleSet alias is deliberate: Microsoft.CodeAnalysis also has a type with that name. The alias selects Drill Press’s rule set while making the compiler’s extension methods available."
  },
  {
    "title": "Projects and relationships",
    "url": "relationships.html",
    "text": "Connect policies across files, types, and project boundaries. Some policies depend on more than one code fragment: a codec needs tests, an interface has implementations, or a production project references an unwanted package. Keep project identity explicit. Read evaluated project facts Keep a legacy package out"
  },
  {
    "title": "Read evaluated project facts · Projects and relationships",
    "url": "relationships.html#projects",
    "text": "Keep a legacy package out of production projects using DrillPress; using DrillPress.Projects; var rules = new RuleSet(); rules.For(ProjectFacts.ProductionFiles.Where(file => file.Source.Project.ReferencesPackage(\"Newtonsoft.Json\"))) .Forbid(\"TEAM012\", \"Use the application's System.Text.Json contract.\"); This intentionally reports on each ordinary file in the offending project. Project-only diagnostics need an existing source anchor; an empty project cannot be reported by this source-diagnostic protocol. If one finding per project is preferable, build a query that chooses one ordinary file per project. ProjectFacts.TestFiles and ProductionFiles use the project classification captured by the loader. WithRole(predicate) selects projects using your policy. ReferencesPackage compares direct package identifiers case-insensitively. AnalysisProject offers Name , ProjectPath , TargetFramework , IsTestProject , Packages , Properties , and SourceRoots . Packages are direct evaluated references, including requested central versions—not the entire transitive dependency graph. Properties contain selected policy values and explicit overrides, not the whole environment."
  },
  {
    "title": "Require a counterpart · Projects and relationships",
    "url": "relationships.html#counterparts",
    "text": "Require a named xUnit round-trip test for each concrete codec using DrillPress; using DrillPress.Semantics; using DrillPress.Testing; using TypeKind = Microsoft.CodeAnalysis.TypeKind; var rules = new RuleSet(); var contract = CodeType.Named(\"Product.ITextCodec\"); var codecs = Code.Types.Where(type => !type.Source.Project.IsTestProject && type.Symbol is { IsAbstract: false, TypeKind: TypeKind.Class or TypeKind.Struct } && type.Implements(contract)); var tests = XunitTests.Methods.Where(method => method.Source.Project.IsTestProject); rules.For(codecs.WithoutMatching(tests, (codec, test) => test.Name == $\"{codec.Name}RoundTrip\" && codec.Solution.ProjectGraph.Includes( test.Source.Project, codec.Source.Project))) .Forbid(\"TEAM013\", \"Add an xUnit <CodecName>RoundTrip test for this codec.\"); The missing thing has no source location, so the rule reports on the existing codec. This naming convention checks discoverable coverage; it does not prove the test’s assertions are correct. Extend the match with namespace or ownership information if your project has duplicate type names. The keyed WithoutMatching(other, key, otherKey, comparer) overload is useful for simple equality and large inventories. The predicate overload handles context-sensitive matching. Join returns matching pairs rather than missing owners."
  },
  {
    "title": "Respect project boundaries · Projects and relationships",
    "url": "relationships.html#graph",
    "text": "solution.ProjectGraph.Includes(consumer, owner) means the consumer is the owner itself or transitively references that exact evaluated context. DependenciesOf(project) includes the project itself. CompatibleViewsOf(owner) returns separate compatible project sets; it does not merge alternate frameworks or incompatible evaluations. A project name is not a unique compilation identity. A multi-targeted project appears as separate contexts, and a linked source file can be compiled with different settings. Keep those memberships separate when comparing code."
  },
  {
    "title": "Follow type and method relationships · Projects and relationships",
    "url": "relationships.html#types",
    "text": "solution.Relationships is the shared CodeRelationships object, also available from CodeRelationships.In(solution) . Member Meaning CallsFrom(methodSymbol) Direct bound source calls, excluding nested function bodies. CallersOf(methodSymbol) Source calls bound to a loaded source declaration. Metadata-only targets have no source declaration key. Reaches(methodSymbol, member) The same static call-path analysis exposed by method.Reaches(member) . DerivedTypes(type) Compatible source types inheriting a class or implementing an interface; includes abstract and test types for you to filter. FilesOf(type) Ordinary source files for a type’s partial declarations in its own context."
  },
  {
    "title": "Count concrete implementations · Projects and relationships",
    "url": "relationships.html#implementations",
    "text": "Find interfaces with one concrete production implementation using DrillPress; var rules = new RuleSet(); rules.For(Code.Interfaces.Where(type => type.Solution.Implementations.HasExactlyOne(type))) .Forbid(\"TEAM014\", \"Consider removing this single-implementation interface.\"); InterfaceImplementations.HasExactlyOne considers compatible source views and excludes abstract and test implementations. It reports when any compatible view has exactly one concrete production implementation. It does not know about implementations in unloaded external consumers. Whether the interface should actually be removed is your team’s architectural choice."
  },
  {
    "title": "Facts, comparisons, and baselines",
    "url": "facts.html",
    "text": "Share expensive work and compare the things that matter. When several rules need the same calculated information, give it a name and compute it once. When a policy compares two inventories or an accepted version of source, make the comparison explicit. Create a custom query Choose one reportable file per p"
  },
  {
    "title": "Create a custom query · Facts, comparisons, and baselines",
    "url": "facts.html#custom",
    "text": "Choose one reportable file per production project using DrillPress; using DrillPress.Queries; var files = Sources.Files.Where(file => !file.Source.Project.IsTestProject); var firstFilePerProject = CodeQuery<CodeFile>.Create(solution => files.In(solution) .GroupBy(file => file.Source.Project) .Select(group => group.OrderBy(file => file.Path).First())); var rules = new RuleSet(); rules.For(firstFilePerProject).Require( file => file.Source.Project.Properties.TryGetValue(\"Nullable\", out var value) && value == \"enable\", \"TEAM015\", \"Enable nullable reference types for this project.\"); Create takes a function from AnalysisSolution to candidates. In(solution) reads an existing query. Reuse these instances and keep selection functions pure. For long custom loops, observe solution.CancellationToken . Missing captured properties are not the same as a complete MSBuild evaluation; choose conservative policy behavior."
  },
  {
    "title": "Cache reusable facts · Facts, comparisons, and baselines",
    "url": "facts.html#facts",
    "text": "Share an inventory of public type names using DrillPress; using DrillPress.Facts; using Microsoft.CodeAnalysis; var publicTypes = Code.Types.Where(type => type.Symbol.DeclaredAccessibility == Accessibility.Public); var names = new AnalysisFact<IReadOnlyDictionary<string, int>>(solution => publicTypes.In(solution) .GroupBy(type => type.Name) .ToDictionary(group => group.Key, group => group.Count())); var repeatedNames = names.SelectMany(counts => counts.Where(pair => pair.Value > 1).Select(pair => pair.Key)); var repeatedTypes = publicTypes.Join( repeatedNames, type => type.Name, name => name, (type, _) => type); AnalysisFact<T>.In(solution) computes once per fact instance per analysis. SelectMany turns part of its value into a query. Values should be treated as immutable. ProjectFact<T> instead takes a function of AnalysisProject . Read it with In(solution, project) ; the project must belong to that solution. Alternative frameworks do not share values. Fact dependencies may read other facts, but cycles fail, and computation failures remain cached for that analysis. None of these caches persist across runs."
  },
  {
    "title": "Compare inventories and repeated syntax · Facts, comparisons, and baselines",
    "url": "facts.html#sets",
    "text": "Understand missing and unexpected items using DrillPress.Collections; var formats = new SetComparison<string>( expected: [\"json\", \"xml\"], actual: [\"json\", \"yaml\"]); Console.WriteLine(string.Join(\", \", formats.Missing)); // xml Console.WriteLine(string.Join(\", \", formats.Unexpected)); // yaml Console.WriteLine(formats.AreEqual); // False SetComparison<T> compares membership, not occurrence counts. It supports a custom equality comparer. Duplicate inputs do not change the result. DuplicateSyntax.In(query, minimumTokens) reports every occurrence of an exact repeated token shape within a compilation context. It ignores comments and whitespace but preserves identifier spelling and literal text. A repeated shape does not prove equivalent behavior or justify an automatic extraction."
  },
  {
    "title": "Anchor calculated results · Facts, comparisons, and baselines",
    "url": "facts.html#anchors",
    "text": "Keep a custom value attached to the type that owns it using DrillPress; var documentedTypes = Code.Types.Select(type => new { Type = type, Summary = type.Symbol.GetDocumentationCommentXml() }); var undocumented = documentedTypes .Where(item => string.IsNullOrWhiteSpace(item.Summary)) .At(item => item.Type); var rules = new RuleSet(); rules.For(undocumented).Forbid(\"TEAM016\", \"Document this type's purpose.\"); At produces a LocatedCandidate<T> : your calculated Value plus the existing anchor’s Location and Source . This also works for joined tuples and missing-counterpart checks."
  },
  {
    "title": "Compare with accepted source · Facts, comparisons, and baselines",
    "url": "facts.html#baseline",
    "text": "Create a change-aware rule factory using DrillPress; using DrillPress.Baselines; using DrillPress.Configuration; static RuleSet CreateRules(SourceBaseline accepted) { var rules = new RuleSet(); var legacyFiles = new PathPattern(\"**/Legacy/**/*.cs\"); rules.For(accepted.ChangedFiles.Where(file => accepted.ChangeOf(file) == SourceChange.Added && legacyFiles.Matches(file.Path))) .Forbid(\"TEAM017\", \"Add new implementations outside the legacy layer.\"); return rules; } Create the baseline with new SourceBaseline(acceptedAnalysis) before analyzing the new source. ChangeOf(file) returns Added , Modified , or Unchanged ; ChangedFiles selects additions and modifications. ChangeOf(project, symbol) compares declaration text, including partial declarations. PreviousAccessibility(project, symbol) returns the old compiler accessibility, or null when no matching declaration was captured. Renames and changed signatures count as additions; this is not a rename detector. There is no removed-file candidate. A baseline is explicit input The SDK does not fetch Git history, load a baseline from the CLI, or infer an accepted commit. Supply an accepted analysis yourself and use stable project paths, framework labels, and evaluation properties. Test the comparison with two workspaces."
  },
  {
    "title": "Offer safe fixes",
    "url": "fixes.html",
    "text": "A useful correction needs more than compilable code. A finding can propose a correction, but “the edited code compiles” is not enough. A fix must preserve the behavior your policy promises to preserve in every affected loaded compilation. Start with a proven fix Attach the built-in empty-string correct"
  },
  {
    "title": "Start with a proven fix · Offer safe fixes",
    "url": "fixes.html#existing",
    "text": "Attach the built-in empty-string correction using DrillPress; using DrillPress.Fixes; using DrillPress.Semantics; var rules = new RuleSet(); var emptyString = CodeType.Of<string>().Member(nameof(string.Empty)); rules.For(emptyString.References).Forbid( \"TEAM018\", \"Use the empty string literal instead of string.Empty.\", fix: EmptyStringFix.Create); A fix factory receives the violating candidate and returns a FixProposal or null. Returning null keeps the finding but offers no fix. Forbid and Require both accept the optional fix argument. Built-in fix Supported correction EmptyStringFix.Create string.Empty to \"\" , with binding, conversion, expression-tree, comment, and directive safeguards. OrdinalComparerFix.IsArgument / Create The condition selects comparer arguments. The fix is narrowly limited to the supported Enumerable.Distinct<string> overload pair, not arbitrary comparer APIs. ModifierFix.RemoveRedundantAccessibility Removes a redundant accessibility modifier from a CodeNode<MemberDeclarationSyntax> , checking accessibility before and after in affected contexts. These factories intentionally withhold fixes for cases they cannot prove. For example, replacing a field access with a constant can change overload selection higher up the expression. A similarly named collection method is not automatically safe for comparer removal."
  },
  {
    "title": "Build an exact edit proposal · Offer safe fixes",
    "url": "fixes.html#proposal",
    "text": "A helper that requires the caller to supply its proof using DrillPress.Analysis; using DrillPress.Fixes; using Microsoft.CodeAnalysis.Text; static FixProposal ProposeReplacement( AnalysisSource source, TextSpan span, string replacement, Func<RewriteContext, bool> preservesBehavior) { var edit = SourceChanges.Replace(source, span, replacement); return SourceChanges.Propose([edit], preservesBehavior); } SourceChanges.Replace creates a SourceEdit anchored to the captured file identity, fingerprint, original text, start offset, and length. It does not edit the filesystem. Pass all edits for a correction to SourceChanges.Propose , along with a required proof callback. The proposal checks source eligibility, original text, inactive regions, conflicts, and compiler errors before invoking the proof. The RewriteContext contains the Original project, the Rewritten compilation with local edits applied, and the complete Edits batch, including edits belonging to other contexts. Never use an unconditional “true” proof The callback must establish the semantic claim of your particular correction. Consider overloads, conversions, evaluation order, side effects, lifetime, and reflection. When those cannot be established, offer a finding without a fix."
  },
  {
    "title": "Check binding without overstating it · Offer safe fixes",
    "url": "fixes.html#binding",
    "text": "BindingProof.PreservesEnclosingExpressions(source, replacedNode, replacement) checks surrounding expression symbols, types, and conversions and withholds errors, nameof , and expression-tree cases. It is a reusable compiler check, not proof of equal side effects, evaluation order, or lifetime. For advanced integrations, the FixProposal(edits, validate) constructor accepts a per-project validator and IsSafeIn(project) evaluates it. Prefer SourceChanges.Propose for ordinary custom fixes so its standard eligibility and compiler checks are included."
  },
  {
    "title": "Validation and application are separate · Offer safe fixes",
    "url": "fixes.html#agreement",
    "text": "Your rule proposes one complete batch. The engine checks every loaded context containing an edited physical file—even a context that reported no finding. The validator requires agreement and withholds conflicting or disagreeing batches. The CLI marks an accepted correction with + . Only fix writes it. Generated, missing, inactive, non-editable, or ambiguously bound memberships cannot be assumed safe. Source fingerprints guard against applying edits to a changed file. A multi-file proposal is validated as a unit, but physical application is atomic per file , not a filesystem-wide transaction. If later application fails, the CLI retains recovery information. There is no file-creation/deletion, project-wide rename, or general refactoring engine in this API. See the application and recovery contract for operational details."
  },
  {
    "title": "Test the fixes you withhold · Offer safe fixes",
    "url": "fixes.html#test-fixes",
    "text": "Check both HasFix and the complete FixedText(path) result. Include linked files, multiple frameworks, conflicting proposals, comments, invalid code, and different overload contexts. “Finding exists” does not prove “fix is safe.” The next chapter uses the production validator in memory."
  },
  {
    "title": "Test and run your rules",
    "url": "testing.html",
    "text": "Prove the rule finds the right code—and leaves the rest alone. Keep tests small enough that the intended violation is obvious. Test the negative cases as carefully as the positive ones: a noisy convention quickly stops being useful. Add the consumer test kit Reference src/DrillPress.Testing/DrillPress.Testing.cs"
  },
  {
    "title": "Add the consumer test kit · Test and run your rules",
    "url": "testing.html#setup",
    "text": "Reference src/DrillPress.Testing/DrillPress.Testing.csproj from your test project, along with your rule project and your chosen test framework. The test kit runs the real evaluator and response validator. It does not need a target project on disk. RuleTestWorkspace() uses the host runtime’s assembly references for convenience. You can supply an explicit list of compiler MetadataReference objects when you need exact reference packs or synthetic assemblies. The default constructor reads reference assemblies; the target source and edit results stay in memory."
  },
  {
    "title": "Assert the complete finding and fixed text · Test and run your rules",
    "url": "testing.html#first-test",
    "text": "An xUnit test for the empty-string rule using DrillPress; using DrillPress.Fixes; using DrillPress.Semantics; using DrillPress.Testing; using Xunit; public class EmptyStringRuleTests { [Fact] public async Task Replaces_the_empty_string_field() { var workspace = new RuleTestWorkspace(); workspace.AddProject(\"Example\", [new TestSource(\"Example.cs\", \"class C { string Value => string.Empty; }\")]); var rules = new RuleSet(); rules.For(CodeType.Of<string>().Member(nameof(string.Empty)).References) .Forbid(\"TEAM018\", \"Use the empty string literal instead of string.Empty.\", fix: EmptyStringFix.Create); var result = await workspace.CheckAsync(rules); Assert.Equal( [new TestFinding(\"TEAM018\", \"Example.cs\", 1, 26, \"string.Empty\", true)], result.Findings); Assert.Equal(\"class C { string Value => \\\"\\\"; }\", result.FixedText(\"Example.cs\")); } } TestFinding includes rule ID, path, physical line and column, exact highlighted text, and HasFix . FixedText applies only the validated plan in memory. Create another workspace from the resulting text to check that a second run is clean."
  },
  {
    "title": "Model the contexts your policy depends on · Test and run your rules",
    "url": "testing.html#contexts",
    "text": "AddProject returns an AnalysisProject . Dependencies must already belong to the workspace. Its options are: Argument Purpose name , sources Project identity and exact TestSource(Path, Text, Generated) values. framework A context label, defaulting to net10.0 . It does not select reference assemblies. isTest Mark a test project for project-role policies. dependencies Previously added source projects to reference. symbols Conditional compilation symbols for #if . allowErrors Keep deliberately invalid source for failure-path tests. references , packages Override compiler references or supply direct package facts. Package facts do not download packages or supply assemblies. Model a referenced production project using DrillPress.Testing; var workspace = new RuleTestWorkspace(); var production = workspace.AddProject(\"Product\", [ new TestSource(\"Codec.cs\", \"public class Codec { }\") ]); workspace.AddProject(\"Product.Tests\", [ new TestSource(\"Tests.cs\", \"class Tests { Codec value = new(); }\") ], isTest: true, dependencies: [production]); Use the same source path and text across projects to test linked-file agreement. Use different reference sets for real framework differences. For xUnit attribute detection, isTest: true alone is not enough: include real xUnit references and Fact/Theory attributes."
  },
  {
    "title": "Write conventions for xUnit bodies · Test and run your rules",
    "url": "testing.html#xunit",
    "text": "Require a predictable three-part test layout using DrillPress; using DrillPress.Testing; var rules = new RuleSet(); var tests = XunitTests.Methods; rules.For(tests).Require( method => method.Body.EmptyLines.Count <= 2, \"TEAM019\", \"Keep at most two empty lines in a test.\", location: method => method.Body.EmptyLines[2]); rules.For(tests.Where(method => method.Body.EarlyAssertion is not null)) .Forbid(\"TEAM020\", \"Move assertions after the final empty line.\", location: method => method.Body.EarlyAssertion!); XunitTests.AreTests is the reusable condition behind Methods . It recognizes genuine Fact/Theory attributes, including derived attributes, in xUnit core assemblies. Unrelated attributes named “Fact” do not count. TestBody.EmptyLines excludes multiline content, disabled text, and nested functions. Assertions contains TestAssertion(MemberName, Location) entries for resolved xUnit assertions outside nested functions. EarlyAssertion finds the first assertion before the final qualifying blank line, except a sole synchronous Assert.Throws . Expression-bodied methods have no block-body layout facts. When a fixture’s exact formatting is the subject of the rule, protect just that fixture with an explained CSharpier ignore comment. Code embedded in string literals normally needs no ignore. Do not use formatter exclusions merely to prefer a different style."
  },
  {
    "title": "Test an accepted baseline · Test and run your rules",
    "url": "testing.html#baselines",
    "text": "Compare two in-memory versions using DrillPress.Baselines; using DrillPress.Testing; var before = new RuleTestWorkspace(); before.AddProject(\"Product\", [new TestSource(\"A.cs\", \"class A { }\")]); var baseline = new SourceBaseline(before.Analyze()); var after = new RuleTestWorkspace(); after.AddProject(\"Product\", [ new TestSource(\"A.cs\", \"class A { public int Value; }\"), new TestSource(\"B.cs\", \"class B { }\") ]); var changes = baseline.ChangedFiles.In(after.Analyze()); // A.cs is Modified; B.cs is Added."
  },
  {
    "title": "Before sharing a bundle · Test and run your rules",
    "url": "testing.html#checklist",
    "text": "Test one violation and a nearby allowed case, including configured exceptions. Check exact locations and full output; do not assert only the count. Test alias spelling, overloads, generated source, and unresolved symbols where relevant. For fixes, check both accepted and withheld corrections. Run the bundle against a restored real project with the CLI."
  },
  {
    "title": "When a rule does not behave as expected · Test and run your rules",
    "url": "testing.html#troubleshooting",
    "text": "It finds nothing. Check the project/file scope, whether the source is generated, the declaring type’s full name, and whether your exact overload includes all parameters. The test compiles, but the target does not. Runtime reference defaults are a convenience, not a target reference pack. Supply explicit references for fidelity. There is a finding but no fix. Inspect the safety boundaries in Offer safe fixes . This is often intentional, not a broken rule. A rule throws. Check null/absent compiler facts, unsuccessful flow analysis, and invalid source. Do not convert unknown facts into confident findings."
  },
  {
    "title": "API field guide",
    "url": "reference.html",
    "text": "Find the right building block without learning the compiler first. Start with selections, conditions, and type or member descriptors. Use compiler objects when your policy needs details that these building blocks do not expose. Namespace tip Start with using DrillPress; , then import the feature namespace listed bel"
  },
  {
    "title": "Declare and compose · API field guide",
    "url": "reference.html#declarations",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "RuleSet · API field guide",
    "url": "reference.html#api-ruleset",
    "text": "For(query); Evaluate(solution); Evaluate(memberReferences) Collect explicit registrations. Evaluate returns ordered RuleDiagnostic values; it is not the complete cross-context fix-validation workflow."
  },
  {
    "title": "RuleScope<T> · API field guide",
    "url": "reference.html#api-rulescopet",
    "text": "Forbid(id, message, location?, fix?); Forbid(descriptor, location?, fix?); Require(condition, id, message, location?, fix?) Forbid reports the selection; Require reports failures. Returns the scope for further registrations."
  },
  {
    "title": "RuleDescriptor / RuleDiagnostic · API field guide",
    "url": "reference.html#api-ruledescriptor-rulediagnostic",
    "text": "Id, Message / Descriptor, Location, Source, Fix Stable policy text and a source-anchored result. IDs must be unique within a rule set."
  },
  {
    "title": "RuleCondition<T> · API field guide",
    "url": "reference.html#api-ruleconditiont",
    "text": "constructor(predicate); And; Or; Not; ExceptWhen Pure reusable conditions with short-circuit composition."
  },
  {
    "title": "CodeQuery<T> / QueryPredicates · API field guide",
    "url": "reference.html#api-codequeryt-querypredicates",
    "text": "Where; ExceptWhen; Select; SelectMany; Join; WithoutMatching; At; Create; In Reusable, analysis-cached selection. Predicate extensions also allow lambdas for Require."
  },
  {
    "title": "Code · API field guide",
    "url": "reference.html#api-code",
    "text": "Methods; Types; Interfaces; MemberReferences Ordinary source roots; types are deduplicated per context."
  },
  {
    "title": "SourceLocation / ICodeElement · API field guide",
    "url": "reference.html#api-sourcelocation-icodeelement",
    "text": "FilePath, Start, Length, Line, Column / Location, Source Physical UTF-16 spans and the contract for custom reportable candidates."
  },
  {
    "title": "Files and syntax · API field guide",
    "url": "reference.html#source",
    "text": "DrillPress.Queries · Read the guide →"
  },
  {
    "title": "Sources · API field guide",
    "url": "reference.html#api-sources",
    "text": "Files; FilesIncludingGenerated; Projects; Nodes<T>(); Attributes Choose ordinary source by default. Generated source can inform facts but cannot anchor findings."
  },
  {
    "title": "SourceQueryExtensions · API field guide",
    "url": "reference.html#api-sourcequeryextensions",
    "text": "files.Nodes<T>(); files.Invocations(); files.Declarations() Start compiler discovery inside an existing file scope."
  },
  {
    "title": "CodeFile · API field guide",
    "url": "reference.html#api-codefile",
    "text": "Source; Path; Name; Folder; Location; Nodes<T>() One document membership; normalized path strings do not read the filesystem."
  },
  {
    "title": "CodeNode<T> · API field guide",
    "url": "reference.html#api-codenodet",
    "text": "Syntax; Source; Location; Operation; TypeInfo; Constant Original syntax plus optional compiler facts. Check constant HasValue."
  },
  {
    "title": "LocatedCandidate<T> · API field guide",
    "url": "reference.html#api-locatedcandidatet",
    "text": "constructor(value, anchor); Value; Location; Source Attach a custom value to a real source element without losing context."
  },
  {
    "title": "Types, members, and symbols · API field guide",
    "url": "reference.html#semantic",
    "text": "DrillPress.Semantics · Read the guide →"
  },
  {
    "title": "CodeType · API field guide",
    "url": "reference.html#api-codetype",
    "text": "Of<T>(); Named(name, assembly?); Member(name, parameters?); Matches; MetadataName; AssemblyName; TypeArguments Metadata identity with open-generic slot normalization, arrays, and optional assembly/constructed-argument constraints. The primary constructor accepts raw metadata identity."
  },
  {
    "title": "CodeMember · API field guide",
    "url": "reference.html#api-codemember",
    "text": "constructor(type, name, parameters?); WithParameters; References; Matches; DeclaringType; Name Fields/properties/method families, or exact method overloads. Empty parameters mean parameterless, not all overloads."
  },
  {
    "title": "Members · API field guide",
    "url": "reference.html#api-members",
    "text": "Are<T>(name); Are(type, name) Reusable name-aware MemberReference conditions."
  },
  {
    "title": "MemberReference · API field guide",
    "url": "reference.html#api-memberreference",
    "text": "ContainingType; MemberName; Location; Source; Syntax; Symbol A named source expression and its binding. Manually supplied references can lack compiler context."
  },
  {
    "title": "Symbols · API field guide",
    "url": "reference.html#api-symbols",
    "text": "HasAttribute(symbol, type, includeDerived?); IsOrDerivesFrom; Implements Semantic predicates for raw compiler symbols."
  },
  {
    "title": "SymbolQueries · API field guide",
    "url": "reference.html#api-symbolqueries",
    "text": "Declarations; DeclarationsIn(files); References; ReferencesTo(symbol) Declaration occurrences and resolved simple-name references, including fields and locals."
  },
  {
    "title": "CodeSymbol · API field guide",
    "url": "reference.html#api-codesymbol",
    "text": "Source; Syntax; Symbol; Location Resolved declaration/reference wrapper."
  },
  {
    "title": "Analysis context · API field guide",
    "url": "reference.html#contexts",
    "text": "DrillPress.Analysis · Read the guide →"
  },
  {
    "title": "CodeMethod · API field guide",
    "url": "reference.html#api-codemethod",
    "text": "Name; IsAsync; HasAttribute; Flow; Reaches; Body; Syntax; Symbol; Source; Solution; Location One ordinary method; its compiler symbol may be absent."
  },
  {
    "title": "CodeDeclaration · API field guide",
    "url": "reference.html#api-codedeclaration",
    "text": "Name; Namespace; Implements; HasAttribute; Syntax; Symbol; Source; Solution; Location One named type definition, with partial declarations deduplicated in the owning context."
  },
  {
    "title": "AnalysisSource · API field guide",
    "url": "reference.html#api-analysissource",
    "text": "Project; Document; Tree; Model; Locate(span) Captured source and its cached semantic model. Document exposes text, file identity, generated status, and edit eligibility."
  },
  {
    "title": "AnalysisProject · API field guide",
    "url": "reference.html#api-analysisproject",
    "text": "Name; ProjectPath; TargetFramework; IsTestProject; Packages; Properties; SourceRoots; Sources; Snapshot; Compilation; CancellationToken An independent evaluated compilation. Advanced constructor: pair an ordered ProjectSnapshot with its matching CSharpCompilation."
  },
  {
    "title": "AnalysisSolution · API field guide",
    "url": "reference.html#api-analysissolution",
    "text": "Projects; Methods; Types; MemberReferences; Implementations; ProjectGraph; Relationships; Options; CancellationToken Shared query/fact cache lifetime. Advanced constructors accept projects, optional AnalysisOptions, and cancellation."
  },
  {
    "title": "AnalysisOptions · API field guide",
    "url": "reference.html#api-analysisoptions",
    "text": "EnableOptimizations; Profile Execution strategy and optional measurements, not policy semantics. Leave defaults for ordinary rules."
  },
  {
    "title": "Operations and flow · API field guide",
    "url": "reference.html#operations",
    "text": "DrillPress.Operations · DrillPress.Flow · Read the guide →"
  },
  {
    "title": "OperationQueries · API field guide",
    "url": "reference.html#api-operationqueries",
    "text": "All; Of<T>(); Invocations; InFiles(files); InvocationsIn(files) Compiler operations across executable source, including implicit operations and nested bodies."
  },
  {
    "title": "CodeOperation<T> · API field guide",
    "url": "reference.html#api-codeoperationt",
    "text": "Operation; ContainingSymbol; Source; Location Typed operation wrapper; binding can be incomplete."
  },
  {
    "title": "CodeInvocation · API field guide",
    "url": "reference.html#api-codeinvocation",
    "text": "Target; Calls(member); Argument(parameterName); Operation; Source; Location Overload-aware calls and compiler-mapped arguments."
  },
  {
    "title": "MethodFlow · API field guide",
    "url": "reference.html#api-methodflow",
    "text": "constructor(method); For(solution, method); Graph; Data; NullState(expression) Lazy CFG, data-flow sets, and nullable state. Prefer method.Flow for shared analysis."
  },
  {
    "title": "Projects and relationships · API field guide",
    "url": "reference.html#graphs",
    "text": "DrillPress.Projects · DrillPress.Relationships · Read the guide →"
  },
  {
    "title": "ProjectFacts · API field guide",
    "url": "reference.html#api-projectfacts",
    "text": "TestFiles; ProductionFiles; WithRole(predicate); project.ReferencesPackage(id) Project-role selections and direct NuGet package identity."
  },
  {
    "title": "ProjectGraph · API field guide",
    "url": "reference.html#api-projectgraph",
    "text": "constructor(solution); Includes; DependenciesOf; CompatibleViewsOf Exact evaluated-context dependencies. Prefer solution.ProjectGraph to share an instance."
  },
  {
    "title": "CodeRelationships · API field guide",
    "url": "reference.html#api-coderelationships",
    "text": "In(solution); CallsFrom; CallersOf; Reaches; DerivedTypes; FilesOf Source relationships and static call paths. Also available as solution.Relationships."
  },
  {
    "title": "InterfaceImplementations · API field guide",
    "url": "reference.html#api-interfaceimplementations",
    "text": "constructor(solution); HasExactlyOne(declaration) Concrete production implementation counts in compatible source views; solution.Implementations shares this analysis."
  },
  {
    "title": "Facts and comparison · API field guide",
    "url": "reference.html#comparison",
    "text": "DrillPress.Facts · DrillPress.Collections · DrillPress.Baselines · Read the guide →"
  },
  {
    "title": "AnalysisFact<T> · API field guide",
    "url": "reference.html#api-analysisfactt",
    "text": "constructor(compute); In(solution); SelectMany(selector) One immutable-by-convention value per fact and analysis."
  },
  {
    "title": "ProjectFact<T> · API field guide",
    "url": "reference.html#api-projectfactt",
    "text": "constructor(compute); In(solution, project) One value per project context within the supplied analysis."
  },
  {
    "title": "SetComparison<T> · API field guide",
    "url": "reference.html#api-setcomparisont",
    "text": "constructor(expected, actual, comparer?); Missing; Unexpected; AreEqual Membership comparison; duplicates do not change membership."
  },
  {
    "title": "DuplicateSyntax · API field guide",
    "url": "reference.html#api-duplicatesyntax",
    "text": "In(query, minimumTokens = 12) Every repeated exact token-shape occurrence within its compilation context."
  },
  {
    "title": "SourceBaseline · API field guide",
    "url": "reference.html#api-sourcebaseline",
    "text": "constructor(accepted); ChangeOf(file); ChangeOf(project, symbol); PreviousAccessibility; ChangedFiles Compare current source against explicitly supplied accepted analyses, not Git history."
  },
  {
    "title": "SourceChange · API field guide",
    "url": "reference.html#api-sourcechange",
    "text": "Unchanged; Added; Modified No removed-file candidate; renames count as additions."
  },
  {
    "title": "Configuration · API field guide",
    "url": "reference.html#configuration",
    "text": "DrillPress.Configuration · Read the guide →"
  },
  {
    "title": "ApiSet · API field guide",
    "url": "reference.html#api-apiset",
    "text": "constructor(params members); Contains(methodSymbol) Immutable collection of method identities."
  },
  {
    "title": "PathPattern · API field guide",
    "url": "reference.html#api-pathpattern",
    "text": "constructor(pattern); Matches(path) Case-sensitive full-path glob with slash normalization. Supports *, ?, **, and **/."
  },
  {
    "title": "Fix proposals · API field guide",
    "url": "reference.html#corrections",
    "text": "DrillPress.Fixes · Read the guide →"
  },
  {
    "title": "EmptyStringFix · API field guide",
    "url": "reference.html#api-emptystringfix",
    "text": "Create(memberReference) Conservative string.Empty replacement."
  },
  {
    "title": "OrdinalComparerFix · API field guide",
    "url": "reference.html#api-ordinalcomparerfix",
    "text": "IsArgument; Create(memberReference) Argument condition and restricted Distinct<string> correction."
  },
  {
    "title": "ModifierFix · API field guide",
    "url": "reference.html#api-modifierfix",
    "text": "RemoveRedundantAccessibility(memberDeclarationNode) Remove a sole redundant internal/private token where effective accessibility stays the same."
  },
  {
    "title": "SourceChanges · API field guide",
    "url": "reference.html#api-sourcechanges",
    "text": "Replace(source, span, replacement); Propose(edits, preservesBehavior) Exact edits and a complete proposal with required contextual semantic proof."
  },
  {
    "title": "FixProposal · API field guide",
    "url": "reference.html#api-fixproposal",
    "text": "constructor(edits, validate); Edits; IsSafeIn(project) Complete batch and per-context safety validation."
  },
  {
    "title": "RewriteContext · API field guide",
    "url": "reference.html#api-rewritecontext",
    "text": "Original; Rewritten; Edits Original project, locally rewritten compilation, and entire edit batch."
  },
  {
    "title": "BindingProof · API field guide",
    "url": "reference.html#api-bindingproof",
    "text": "PreservesEnclosingExpressions(source, replaced, replacement) Binding/type/conversion check, not a general behavioral-equivalence proof."
  },
  {
    "title": "SourceEdit (DrillPress.Manifest) · API field guide",
    "url": "reference.html#api-sourceedit-drillpress-manifest-",
    "text": "FileIdentity; Fingerprint; Start; Length; OriginalText; Replacement Low-level immutable edit data. Prefer SourceChanges.Replace to construct it."
  },
  {
    "title": "Testing and hosting · API field guide",
    "url": "reference.html#tests",
    "text": "DrillPress.Testing · DrillPress.Engine · Read the guide →"
  },
  {
    "title": "XunitTests · API field guide",
    "url": "reference.html#api-xunittests",
    "text": "AreTests; Methods Resolved Fact/Theory selection, including derived xUnit attributes."
  },
  {
    "title": "TestBody / TestAssertion · API field guide",
    "url": "reference.html#api-testbody-testassertion",
    "text": "EmptyLines; Assertions; EarlyAssertion / MemberName; Location Physical block-body layout facts and resolved assertion locations."
  },
  {
    "title": "RuleTestWorkspace · API field guide",
    "url": "reference.html#api-ruletestworkspace",
    "text": "constructor(references?); AddProject; Analyze; CheckAsync In-memory source fixtures; CheckAsync includes production validation."
  },
  {
    "title": "TestSource / TestFinding · API field guide",
    "url": "reference.html#api-testsource-testfinding",
    "text": "Path, Text, Generated / Rule, Path, Line, Column, Text, HasFix Synthetic input and precise validated output."
  },
  {
    "title": "RuleTestResult · API field guide",
    "url": "reference.html#api-ruletestresult",
    "text": "Findings; FixedText(path) Validated findings and in-memory after-fix source."
  },
  {
    "title": "RuleApplication · API field guide",
    "url": "reference.html#api-ruleapplication",
    "text": "constructor(); RunAsync(rules, args, standardOutput?, standardError?, cancellationToken?) Executable bundle host. Return its exit code from Program; use the CLI to load real projects."
  },
  {
    "title": "RuleSet.Evaluate vs AnalysisEngine · API field guide",
    "url": "reference.html#api-ruleset-evaluate-vs-analysisengine",
    "text": "Evaluate / AnalyzeAsync; Reconstruct; EvaluateAsync Use Evaluate to collect diagnostics directly, RuleApplication to host a rule bundle, and RuleTestWorkspace to test rules against small source examples."
  },
  {
    "title": "When you need the compiler directly · API field guide",
    "url": "reference.html#compiler",
    "text": "Microsoft.CodeAnalysis contains symbols, operations, and compiler result types. Microsoft.CodeAnalysis.CSharp.Syntax contains C# syntax node types. Microsoft.CodeAnalysis.Operations contains typed operation interfaces; Microsoft.CodeAnalysis.Text.TextSpan describes a character range. Use the existing Source.Model , Source.Tree , and Project.Compilation rather than making a second compiler view of the same source. Compare symbols using compiler identity, not display strings. Keep unavailable or ambiguous facts distinct from a confirmed policy violation."
  }
];
