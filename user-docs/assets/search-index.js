window.DRILLPRESS_SEARCH = [
  {
    "title": "The rule author’s manual",
    "url": "index.html",
    "text": "A small language for your team’s conventions. Turn a convention your team keeps explaining into a small, repeatable check. Drill Press rules are ordinary C#: you name the convention, choose the code it governs, and say what must or must not be true. Write your first rule →Explore the API field guide"
  },
  {
    "title": "Choose your next step · The rule author’s manual",
    "url": "index.html#choose",
    "text": "01 / START SMALL A working first rule Create a bundle, run it against a project, and understand its output. 02 / SAY WHAT YOU MEAN Readable declarations Selections, requirements, exceptions, and named conditions. 03 / LOOK DEEPER Calls and compiler facts Overloads, arguments, nullable values, and call paths. 04 / BUILD CONFIDENCE Test before you share Small source examples, precise findings, and safe-fix checks."
  },
  {
    "title": "Three ideas are enough to start · The rule author’s manual",
    "url": "index.html#mental-model",
    "text": "A rule names a convention. rules.Rule(id, message) gives it a stable identifier and tells the reader what to do next. A query chooses candidates. Every query starts from Code: files, types, methods, calls, fields, catch clauses and more. Where and the named filters narrow it. Each candidate knows where to report. A verdict closes the rule. Forbid() reports everything selected. Require(condition) reports only the candidates that fail it. Both accept an optional safe fix."
  },
  {
    "title": "You do not need to know Roslyn first · The rule author’s manual",
    "url": "index.html#knowledge",
    "text": "You should be comfortable with C# methods, lambdas, and basic LINQ. Roslyn is the C# compiler’s API: it lets a rule distinguish a real framework call from an unrelated method with the same name. Start with Drill Press’s wrappers. Working with a coding agent? Install the bundled rule-authoring skill; it carries this manual’s essentials and compiled examples, so the agent needs no online documentation."
  },
  {
    "title": "Write your first rule",
    "url": "first-rule.html",
    "text": "From a C# project to your first useful finding. We’ll prevent direct console logging in production code. The first version only reports violations; it does not change source files."
  },
  {
    "title": "1. Create a rule bundle · Write your first rule",
    "url": "first-rule.html#project",
    "text": "Install a .NET 10 SDK. For package installation outside this checkout, see the distribution guide. The example below uses project references for source development. Register your rules explicitly in a console application; this application becomes the rule bundle. Create samples/MyRules/MyRules.csproj with the following content. These relative references assume that location inside the Drill Press checkout. samples/MyRules/MyRules.csproj <Project Sdk=\"Microsoft.NET.Sdk\"> <PropertyGroup> <OutputType>Exe</OutputType> <TargetFramework>net10.0</TargetFramework> <ImplicitUsings>enable</ImplicitUsings> <Nullable>enable</Nullable> </PropertyGroup> <ItemGroup> <ProjectReference Include=\"../../src/DrillPress.RuleAuthoring/DrillPress.RuleAuthoring.csproj\" /> <ProjectReference Include=\"../../src/DrillPress.Engine/DrillPress.Engine.csproj\" /> </ItemGroup> </Project> The authoring library supplies the building blocks. The engine supplies RuleApplication, which hosts your compiled rules so the CLI can run them. You do not need NativeAOT to get started."
  },
  {
    "title": "2. Declare and host the rule · Write your first rule",
    "url": "first-rule.html#declaration",
    "text": "samples/MyRules/Program.cs using DrillPress; using DrillPress.Engine; var rules = new RuleCatalog(); var writeLine = CodeType.Named(\"System.Console\").Member(\"WriteLine\"); rules.Rule(\"TEAM001\", \"Use the application logger instead of Console.WriteLine.\") .For(Code.Calls.To(writeLine).InNonTestProjects()) .Forbid(); return (int)await new RuleApplication().RunAsync(rules, args); TEAM001 is your stable rule identifier. Choose a unique identifier for every registered rule. The message should tell someone what to do next—not just say “bad code.” Both must be nonblank and single-line. A rule must end in Forbid or Require; evaluation stops with an error that names an unfinished rule."
  },
  {
    "title": "3. Build and check a target · Write your first rule",
    "url": "first-rule.html#run",
    "text": "Create a tiny target in samples/MyRuleTarget. Its console call is an intentional violation. samples/MyRuleTarget/MyRuleTarget.csproj <Project Sdk=\"Microsoft.NET.Sdk\"> <PropertyGroup> <TargetFramework>net10.0</TargetFramework> </PropertyGroup> </Project> samples/MyRuleTarget/Worker.cs public class Worker { public void Run() => System.Console.WriteLine(\"starting\"); } Run these commands from the repository root: Terminal · repository root dotnet build DrillPress.slnx dotnet build samples/MyRules/MyRules.csproj dotnet restore samples/MyRuleTarget/MyRuleTarget.csproj dotnet src/DrillPress.Cli/bin/Debug/net10.0/DrillPress.Cli.dll check --build-host src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll --rules samples/MyRules/bin/Debug/net10.0/MyRules.dll samples/MyRuleTarget/MyRuleTarget.csproj Replace the last path to check your own project or solution. Quote paths with spaces. The target must be restored first; generated assemblies needed by the target must also be built. The target’s own global.json controls its SDK selection. The bundle is not the project loader Run the CLI with --rules, not your rule DLL with a project path. The CLI loads the project and passes a captured compilation to your bundle. Direct bundle output is an internal protocol, not the human-readable findings."
  },
  {
    "title": "4. Read the result · Write your first rule",
    "url": "first-rule.html#output",
    "text": "For the target above, the finding points at the name of the called method, WriteLine: Expected CLI output TEAM001 Use the application logger instead of Console.WriteLine. samples/MyRuleTarget/Worker.cs 3:41 Output groups findings by rule and file, followed by physical line/column locations. A clean run prints nothing. Exit codes are 0 for clean, 1 for findings, and 2 when the check could not finish. A + before a location means a proposed fix survived validation. This first rule offers none. Only the CLI’s fix command applies changes; check never does."
  },
  {
    "title": "5. Keep a growing bundle understandable · Write your first rule",
    "url": "first-rule.html#organize",
    "text": "Move registrations into a class such as LoggingRules with a Register(RuleCatalog rules) method. Put reusable selections and named predicates next to their domain rules. Keep compiler details in small helpers, not inside a long registration expression. Register every rule explicitly before calling RuleApplication.RunAsync. This keeps the bundle easy to inspect and avoids runtime assembly scanning. Next, write a small test before adding more policies."
  },
  {
    "title": "Select and compose",
    "url": "selections.html",
    "text": "Choose the code you mean, then say what it must do. A good declaration separates where the policy applies from what it requires. Keep these decisions visible in the code."
  },
  {
    "title": "Start with the smallest useful selection · Select and compose",
    "url": "selections.html#roots",
    "text": "Every selection starts from Code. Pick the root that names the thing your convention is about; its candidates already know their source, project and diagnostic location. Start here You get Code.Files / Code.Projects Ordinary source files / evaluated project contexts. FilesIncludingGenerated adds generated files. Code.Types / Code.Interfaces Named type definitions; partial types appear once per evaluated project context. Code.TypeDeclarations Each written type part, including every partial part. Code.Methods, Fields, Properties, Parameters Member declarations with typed facts. Each field variable is its own candidate. Code.Declarations Every other declared symbol: events, locals, accessors and more. Code.TestMethods / TestClasses xUnit, NUnit and MSTest tests and the concrete classes that contain them. Code.Calls / ObjectCreations Bound method calls / new expressions, by compiler identity. Code.MemberReferences / TypeReferences Source references to members / every written use of a type. Code.IfStatements, Catches, LocalVariables, Enumerations, NullChecks, Comments Statement, handler, declaration and comment shapes. Code.Nodes<TSyntax>() / Code.Operations<TOperation>() Any Roslyn syntax or operation kind when no named root fits. Everything lives in the DrillPress namespace. See the field guide for each candidate’s members."
  },
  {
    "title": "Forbid or require? · Select and compose",
    "url": "selections.html#require",
    "text": "Require a naming convention for async methods using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"TEAM002\", \"Give asynchronous methods an Async suffix.\") .For(Code.Methods.Where(method => method.IsAsync)) .Require(method => method.NameEndsWith(\"Async\")); This selects async methods, then reports the ones whose names lack the suffix. Forbid() instead reports every candidate already selected. Put the scope in the selection and the obligation in Require; the same check can usually be written either way, but this split keeps the rule easy to read."
  },
  {
    "title": "Filter by name, place, and shape · Select and compose",
    "url": "selections.html#filters",
    "text": "Declarations (types, type parts, methods, fields, properties, parameters and declared symbols) share one set of filters, so a convention reads the same whatever it governs. Every reportable candidate also shares the place filters. Shared filters using DrillPress; using Microsoft.CodeAnalysis; var rules = new RuleCatalog(); var handler = CodeType.Named(\"Shop.Messaging.HandlerAttribute\"); rules.Rule(\"API001\", \"Document public API.\") .For(Code.Methods .InProject(\"Shop.*\") .InNamespace(\"Shop.Api.**\") .WithAccessibility(Accessibility.Public)) .Require(method => method.HasDocumentationComment()); rules.Rule(\"MSG001\", \"Name handlers after the message they handle.\") .For(Code.Types.WithAttribute(handler).InNonTestProjects()) .Require(type => type.NameEndsWith(\"Handler\")); rules.Rule(\"FIELD001\", \"Make private instance fields readonly when possible.\") .For(Code.Fields .WithAccessibility(Accessibility.Private) .Where(field => !field.IsStatic && !field.IsConst)) .Require(field => field.IsReadOnly); Filter Meaning Named(...), NameMatching(\"*Service\"), WithNameContainingAnyWord(...) Exact names, globs, or whole camel-case words. WithAttribute(type), WithAccessibility(...), WithExplicitModifier(Modifier.Internal) Attributes (including derived classes), effective accessibility, or a modifier written in source. InProject(glob), InTestProjects(), InNonTestProjects() Project scope; test projects are classified by the build, not by name. InNamespace(\"A.B.**\"), InFolder(\"Domain\"), InFilesNamed(\"*"
  },
  {
    "title": "Give typed clauses one rule identity · Select and compose",
    "url": "selections.html#shared-identity",
    "text": "One rule can hold several clauses when a policy covers different candidate types. Calls can require execution while loops require enumeration advancement under the same ID and remediation. Compose call and loop requirements using DrillPress; var rules = new RuleCatalog(); var policy = rules.Rule(\"DATA002\", \"Exercise each data query.\"); policy.For(Code.Calls.ToMethodsNamed(\"ReadQuery\")) .Require(Coverage.Executed); policy.For(Code.Enumerations) .Require(Coverage.EnumerationStarted); Apply your own source filters to both selections. The rule reserves its ID immediately; no other rule can reuse it. Each clause keeps its typed evidence and outcome policy. ReportOncePer groups only its own clause, and every fix still passes conflict and combined validation. See shared-rule reporting and fix behavior."
  },
  {
    "title": "Name conditions and exceptions · Select and compose",
    "url": "selections.html#conditions",
    "text": "Compose a convention and its exception using DrillPress; var rules = new RuleCatalog(); var asynchronous = new RuleCondition<CodeMethod>(method => method.IsAsync); var eventHandler = new RuleCondition<CodeMethod>(method => method.HasAttribute(CodeType.Named(\"Product.EventHandlerAttribute\"))); rules.Rule(\"TEAM003\", \"Give asynchronous application methods an Async suffix.\") .For(Code.Methods.InNonTestProjects().Where(asynchronous.ExceptWhen(eventHandler))) .Require(method => method.NameEndsWith(\"Async\")); And, Or, Not, and condition-level ExceptWhen compose Boolean tests with short-circuit behavior. Query-level ExceptWhen excludes matches. Both Where and Require also accept ordinary lambdas."
  },
  {
    "title": "Choose where the finding points · Select and compose",
    "url": "selections.html#report-at",
    "text": "Each candidate reports at its natural location: a declaration at its identifier, a call at its method name (ThenBy in items.OrderBy(a).ThenBy(b)), a catch clause at its header. ReportAt moves the finding to a more precise part, or anchors a computed value to source. Report at a part of the candidate using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"ASYNC001\", \"Return Task from async methods instead of void.\") .For(Code.Methods.Where(method => method.IsAsync && method.ReturnsVoid)) .ReportAt(method => method.Syntax.ReturnType) .Forbid(); rules.Rule(\"PARAM001\", \"Group long parameter lists into a request type.\") .For(Code.Methods .Select(method => (Method: method, Count: method.Parameters.Count)) .Where(item => item.Count > 5)) .ReportAt(item => item.Method) .Forbid(); ReportAt accepts another candidate, a syntax node, a token or a SourceLocation; returning null keeps the default. Tuples, projects and other computed values have no location of their own, so they need ReportAt before Forbid or Require. A tuple finds syntax, tokens and locations in the compilation of its first code element. A record that does not implement ICodeElement has no compilation, so registering it without ReportAt(candidate => element) fails immediately."
  },
  {
    "title": "Transform and connect selections · Select and compose",
    "url": "selections.html#composition",
    "text": "CodeQuery<T> is the reusable selection type. It resembles LINQ, but it belongs to an analysis rather than an arbitrary list. Operation Use it when Select Each candidate becomes another value. SelectMany Each candidate has several children to inspect. Concat / Union Several selections share one rule. Join You need matching pairs, using explicit keys and an optional equality comparer. WithoutMatching You need owners missing a counterpart. Supply keys or a predicate comparing the two candidates. Create / In(solution) You need a custom query, or need to read a query in a supplied analysis. See counterpart matching and custom queries for complete examples."
  },
  {
    "title": "Reuse queries; keep predicates pure · Select and compose",
    "url": "selections.html#reuse",
    "text": "Drill Press materializes a query once per query instance and analysis. Reuse the instance to share work. Do not increment counters, write files, log to standard output, or depend on evaluation order in predicates. A new analysis gets fresh caches. Place filters run after candidates are found. When a policy covers a small part of a large solution, start narrow, as in Code.Files.InFolder(\"Domain\").Calls(), so the compiler binds only the calls you need. Generated files supply compiler information but ordinary roots exclude them; see generated and linked source."
  },
  {
    "title": "Types and members",
    "url": "identities.html",
    "text": "Match what code refers to—not how someone spelled it. Code can spell the same operation in different ways: a fully qualified name, a using alias, or a static import. Semantic identity means matching the actual type or member chosen by the compiler."
  },
  {
    "title": "Describe a type · Types and members",
    "url": "identities.html#types",
    "text": "Types you know—and types your rule project does not reference using DrillPress; var text = CodeType.Of<string>(); var textLists = CodeType.Of<List<string>>(); var console = CodeType.Named(\"System.Console\"); var contract = CodeType.Named(\"Product.IStorage\", \"Product.Contracts\"); Of<T>() records a type your rule project can reference, including exact generic arguments. Named describes a target type by its namespace-qualified name, without taking a project dependency on it. The optional second argument restricts the declaring assembly; omitting it permits any assembly. CodeType exposes MetadataName, AssemblyName, and TypeArguments. These describe compiler identity, not a display name. Use Matches to test a compiler type; record equality is not a substitute for its optional-assembly and framework-facade matching rules."
  },
  {
    "title": "Open and constructed generics · Types and members",
    "url": "identities.html#generics",
    "text": "Empty slots match any constructed arguments using DrillPress; var anyPool = CodeType.Named(\"System.Buffers.ArrayPool<>\"); var dictionaries = CodeType.Named(\"System.Collections.Generic.Dictionary<,>\"); var nested = CodeType.Named(\"Product.Outer<>.Inner<,>\"); var listArrays = CodeType.Named(\"System.Collections.Generic.List<>[]\"); var onlyStringLists = CodeType.Of<List<string>>(); var onlyTwoDimensionalArrays = CodeType.Of<List<string>[,]>(); <> means one generic parameter; <,> means two. Empty slots normalize to the compiler’s metadata representation. Keep array suffixes such as [] or [,]. After an open generic type, use dots for nested types: Product.Outer<>.Inner<,> selects Inner<U, V> declared inside Outer<T>, regardless of their type arguments. This is not Outer<Inner<,>>, which puts a type argument inside Outer. Existing metadata names using + still work. If the first containing type is not generic, use + to distinguish it from a namespace, as in Product.Container+Item<>. Names are not C# type expressions Named(\"List<string>\") and Named(\"List<T>\") are not accepted. Use a fully qualified open name, or Of<List<string>>() for an exact constructed type."
  },
  {
    "title": "Build members from their declaring type · Types and members",
    "url": "identities.html#members",
    "text": "A family, a parameterless overload, and an exact overload using DrillPress; var console = CodeType.Named(\"System.Console\"); var allWriteLines = console.Member(\"WriteLine\"); var blankLine = allWriteLines.WithParameters(); var textLine = allWriteLines.WithParameters(CodeType.Of<string>()); var trim = CodeType.Of<string>().Member(nameof(string.Trim), []); var emptyStringReferences = CodeType.Of<string>().Member(nameof(string.Empty)).References; Omitting parameters matches every method overload. Passing an empty list—or calling WithParameters()—matches only a parameterless method. A nonempty list matches the parameter types in order. WithParameters creates a new descriptor; it does not narrow the original one. CodeMember also has a constructor taking declaring type, name, and optional parameters. Its DeclaringType and Name are readable properties. Matches accepts compiler methods, fields, or properties; a parameter-constrained descriptor matches methods only. Reduced extension methods are compared with their original static declaration, so an exact parameter list includes the extension receiver."
  },
  {
    "title": "References, calls, or creations? · Types and members",
    "url": "identities.html#references",
    "text": "Choose the selection that matches the use your convention cares about. Each one identifies the member or type by compiler identity. Selection Covers Code.Calls.To(member) Invocations of the method family or overload, in any spelling: instance, static, extension, or static import. Each call exposes Target, Receiver, Arguments and Argument(\"name\"). member.References Named source expressions referring to a field, property, or method, including method groups and nameof. Add .OutsideNameOf() to skip compiler-only uses. Code.ObjectCreations.Of<T>() new T(...) and target-typed new(...), with the chosen constructor and parameter-mapped arguments. type.References / Code.TypeReferences Every written use of a type: declarations, parameters, base lists, generic arguments, casts, typeof, attributes and static qualifiers. A MemberReference exposes ContainingType, MemberName, Symbol, Expression and Facts. Implicit uses, such as a property read inside a compiler-generated deconstruction, belong to operation queries. Keep the domain independent of infrastructure using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"ARCH001\", \"Keep the domain independent of infrastructure.\") .For(Code.TypeReferences .InNamespace(\"Shop.Domain.**\") .Where(reference => reference.Type.IsInNamespace(\"Shop.Infrastructure.**\"))) .Forbid(); rules.Rule(\"TIME001\", \"Inject TimeProvider instead of creating timers.\") .For(Code.ObjectCreations.Of<System.Threading.Timer>().InNonTestProjects()) .Forbid(); A qualified n"
  },
  {
    "title": "Configure a family of restricted APIs · Types and members",
    "url": "identities.html#sets",
    "text": "Keep non-repeatable values out of production code using DrillPress; var rules = new RuleCatalog(); var randomValues = new ApiSet( CodeType.Of<Guid>().Member(nameof(Guid.NewGuid)), CodeType.Of<Random>().Member(nameof(Random.Next))); rules.Rule(\"TEAM004\", \"Pass reproducible values into the application core.\") .For(Code.Calls.To(randomValues).InNamespace(\"Shop.Core.**\")) .Forbid(); To accepts a single member or an ApiSet; ApiSet.Contains tests one compiler method directly. Scope this policy to the deterministic part of your application; randomness is appropriate in many other places. When the declaring type does not matter, name-based call filters are available: ToMethodsNamed(\"Commit\"), ToMethodsMatching(\"*Async\"), ToMethodsDeclaredOn(type), ToMethodsDeclaredOnOrDerivedFrom(type) and OnReceiverOfType<T>()."
  },
  {
    "title": "Attributes, inheritance, and symbols · Types and members",
    "url": "identities.html#attributes",
    "text": "declaration.HasAttribute(type) and WithAttribute(type) inspect resolved attributes on any declaration, including derived attribute classes. type.Implements(contract) follows inherited interfaces, not just the names written beside the class; DerivesFrom and IsOrDerivesFrom follow base classes. Code.Types.DerivedFrom(type) and ImplementingInterface(type) are the query forms. For raw compiler symbols, Symbols.HasAttribute can include or exclude derived attribute types; Symbols.IsOrDerivesFrom follows base classes; Symbols.Implements follows interfaces. symbol.Attributes() wraps attribute data with typed constructor and named values. Unresolved types are not guessed."
  },
  {
    "title": "Inspect source code",
    "url": "syntax.html",
    "text": "Files, declarations, syntax, and precise locations. Use the named roots and declaration wrappers first. Reach for syntax when your convention is about an exact piece of source: a modifier, a switch expression, or a particular literal."
  },
  {
    "title": "Files and path patterns · Inspect source code",
    "url": "syntax.html#files",
    "text": "Keep console output inside a tracing adapter using DrillPress; var rules = new RuleCatalog(); var writeLine = CodeType.Named(\"System.Console\").Member(\"WriteLine\"); rules.Rule(\"TEAM005\", \"Keep direct console output inside the Tracing adapter.\") .For(Code.Calls.To(writeLine).InNonTestProjects()) .Require(call => call.IsInFolder(\"Tracing\")); InFolder(\"Tracing\") and IsInFolder match whole folder segments at any depth below the project. InFilesNamed(\"*Tests.cs\") matches file names. CodeFile provides a slash-normalized Path, Name, Folder, Source, and a location at the start of the file. For other path policies, PathPattern.Matches compares an entire path, case-sensitively, without accessing the filesystem. * stays within one path segment; ? matches one character; ** crosses directories; **/ also matches no directory."
  },
  {
    "title": "A small compiler vocabulary · Inspect source code",
    "url": "syntax.html#vocabulary",
    "text": "Syntax node A structured piece of the written code, such as a class declaration or a string literal. It is useful even when the code does not compile. Token A small language element such as an identifier, keyword, or punctuation mark. Tokens carry exact source spans. Trivia Whitespace, comments, and directives attached to tokens. The name does not mean it is unimportant—formatting rules need it. Symbol The resolved identity of a declaration: which method, type, field, or parameter a piece of code means. Semantic model The compiler’s bridge from written syntax to symbols, types, and other meaning."
  },
  {
    "title": "Statement and declaration shapes · Inspect source code",
    "url": "syntax.html#shapes",
    "text": "Common shapes have named roots, so you do not need to enumerate every syntax spelling yourself. Braces, empty handlers, and explicit types using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"STYLE001\", \"Add braces to if and else branches.\") .For(Code.IfStatements.Branches().WithoutBraces()) .Forbid(); rules.Rule(\"ERR001\", \"Handle, log, or rethrow caught exceptions.\") .For(Code.Catches.Where(handler => handler.IsEmpty)) .Forbid(); rules.Rule(\"STYLE002\", \"Use var when the type is apparent.\") .For(Code.LocalVariables.WithExplicitType().WithInitializer().WhereVarPreservesType()) .ReportAt(declaration => declaration.TypeName) .Forbid(); CodeIfStatement exposes Then, Else and Branches; WithoutBraces() skips blocks and else if continuations, which are not missing braces. InconsistentlyBracedBranch finds the unbraced side of an if/else pair where only one side has braces. CodeCatch exposes ExceptionType, Catches<T>(), CatchesAnyException, Filter, Body, IsEmpty and Rethrows. CodeVariableDeclaration covers local, for, using, foreach and out declarations through LocalVariables, ForEachLoops and OutVariables."
  },
  {
    "title": "Choose a syntax kind · Inspect source code",
    "url": "syntax.html#nodes",
    "text": "Find repeated format-dispatch expressions using DrillPress; using Microsoft.CodeAnalysis.CSharp.Syntax; var rules = new RuleCatalog(); rules.Rule(\"TEAM006\", \"Share the repeated format-dispatch expression.\") .For(Code.Nodes<SwitchExpressionSyntax>().InNonTestProjects().Duplicates(minimumTokens: 20)) .Forbid(); SwitchExpressionSyntax represents an expression such as format switch { ... }. Other useful types in Microsoft.CodeAnalysis.CSharp.Syntax include LiteralExpressionSyntax, AttributeSyntax, TypeDeclarationSyntax, and MemberDeclarationSyntax. files.Nodes<T>() restricts the search to a file selection. Each CodeNode<T> has Syntax, Source, and Location, plus ContainingSymbol, Operation, TypeInfo, and Constant. A constant result distinguishes “no constant value” from a constant whose value is null: check HasValue before reading Value. node.AsExpression() turns an expression node into a CodeExpression."
  },
  {
    "title": "Methods, types, and all other declarations · Inspect source code",
    "url": "syntax.html#declarations",
    "text": "CodeMethod exposes Name, IsAsync, IsStatic, ReturnsVoid, ReturnTypeIs, Parameters, HasEmptyBody, Body(), ContainingType, Flow, Reaches, Syntax and Symbol. The symbol can be null when binding fails. CodeTypeDefinition is a named type with Namespace, kind tests such as IsClass, IsRecord and IsStatic, and Implements/DerivesFrom. Code.TypeDeclarations yields each written part instead; TopLevel() keeps those outside other types. CodeField, CodeProperty and CodeParameter expose Type, TypeIs<T>() and their initializer or default value. Code.EnumMembers selects enum members, which Code.Fields leaves out, with their Value and HasExplicitValue. Code.Declarations covers every other declared symbol—events, locals, accessors—as CodeSymbol. Code.ReferencesTo(symbol) follows compiler identity or matching loaded source declarations across compilations. Neither guesses ambiguous bindings; implicit uses belong to operation queries."
  },
  {
    "title": "Sections, scopes and explanatory comments · Inspect source code",
    "url": "syntax.html#bodies",
    "text": "Explain a null-forgiving operator; assert only at the end of a test. ExplanatoryComment(position) on a source, or element.ExplanatoryComment() and HasExplanatoryComment(), find the ordinary // or /* */ comment on the line, on the comment line directly above it, or directly above the statement or member it belongs to. Documentation comments and drillpress-allow comments do not explain code. body.Sections() splits a block body at whitespace-only lines between statements, like the Arrange, Act and Assert sections of a test. Each CodeSection has its Statements, Index, Calls() and Nodes<T>(). CodeBody.Of(source, node) scopes Calls(), Nodes<T>() and the other body queries to any syntax node, such as a loop body."
  },
  {
    "title": "Report the smallest useful location · Inspect source code",
    "url": "syntax.html#locations",
    "text": "Report the internal keyword, not the entire type using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"TEAM007\", \"Use implicit assembly visibility for top-level types.\") .For(Code.TypeDeclarations.TopLevel().WithExplicitModifier(Modifier.Internal)) .ReportAt(type => type.ExplicitModifier(Modifier.Internal)) .Forbid(); Declarations report at their identifier by default. ReportAt accepts a candidate part (such as an argument or branch), a syntax node, a token, or a SourceLocation. AnalysisSource.Locate(TextSpan) converts an exact character range into a SourceLocation: file path, zero-based Start, Length, and one-based Line/Column. Columns use UTF-16 character positions; physical positions ignore #line remapping. Locations and fix factories run only for violating candidates. A custom candidate type can implement ICodeElement with Source and Location, or use ReportAt to anchor itself to an existing candidate."
  },
  {
    "title": "Generated and linked source · Inspect source code",
    "url": "syntax.html#generated",
    "text": "Ordinary roots exclude generated files. Code.FilesIncludingGenerated lets advanced facts inspect them, but the evaluator still suppresses findings anchored there, including findings that ReportAt moves into a generated file. Linked files and alternative target frameworks remain separate compilation memberships; do not collapse them just because the path is the same. AnalysisSource.Document exposes captured text, path, generated classification, and edit eligibility. Tree is the original syntax tree; Model is the shared semantic model; Project owns that compilation context. Prefer these captured values to opening source files yourself."
  },
  {
    "title": "Calls and flow",
    "url": "analysis.html",
    "text": "Ask what a call does and what the compiler knows. A method name alone does not tell you which overload is called, how named arguments map to parameters, or whether a value may be null. These APIs use the compiler’s answers."
  },
  {
    "title": "Inspect calls and arguments · Calls and flow",
    "url": "analysis.html#invocations",
    "text": "Disallow an infinite timeout on a specific API using DrillPress; var rules = new RuleCatalog(); var send = CodeType.Named(\"Product.Transport\").Member(\"Send\"); rules.Rule(\"TEAM008\", \"Pass a bounded timeout to Transport.Send.\") .For(Code.Calls.To(send) .WhereArgument(\"timeoutMilliseconds\", argument => argument.Is(-1))) .Forbid(); CodeInvocation.Target is the chosen method overload; To and Calls compare it to your CodeMember whichever way the call is spelled. Argument(\"name\") returns the argument bound to that declared parameter, including an omitted optional default; it returns null for an unknown parameter. A named argument appearing first in the source is not necessarily the first declared parameter, and parameter names keep working when callers reorder them. Member Meaning argument.Is(value), IsOmittedOr(value), ValueAs<T>(), TextValue Compiler constant values, including defaults supplied by the compiler. argument.IsExplicit, Value, Location Written arguments have a source expression and location; findings on omitted defaults report at the call. call.Receiver, WhereReceiver(...), OnReceiverOfType<T>() The object before the dot, including an extension method’s first argument. calls.ArgumentsFor(\"name\"), .SourceValues() Turn calls into argument or written-value candidates that report at the argument. When you know the target, narrow a call query with To, ToMethodsNamed or ToMethodsMatching, before or after any Where. Drill Press then binds only the files that can contain such a call; Code.Calls.Where(...) alone has to bind every file in the solution. Place filters such as InTestProjects() or InFolder(...) likewise skip other files before binding, for calls, operations, member and type references and object creations."
  },
  {
    "title": "Follow a fluent chain · Calls and flow",
    "url": "analysis.html#chains",
    "text": "Require an ordering before a query is paged using DrillPress; var rules = new RuleCatalog(); var skip = CodeType.Framework(\"System.Linq.Queryable\").Member(\"Skip\"); rules.Rule(\"DATA003\", \"Order a query before paging it with Skip.\") .For(Code.Calls.To(skip).Where(call => !call.Chain() .ThroughLocal() .SelectMany(chain => chain.Calls) .Any(other => other.TargetNameMatches(\"OrderBy*\")))) .Forbid(); call.Chain() returns the calls that each receive the previous call’s result, in evaluation order. Before(call) and After(call) split it at a call, and Root is the value it starts from. Extension methods link in both spellings, and conditional access links too; an await ends the chain. When a chain starts from a local (chain.Local), ThroughLocal() adds the chains assigned to that local and the chains that start from it anywhere in the same body, including builders that change in place, in source order."
  },
  {
    "title": "Find the values in scope · Calls and flow",
    "url": "analysis.html#scope",
    "text": "Pass on the cancellation token a call can reach. element.ValuesInScope<T>() lists the locals, parameters, fields and properties of a type (or a derived or implementing type) that code at the element can read by name, nearest first: locals declared before it, the parameters of enclosing lambdas, local functions and the method, primary constructor parameters, then members by name. Each ScopedValue has its Symbol, Name, Kind and declared Type, so a fix can name the one injected clock, and a rule can ignore a TimeProvider?. Instance state is left out in a static context, the locals and parameters around a static lambda or local function are left out inside it, and an outer type's instance state is left out in a nested type."
  },
  {
    "title": "Look beyond ordinary calls · Calls and flow",
    "url": "analysis.html#operations",
    "text": "An operation describes what bound code does. A new expression is an object-creation operation; a method call is an invocation operation. Operations can include things the compiler adds implicitly, such as conversions. Object creation has its own root: Flag a legacy serializer being constructed using DrillPress; using Microsoft.CodeAnalysis.Operations; var rules = new RuleCatalog(); var legacy = CodeType.Named(\"Product.LegacySerializer\"); rules.Rule(\"TEAM009\", \"Create the supported serializer instead.\") .For(Code.ObjectCreations.Of(legacy)) .Forbid(); rules.Rule(\"TEAM012\", \"Avoid lock statements; use the async coordinator.\") .For(Code.Operations<ILockOperation>()) .Forbid(); Code.Operations<T>() covers any operation kind, and files.Operations<T>() restricts it to a file selection. Discovery covers bodies, accessors, initializers, attributes, top-level code, and nested functions—not just methods selected by Code.Methods. CodeOperation<T> gives you the typed Operation, ContainingSymbol when resolvable, Source, and Location. Implicit operations may share a source span. Invalid operations can remain visible; check the facts your conclusion depends on."
  },
  {
    "title": "Follow a call path · Calls and flow",
    "url": "analysis.html#paths",
    "text": "Find blocking sleeps beneath async entry points using DrillPress; var rules = new RuleCatalog(); var sleep = CodeType.Named(\"System.Threading.Thread\").Member(\"Sleep\"); rules.Rule(\"TEAM010\", \"Use an awaited delay along asynchronous call paths.\") .For(Code.Methods.Where(method => method.IsAsync && method.Reaches(sleep))) .Forbid(); method.Reaches(member) follows statically bound calls through loaded source, including helpers in referenced projects and generated implementations. It terminates cycles. It does not infer reflection, runtime interface dispatch, dependency injection, or delegate dispatch into a lambda. An uncalled local function is not attributed to its enclosing method. method.Callees() lists the source methods a method calls, in the order its calls appear; Callees(sameType: true, transitive: true) gives every method of its own type that it runs, directly or through others, such as the loop methods a BackgroundService.ExecuteAsync runs. Calls in lambdas count unless you pass nested: NestedFunctions.Exclude. “Not found” is not a proof of safety A negative result only says the modeled source call paths did not reach that target. It does not prove that runtime execution cannot call it."
  },
  {
    "title": "Use method-local flow facts · Calls and flow",
    "url": "analysis.html#flow",
    "text": "method.Flow returns shared MethodFlow analysis. The older MethodFlow.For(solution, method) entry point shares that cache; constructing new MethodFlow(method) creates independent lazy analysis. Data Reads, writes, captured variables, and other compiler data-flow sets. Check Succeeded before trusting them. A captured variable is one referenced by a nested lambda or local function. Graph A control-flow graph: blocks of operations connected by possible branches. It can be null for a method without a supported executable body. NullState(expression) The compiler’s nullable state at an expression. MaybeNull, NotNull, or None (no answer). It is not a runtime guarantee. A query exposing methods with captured variables using DrillPress; var methodsWithClosures = Code.Methods.Where(method => method.Flow.Data is { Succeeded: true } data && data.Captured.Length > 0); This is evidence to build on, not a rule forbidding every closure. In the pooled-buffer example, a captured local initialized by ArrayPool<T>.Rent indicates a concrete ownership concern. The rule does not claim to prove delegate escape or lease lifetime."
  },
  {
    "title": "Read nullable state at a call site · Calls and flow",
    "url": "analysis.html#nullable",
    "text": "Find potentially null text receivers using DrillPress; var rules = new RuleCatalog(); var trim = CodeType.Of<string>().Member(nameof(string.Trim)).WithParameters(); rules.Rule(\"TEAM011\", \"Handle missing input before trimming text.\") .For(Code.Calls.To(trim).WhereReceiver(receiver => receiver.MayBeNull)) .Forbid(); CodeExpression.MayBeNull reads the compiler’s nullable flow state at that expression; FlowState and DeclaredNullability expose the raw values. Flow state is a compile-time judgment, not a runtime guarantee, and is None where nullable analysis is disabled. Static calls have no receiver, so WhereReceiver skips them. Code.NullChecks selects written null tests (== null, is null, is not null, ?? and friends) with their checked value, polarity and the flow state before the check, so redundant-check rules need no pattern matching of their own."
  },
  {
    "title": "Require test execution · Calls and flow",
    "url": "analysis.html#coverage",
    "text": "Use Coverage.Executed to require execution of selected calls, member references, or other source expressions. Use Coverage.Line.AtLeast(90) for percentage requirements over files, methods, types, or projects. Require tests for critical code using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"TEAM013\", \"Exercise each commit in tests.\") .For(Code.Calls.ToMethodsNamed(\"Commit\")) .Require(Coverage.Executed); rules.Rule(\"TEAM014\", \"Exercise domain code.\") .For(Code.Files.InFolder(\"Domain\")) .Require(Coverage.Line.AtLeast(90)); The analysis engine discovers referencing test projects, arranges collection before evaluation, and reuses successful reports while source, build, test inputs, and environment match. No collector package or report configuration belongs in your rules. The first check needs NuGet access to install the tool automatically. Evidence has three states: covered, uncovered, and unknown. A covered line containing cached ?? store.Load() does not prove that Load ran. Missing, excluded, stale, or ambiguous evidence fails the requirement; findings identify the occurrence and state. Line findings include measured percentages and counts. Zero coverable lines fail. Direct in-memory rule evaluation without prepared evidence returns unknown. Use --refresh-coverage when tests depend on changing external state. Use --skip-coverage to skip coverage rules and their test runs while iterating; the output ends with a line listing the skipped rules. See the coverage integration guide for discovery boundaries, symbol matching, conservative expression mapping, and cache inputs."
  },
  {
    "title": "Projects and relationships",
    "url": "relationships.html",
    "text": "Connect policies across files, types, and project boundaries. Some policies depend on more than one code fragment: a codec needs tests, an interface has implementations, or a production project references an unwanted package. Keep project identity explicit."
  },
  {
    "title": "Read evaluated project facts · Projects and relationships",
    "url": "relationships.html#projects",
    "text": "Keep a legacy package out of production projects using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"TEAM015\", \"Use the application's System.Text.Json contract.\") .For(Code.Projects.Where(project => !project.IsTestProject && project.ReferencesPackage(\"Newtonsoft.Json\"))) .Forbid(); A project finding reports at the start of the project’s first ordinary source file; an empty project cannot be reported by this source-diagnostic protocol. To report on every file instead, select Code.Files and test file.Source.Project. Code.Projects takes the same project filters as other queries: InProject(glob), InTestProjects(), InNonTestProjects() and InProjectsWithType(type). InTestProjects() and InNonTestProjects() use the project classification captured by the loader, not project names. ReferencesPackage compares direct package identifiers case-insensitively. AnalysisProject offers Name, ProjectPath, TargetFramework, IsTestProject, Packages, Properties, SourceRoots and HasType. Packages are direct evaluated references, including requested central versions—not the entire transitive dependency graph. Properties contain selected policy values and explicit overrides, not the whole environment."
  },
  {
    "title": "Require a counterpart · Projects and relationships",
    "url": "relationships.html#counterparts",
    "text": "Require a named round-trip test for each concrete codec using DrillPress; var rules = new RuleCatalog(); var codecs = Code.Types .InNonTestProjects() .ImplementingInterface(CodeType.Named(\"Product.ITextCodec\")) .Where(type => !type.IsAbstract && !type.IsInterface); rules.Rule(\"TEAM016\", \"Add a <CodecName>RoundTrip test for this codec.\") .For(codecs.WithoutMatching(Code.TestMethods, (codec, test) => test.Name == $\"{codec.Name}RoundTrip\" && codec.Solution.ProjectGraph.Includes(test.Source.Project, codec.Source.Project))) .Forbid(); The missing thing has no source location, so the rule reports on the existing codec. Code.TestMethods recognizes xUnit, NUnit and MSTest markers by compiler identity, including derived attributes. This naming convention checks discoverable coverage; it does not prove the test’s assertions are correct. Extend the match with namespace or ownership information if your project has duplicate type names. The keyed WithoutMatching(other, key, otherKey, comparer) overload is useful for simple equality and large inventories. The predicate overload handles context-sensitive matching. Join returns matching pairs rather than missing owners."
  },
  {
    "title": "Respect project boundaries · Projects and relationships",
    "url": "relationships.html#graph",
    "text": "solution.ProjectGraph.Includes(consumer, owner) means the consumer is the owner itself or transitively references that exact evaluated context. DependenciesOf(project) includes the project itself. CompatibleViewsOf(owner) returns separate compatible project sets; it does not merge alternate frameworks or incompatible evaluations. A project name is not a unique compilation identity. A multi-targeted project appears as separate contexts, and a linked source file can be compiled with different settings. Keep those memberships separate when comparing code."
  },
  {
    "title": "Follow type and method relationships · Projects and relationships",
    "url": "relationships.html#types",
    "text": "solution.Relationships is the shared CodeRelationships object, also available from CodeRelationships.In(solution). Member Meaning CallsFrom(methodSymbol) Direct bound source calls, excluding nested function bodies. CallersOf(methodSymbol) Source calls bound to a loaded source declaration. Metadata-only targets have no source declaration key. Reaches(methodSymbol, member) The same static call-path analysis exposed by method.Reaches(member). DerivedTypes(type) Compatible source types inheriting a class or implementing an interface; includes abstract and test types for you to filter. FilesOf(type) Ordinary source files for a type’s partial declarations in its own context."
  },
  {
    "title": "Count concrete implementations · Projects and relationships",
    "url": "relationships.html#implementations",
    "text": "Find interfaces with one concrete production implementation using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"TEAM017\", \"Consider removing this single-implementation interface.\") .For(Code.Interfaces .ImplementationViews() .IgnoringTestProjects() .ConcreteOnly() .WithExactlyOneImplementation()) .Forbid(); ImplementationViews() returns one ImplementationView per compatible source view of each interface, with its Interface, Projects and Implementations. Before filtering, views include abstract types, derived interfaces, test projects and generated definitions. IgnoringTestProjects(), ConcreteOnly() and WhereImplementation(...) filter inside every view, and WithExactlyOneImplementation() keeps interfaces with exactly one implementation in every view. A view is itself reportable, so per-view policies can use Where(view => ...) directly. The analysis does not know about implementations in unloaded external consumers. Whether the interface should actually be removed is your team’s architectural choice."
  },
  {
    "title": "Facts, comparisons, and baselines",
    "url": "facts.html",
    "text": "Share expensive work and compare the things that matter. When several rules need the same calculated information, give it a name and compute it once. When a policy compares two inventories or an accepted version of source, make the comparison explicit."
  },
  {
    "title": "Create a custom query · Facts, comparisons, and baselines",
    "url": "facts.html#custom",
    "text": "Find public type names reused across namespaces using DrillPress; using Microsoft.CodeAnalysis; var publicTypes = Code.Types.WithAccessibility(Accessibility.Public); var ambiguous = CodeQuery<CodeTypeDefinition>.Create(solution => publicTypes.In(solution) .GroupBy(type => type.Name) .Where(group => group.Select(type => type.Namespace).Distinct().Count() > 1) .SelectMany(group => group)); var rules = new RuleCatalog(); rules.Rule(\"TEAM018\", \"Give public types unique names across namespaces.\") .For(ambiguous) .Forbid(); Create takes a function from AnalysisSolution to candidates; use it when a decision needs the whole selection at once, such as grouping. In(solution) reads an existing query. Reuse these instances and keep selection functions pure. For long custom loops, observe solution.CancellationToken."
  },
  {
    "title": "Cache reusable facts · Facts, comparisons, and baselines",
    "url": "facts.html#facts",
    "text": "Share an inventory of public type names using DrillPress; using Microsoft.CodeAnalysis; var publicTypes = Code.Types.Where(type => type.Symbol.DeclaredAccessibility == Accessibility.Public); var names = new AnalysisFact<IReadOnlyDictionary<string, int>>(solution => publicTypes.In(solution) .GroupBy(type => type.Name) .ToDictionary(group => group.Key, group => group.Count())); var repeatedNames = names.SelectMany(counts => counts.Where(pair => pair.Value > 1).Select(pair => pair.Key)); var repeatedTypes = publicTypes.Join( repeatedNames, type => type.Name, name => name, (type, _) => type); AnalysisFact<T>.In(solution) computes once per fact instance per analysis. SelectMany turns part of its value into a query. Values should be treated as immutable. Inside a predicate, ReportAt or fix, read the analysis from any candidate with candidate.Source.Solution (or project.Solution), as in names.In(type.Source.Solution), instead of wrapping candidates in records that carry facts. ProjectFact<T> instead takes a function of AnalysisProject. Read it with In(solution, project); the project must belong to that solution. Alternative frameworks do not share values. Fact dependencies may read other facts, but cycles fail, and computation failures remain cached for that analysis. None of these caches persist across runs."
  },
  {
    "title": "Compare inventories and repeated syntax · Facts, comparisons, and baselines",
    "url": "facts.html#sets",
    "text": "Understand missing and unexpected items using DrillPress; var formats = new SetComparison<string>( expected: [\"json\", \"xml\"], actual: [\"json\", \"yaml\"]); Console.WriteLine(string.Join(\", \", formats.Missing)); // xml Console.WriteLine(string.Join(\", \", formats.Unexpected)); // yaml Console.WriteLine(formats.AreEqual); // False SetComparison<T> compares membership, not occurrence counts. It supports a custom equality comparer. Duplicate inputs do not change the result. nodes.Duplicates(minimumTokens) selects every occurrence of an exact repeated token shape within a compilation context. It ignores comments and whitespace but preserves identifier spelling and literal text. A repeated shape does not prove equivalent behavior or justify an automatic extraction."
  },
  {
    "title": "Anchor calculated results · Facts, comparisons, and baselines",
    "url": "facts.html#anchors",
    "text": "Keep a custom value attached to the type that owns it using DrillPress; var rules = new RuleCatalog(); rules.Rule(\"TEAM019\", \"Describe this type's purpose in its summary.\") .For(Code.Types .Select(type => (Type: type, Summary: type.Symbol.GetDocumentationCommentXml())) .Where(item => item.Summary?.Contains(\"<summary>\") != true)) .ReportAt(item => item.Type) .Forbid(); A tuple, anonymous object or record has no location of its own; ReportAt anchors it to an existing candidate while the rule keeps working with the calculated value. This also works for joined pairs and custom facts. Whether a declaration has any documentation comment at all is built in as HasDocumentationComment()."
  },
  {
    "title": "Compare with accepted source · Facts, comparisons, and baselines",
    "url": "facts.html#baseline",
    "text": "Create a change-aware rule factory using DrillPress; static RuleCatalog CreateRules(SourceBaseline accepted) { var rules = new RuleCatalog(); rules.Rule(\"TEAM020\", \"Add new implementations outside the legacy layer.\") .For(accepted.ChangedFiles .InFolder(\"Legacy\") .Where(file => accepted.ChangeOf(file) == SourceChange.Added)) .Forbid(); return rules; } Create the baseline with new SourceBaseline(acceptedAnalysis) before analyzing the new source. ChangeOf(file) returns Added, Modified, or Unchanged; ChangedFiles selects additions and modifications. ChangeOf(project, symbol) compares declaration text, including partial declarations. PreviousAccessibility(project, symbol) returns the old compiler accessibility, or null when no matching declaration was captured. Renames and changed signatures count as additions; this is not a rename detector. There is no removed-file candidate. A baseline is explicit input The SDK does not fetch Git history, load a baseline from the CLI, or infer an accepted commit. Supply an accepted analysis yourself and use stable project paths, framework labels, and evaluation properties. Test the comparison with two workspaces."
  },
  {
    "title": "Offer safe fixes",
    "url": "fixes.html",
    "text": "A useful correction needs more than compilable code. A finding can propose a correction, but “the edited code compiles” is not enough. A fix must preserve the behavior your policy promises to preserve in every affected loaded compilation."
  },
  {
    "title": "Start from what you selected · Offer safe fixes",
    "url": "fixes.html#builders",
    "text": "A fix factory receives the violating candidate and returns a FixProposal or null. Returning null keeps the finding but offers no fix. Forbid and Require both accept the optional fix argument. Start the proposal with Fix.For(candidate); the candidate’s type decides which edits are available. Three fixes, three builders using DrillPress; var rules = new RuleCatalog(); var empty = CodeType.Of<string>().Member(nameof(string.Empty)); rules.Rule(\"TEAM022\", \"Use \\\"\\\" instead of string.Empty.\") .For(empty.References.OutsideNameOf()) .Forbid(fix: reference => Fix.For(reference) .ReplaceWithLiteral(\"\") .SafeWhen(change => change.Before.RefersTo(empty) && change.After.Is(\"\"))); rules.Rule(\"TEAM023\", \"Add braces to if and else branches.\") .For(Code.IfStatements.Branches().WithoutBraces()) .Forbid(fix: branch => Fix.For(branch).AddBraces().Propose()); rules.Rule(\"TEAM024\", \"Use var when the type is apparent.\") .For(Code.LocalVariables.WithExplicitType().WithInitializer().WhereVarPreservesType()) .Forbid(fix: declaration => Fix.For(declaration).UseVar().Propose()); Candidate Edits Finish with Expression, member reference, object creation or call ReplaceWithLiteral(value), ReplaceWith(\"{0} == {1}\", a, b), ReplaceWithEquality(a, b), ReplaceWithReference(symbol), ReplaceWith(syntax) SafeWhen(change => ...) Call or CodeArgument RemoveArgument(\"name\") / Remove() with ExpectOverloadChange, RequireRemovedValue and RequireRemovedEvaluation SafeWhen(...) Any declaration RemoveModifier(Modifier.Private) Propose() for accessibili"
  },
  {
    "title": "Keep correction policy in your bundle · Offer safe fixes",
    "url": "fixes.html#existing",
    "text": "The SDK provides edit construction and compiler checks. It does not choose between string.Empty and \"\", explicit and implicit accessibility, or explicit and default comparers. The sample spelling fix and sample comparer fix show consumer-owned policies and their conservative proofs. Replacing a field access with a constant can change an enclosing overload. Removing an argument can change behavior even when the resulting call compiles. Keep those proofs with the policy that needs them."
  },
  {
    "title": "Build a custom edit · Offer safe fixes",
    "url": "fixes.html#proposal",
    "text": "When no builder fits, propose raw text edits and supply the complete proof yourself. A helper that requires the caller to supply its proof using DrillPress; using Microsoft.CodeAnalysis.Text; static FixProposal ProposeReplacement( AnalysisSource source, TextSpan span, string replacement, Func<RewriteContext, bool> preservesBehavior) { var edit = SourceChanges.Replace(source, span, replacement); return SourceChanges.Propose([edit], preservesBehavior); } SourceChanges.Replace creates a SourceEdit anchored to the captured file identity, fingerprint, original text, start offset, and length. It does not edit the filesystem. Pass all edits for a correction to SourceChanges.Propose, along with a required proof callback. The proposal checks source eligibility, original text, inactive regions, conflicts, and compiler errors before invoking the proof. The RewriteContext contains the Original project, the Rewritten compilation with local edits applied, and the complete Edits batch, including edits belonging to other contexts. Never use an unconditional “true” proof The callback must establish the semantic claim of your particular correction. Consider overloads, conversions, evaluation order, side effects, lifetime, and reflection. When those cannot be established, offer a finding without a fix."
  },
  {
    "title": "Check binding without overstating it · Offer safe fixes",
    "url": "fixes.html#binding",
    "text": "BindingProof.PreservesEnclosingExpressions(source, replacedNode, replacement) checks surrounding expression symbols, types, and conversions and withholds errors, nameof, and expression-tree cases. It is a reusable compiler check, not proof of equal side effects, evaluation order, or lifetime. For advanced integrations, the FixProposal(edits, validate) constructor accepts a per-project validator and IsSafeIn(project) evaluates it. Prefer SourceChanges.Propose for ordinary custom fixes so its standard eligibility and compiler checks are included."
  },
  {
    "title": "Validation and application are separate · Offer safe fixes",
    "url": "fixes.html#agreement",
    "text": "Your rule proposes one complete batch. The engine checks every loaded context containing an edited physical file—even a context that reported no finding. The validator requires agreement and withholds conflicting or disagreeing batches. The CLI marks an accepted correction with +. Only fix writes it. Generated, missing, inactive, non-editable, or ambiguously bound memberships cannot be assumed safe. Source fingerprints guard against applying edits to a changed file. A multi-file proposal is validated as a unit, but physical application is atomic per file, not a filesystem-wide transaction. If later application fails, the CLI retains recovery information. There is no file-creation/deletion, project-wide rename, or general refactoring engine in this API. See the application and recovery contract for operational details."
  },
  {
    "title": "Test the fixes you withhold · Offer safe fixes",
    "url": "fixes.html#test-fixes",
    "text": "Check both HasFix and the complete FixedText(path) result. Include linked files, multiple frameworks, conflicting proposals, comments, invalid code, and different overload contexts. “Finding exists” does not prove “fix is safe.” The next chapter uses the production validator in memory."
  },
  {
    "title": "Reusable structural and framework edits · Offer safe fixes",
    "url": "fixes.html#reusable-edits",
    "text": "Fix.For(expression).UseCollectionExpression().Propose() converts supported standard collections/arrays with a written target type, preserving the concrete type and each element's evaluation count and order. Nonempty dictionary initializers, capacity constructors and interface targets changing concrete type are withheld. Fix.For(namespaceNode).UseFileScopedNamespace().Propose() converts a file's single block namespace and removes one four-space indentation level. Nested namespaces, directives, multiline text and ambiguous layouts are withheld, and so is a conversion that would change a string's value or a compiler-supplied argument such as a caller line number. A consumer must exclude source exported or concatenated into a different compilation. Fix.For(call).AddArgumentOfType(type, expression, proof) finds the unique overload adding that type and uses the argument-addition validator. Existing parameter names, types, ref kinds and generic arity must match, comparing generic overloads with the call's type arguments; the supplied proof explains the new value. Fix.For(expression).UseTimeProvider(clockExpression).Propose() supports DateTime.UtcNow, DateTimeOffset.UtcNow/Now, paired Stopwatch timestamp measurements, Task delays/timed waits and explicit new PeriodicTimer(TimeSpan) or new CancellationTokenSource(TimeSpan) constructors. The consumer chooses the application's clock; the library requires a TimeProvider local, parameter or instance member. Either end of a Stopwatch measurement yields one fix that rewrites the timestamp and every elapsed-time use together. Local DateTime kinds, ambiguous timestamp uses, timer creations with an object initializer and unsupported argument shapes are withheld. Fix.For(property).UseInitOnly().SafeWhen(symbol => ...) converts an unrestricted automatic setter to init, proves symbol correspondence and compilation, and asks the consumer to approve its construction/serialization contract. Implemented or restricted setters are withheld. Fix.For(creation).AddInitializer(member, expression).SafeWhen(value => ...) adds a previously unassigned simple member, proves unchanged constructed type, enclosing bindings, compiler-supplied arguments such as CallerArgumentExpression and binding to that member, and asks the consumer to prove the new value. Existing assignments remain. Adding static to a sealed class replaces the written sealed token; ModifierChange reports both the removed and added tokens. Rename permits a destination name in an unrelated scope, but rebinds references spelled like either name to reject capture or shadowing. Independent argument additions validate their own changed defaults in an atomic batch; caller-info changes around other edits remain unsafe. Evaluation-count checks with no registered inputs succeed only when both bounded evaluation traces have no child evaluations, so empty collections need no special-case bypass. Fix.For(statementNode).UseAssertAll().Propose() converts a synchronous var foreach with one xUnit assertion to Assert.All , preserving element type and assertion binding. Choosing it explicitly accepts aggregated failure reporting. Await/ref expressions, pattern-only enumerators and a public GetEnumerator that is not the interface's (outside arrays and the common System.Collections.Generic collections) are withheld."
  },
  {
    "title": "Test and run your rules",
    "url": "testing.html",
    "text": "Prove the rule finds the right code—and leaves the rest alone. Keep tests small enough that the intended violation is obvious. Test the negative cases as carefully as the positive ones: a noisy convention quickly stops being useful."
  },
  {
    "title": "Add the consumer test kit · Test and run your rules",
    "url": "testing.html#setup",
    "text": "Reference the DrillPress.Testing NuGet package at the same exact alpha version as your SDK (or src/DrillPress.Testing/DrillPress.Testing.csproj when working from source) from your test project, along with your rule project and your chosen test framework. The test kit runs the real evaluator and response validator. It does not need a target project on disk. RuleTestWorkspace() uses the host runtime’s assembly references for convenience. You can supply an explicit list of compiler MetadataReference objects when you need exact reference packs or synthetic assemblies. The default constructor reads reference assemblies; the target source and edit results stay in memory."
  },
  {
    "title": "Assert the complete finding and source text · Test and run your rules",
    "url": "testing.html#first-test",
    "text": "An xUnit test for a rule and its fix using DrillPress; using DrillPress.Testing; using Xunit; public class EmptyStringRuleTests { [Fact] public async Task Replaces_string_Empty_with_a_literal() { var workspace = new RuleTestWorkspace(); workspace.AddProject(\"Example\", [new TestSource(\"Example.cs\", \"class C { string Value => string.Empty; }\")]); var rules = new RuleCatalog(); rules.Rule(\"TEAM021\", \"Use \\\"\\\" instead of string.Empty.\") .For(CodeType.Of<string>().Member(nameof(string.Empty)).References.OutsideNameOf()) .Forbid(fix: reference => Fix.For(reference) .ReplaceWithLiteral(\"\") .SafeWhen(change => change.After.Is(\"\"))); var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken); Assert.Equal( \"\"\" TEAM021 Use \"\" instead of string.Empty. Example.cs +1:27 \"\"\", result.Output); Assert.Equal(\"class C { string Value => \\\"\\\"; }\", result.FixedText(\"Example.cs\")); } } result.Output is exactly what the CLI prints: each rule once, then each file and its line:column locations, with + marking a fix that survived validation. Asserting the whole output checks the rule, location and fix availability in one readable value; normalize \\r\\n if your sources use Windows line endings. result.Findings offers the same data as TestFinding values with the exact highlighted text. FixedText applies only validated fixes and returns the original source when none survive."
  },
  {
    "title": "Model the contexts your policy depends on · Test and run your rules",
    "url": "testing.html#contexts",
    "text": "AddProject returns an AnalysisProject. Dependencies must already belong to the workspace. Its options are: Argument Purpose name, sources Project identity and exact TestSource(Path, Text, Generated) values. framework A context label, defaulting to net10.0. It does not select reference assemblies. isTest Mark a test project for project-role policies. dependencies Previously added source projects to reference. symbols Conditional compilation symbols for #if. allowErrors Keep deliberately invalid source for failure-path tests. references, packages Override compiler references or supply direct package facts. Package facts do not download packages or supply assemblies. A project named like an assembly your test process loads keeps that assembly, so its snippets can use the real types; projects that depend on it see the workspace project instead. outputKind The compilation kind, defaulting to a class library. Use OutputKind.ConsoleApplication for top-level statements. nullable, languageVersion Compiler options, defaulting to nullable annotations and warnings enabled and C# 14. implicitUsings When true, adds the .NET SDK’s default global usings (System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Threading, System.Threading.Tasks) as a generated file, like a project with ImplicitUsings enabled. Off by default. Model a referenced production project using DrillPress.Testing; var workspace = new RuleTestWorkspace(); var production = workspace.AddProject(\"Product\", [ new TestSource(\"Codec.cs\", \"public class Codec { }\") ]); workspace.AddProject(\"Product.Tests\", [ new TestSource(\"Tests.cs\", \"class Tests { Codec value = new(); }\") ], isTest: true, dependencies: [production]); Use the same source path and text across projects to test linked-file agreement. Use different reference sets for real framework differences. For xUnit attribute detection, isTest: true alone is not enough: include real xUnit references and Fact/Theory attributes."
  },
  {
    "title": "Rules about tests · Test and run your rules",
    "url": "testing.html#xunit",
    "text": "Code.TestMethods and Code.TestClasses recognize xUnit, NUnit and MSTest markers by compiler identity, so rules about tests need no attribute lists of their own. Layout and assertion conventions stay in your bundle: the sample body analysis shows a cached consumer fact built from public APIs. To test such rules, add the real test-framework assemblies to the workspace with references, and mark the project with isTest: true. The project flag alone does not make a method a test. When a fixture's exact formatting is the subject of the rule, protect just that fixture with an explained CSharpier ignore comment. Code embedded in string literals normally needs no ignore."
  },
  {
    "title": "Show that exceptions can be allowed · Test and run your rules",
    "url": "testing.html#allowances",
    "text": "Teams that run rules as hard errors still need a way to accept a justified exception. Instead of teaching each rule to look for comments, the code names the rule and the reason in an ordinary comment: // drillpress-allow RULE-ID: reason, with several IDs separated by commas. After code, it allows findings that start on that line. On its own line, it allows the whole statement or member or type declaration that starts on the next code line, such as a multi-line statement or a complete test method; only comment lines may come between. An allowance accepts one finding using DrillPress; using DrillPress.Testing; using Xunit; public class ClockRuleTests { [Fact] public async Task An_explained_allowance_accepts_one_clock_read() { var workspace = new RuleTestWorkspace(); workspace.AddProject(\"Shop\", [new TestSource(\"Orders.cs\", \"\"\" class Orders { DateTime Placed() => DateTime.Now; // drillpress-allow TEAM030: shown to the clerk in local time DateTime Shipped() => DateTime.Now; } \"\"\")], implicitUsings: true); var rules = new RuleCatalog(); rules.Rule(\"TEAM030\", \"Read the time from TimeProvider.\").For(CodeType.Of<DateTime>().Member(nameof(DateTime.Now)).References).Forbid(); var result = await workspace.CheckAsync(rules, TestContext.Current.CancellationToken); Assert.Equal( \"\"\" TEAM030 Read the time from TimeProvider. Orders.cs 4:27 \"\"\", result.Output); } } Allowed findings are not printed, counted or fixed. Allowances are checked too: one that allows nothing is reported as drillpress-allow-unused, with a fix that removes it, and one without a rule ID or reason, naming an unknown rule, or separated from its code by a blank line is reported as drillpress-allow-invalid. Rule IDs starting with drillpress- are reserved for these built-in rules. The rule authoring reference defines the exact scope."
  },
  {
    "title": "Test an accepted baseline · Test and run your rules",
    "url": "testing.html#baselines",
    "text": "Compare two in-memory versions using DrillPress; using DrillPress.Testing; var before = new RuleTestWorkspace(); before.AddProject(\"Product\", [new TestSource(\"A.cs\", \"class A { }\")]); var baseline = new SourceBaseline(before.Analyze()); var after = new RuleTestWorkspace(); after.AddProject(\"Product\", [ new TestSource(\"A.cs\", \"class A { public int Value; }\"), new TestSource(\"B.cs\", \"class B { }\") ]); var changes = baseline.ChangedFiles.In(after.Analyze()); // A.cs is Modified; B.cs is Added."
  },
  {
    "title": "Before sharing a bundle · Test and run your rules",
    "url": "testing.html#checklist",
    "text": "Test one violation and a nearby allowed case, including configured exceptions. Check exact locations and full output; do not assert only the count. Test alias spelling, overloads, generated source, and unresolved symbols where relevant. For fixes, check both accepted and withheld corrections. Run the bundle against a restored real project with the CLI."
  },
  {
    "title": "When a rule does not behave as expected · Test and run your rules",
    "url": "testing.html#troubleshooting",
    "text": "It finds nothing. Check the project/file scope, whether the source is generated, the declaring type’s full name, and whether your exact overload includes all parameters. The test compiles, but the target does not. Runtime reference defaults are a convenience, not a target reference pack. Supply explicit references for fidelity. There is a finding but no fix. Inspect the safety boundaries in Offer safe fixes. This is often intentional, not a broken rule. A rule throws. Check null/absent compiler facts, unsuccessful flow analysis, and invalid source. Do not convert unknown facts into confident findings."
  },
  {
    "title": "API field guide",
    "url": "reference.html",
    "text": "Find the right building block without learning the compiler first. Start with a Code root, narrow it with filters, and finish with Forbid or Require. Use compiler objects when your policy needs details that these building blocks do not expose. Namespace tip Rules need only using DrillPress;. Tests a"
  },
  {
    "title": "Declare rules · API field guide",
    "url": "reference.html#declarations",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "RuleCatalog · API field guide",
    "url": "reference.html#api-rulecatalog",
    "text": "Rule(id, message, fixComplexity?); Rule(descriptor); Evaluate(solution) Reserves unique rule identities. Evaluate returns ordered diagnostics and fails when a rule has no Forbid or Require clause; it is not the cross-context fix-validation workflow."
  },
  {
    "title": "RuleDefinition · API field guide",
    "url": "reference.html#api-ruledefinition",
    "text": "Descriptor; For(query) One rule identity. Each For starts a typed clause; several clauses can share the identity."
  },
  {
    "title": "RuleClause<T> · API field guide",
    "url": "reference.html#api-ruleclause-t",
    "text": "ReportAt(element | syntax | token | location); ReportOncePer(key); CollectCoverageIn(projects); Forbid(fix?); Require(condition, fix?) Forbid reports every candidate; Require reports candidates failing the condition. Both return the definition, so another For can follow."
  },
  {
    "title": "RuleCondition<T> · API field guide",
    "url": "reference.html#api-rulecondition-t",
    "text": "constructor(predicate); And; Or; Not; ExceptWhen; implicit from CoverageRequirement Pure reusable conditions with short-circuit composition."
  },
  {
    "title": "RuleDescriptor / RuleDiagnostic · API field guide",
    "url": "reference.html#api-ruledescriptor-rulediagnostic",
    "text": "Id, Message, FixComplexity / Descriptor, Location, Source, Fix, Disposition, Evidence Stable policy text and a source-anchored result."
  },
  {
    "title": "Select code · API field guide",
    "url": "reference.html#selection",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "Code · API field guide",
    "url": "reference.html#api-code",
    "text": "Files; FilesIncludingGenerated; Projects; Types; Interfaces; TypeDeclarations; Methods; Fields; EnumMembers; Properties; Parameters; Declarations; TestMethods; TestClasses; AbstractTestClasses; Calls; ObjectCreations; Routes; RouteGroups; MemberReferences; TypeReferences; IfStatements; Catches; LocalVariables; ForEachLoops; OutVariables; Enumerations; NullChecks; Comments; Nodes<T>(); Operations<T>(); ReferencesTo(symbol) The single starting point. Ordinary roots exclude generated files; types are deduplicated per evaluated context."
  },
  {
    "title": "CodeQuery<T> · API field guide",
    "url": "reference.html#api-codequery-t",
    "text": "Where; ExceptWhen; Select; SelectMany; Concat; Union; Join; WithoutMatching; Create; In(solution) Reusable, analysis-cached selection. Where and ExceptWhen accept lambdas or RuleCondition values."
  },
  {
    "title": "Place filters · API field guide",
    "url": "reference.html#api-place-filters",
    "text": "InProject(glob); InTestProjects(); InNonTestProjects(); InProjectsWithType(type); InNamespace(pattern); InFolder(folder); InFilesNamed(glob) Available on every query of reportable elements; the project filters also work on Code.Projects. Test projects come from the build classification, not names."
  },
  {
    "title": "Declaration filters · API field guide",
    "url": "reference.html#api-declaration-filters",
    "text": "Named(names); NameMatching(glob); WithNameContainingAnyWord(words); WithAttribute(types); WithExplicitModifier(modifier); WithAccessibility(accessibility) Shared by types, type parts, methods, fields, properties, parameters and declared symbols."
  },
  {
    "title": "ICodeDeclaration · API field guide",
    "url": "reference.html#api-icodedeclaration",
    "text": "Name; NameWords; Symbol; Accessibility; HasExplicitModifier; ExplicitModifier; NameStartsWith; NameEndsWith; NameMatches; NameContainsWord; Attributes(); HasAttribute; HasDocumentationComment() The same name, attribute, modifier and documentation tests for every declaration kind."
  },
  {
    "title": "ICodeElement / SourceLocation · API field guide",
    "url": "reference.html#api-icodeelement-sourcelocation",
    "text": "Source; Location; IsInProject; IsInNamespace; IsInFolder / FilePath, Start, Length, Line, Column The contract for reportable candidates and physical UTF-16 spans."
  },
  {
    "title": "Enclosing code · API field guide",
    "url": "reference.html#api-enclosing-code",
    "text": "element.IsInType(type); ContainingMethod(); EnclosingLoop(); IsInLoop(); IsGuardedByCatchAll(); UnguardedHandler() Where any reportable element runs. Code in lambdas and local functions belongs to the type and method that declare them, while loops and try statements count only within the element's own function. UnguardedHandler returns the HandlerClause (None, Catch or Finally) whose failure would replace the exception being handled; a catch clause that catches any exception guards it, without evaluating filters."
  },
  {
    "title": "Declarations and statements · API field guide",
    "url": "reference.html#source",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "CodeTypeDefinition · API field guide",
    "url": "reference.html#api-codetypedefinition",
    "text": "Name; Namespace; IsClass; IsInterface; IsStruct; IsEnum; IsRecord; IsAbstract; IsStatic; IsNested; HasOnlyStaticMembers; IsTopLevelProgram; Implements; DerivesFrom; IsOrDerivesFrom; Symbol; Syntax; Solution One named type per evaluated context. IsTopLevelProgram recognizes the Program class generated for top-level statements, including a partial declaration of it. Query forms: DerivedFrom, ImplementingInterface, ImplementationViews."
  },
  {
    "title": "CodeTypeDeclaration · API field guide",
    "url": "reference.html#api-codetypedeclaration",
    "text": "IsTopLevel; Syntax; Symbol; TopLevel() Each written type part, including every partial part."
  },
  {
    "title": "CodeMethod · API field guide",
    "url": "reference.html#api-codemethod",
    "text": "IsAsync; IsStatic; ReturnsVoid; ReturnTypeIs; Parameters; HasBody; HasEmptyBody; HasNoStatements; Body(nested?); ContainingType; Flow; Reaches(member); Callees(sameType?, transitive?, nested?) An ordinary method. Query forms: DeclaredInTypesDerivedFrom, Overriding, DirectlyOverriding, OverrideMatches, WhereBody, ContainingTypes."
  },
  {
    "title": "CodeField / CodeProperty / CodeParameter · API field guide",
    "url": "reference.html#api-codefield-codeproperty-codeparameter",
    "text": "Type; TypeIs<T>(); IsConst, IsReadOnly, IsStatic, Initializer / HasGetter, HasSetter, HasInit, IsAutoProperty / Ordinal, ContainingSymbol, IsLambdaParameter, HasDefaultValue, DefaultValue Member declarations reported at their identifier. Each field variable is its own candidate."
  },
  {
    "title": "CodeEnumMember · API field guide",
    "url": "reference.html#api-codeenummember",
    "text": "Name; Symbol; Value; HasExplicitValue; ContainingType; Syntax One enum member, reported at its name; Code.Fields leaves enum members out."
  },
  {
    "title": "CodeSymbol · API field guide",
    "url": "reference.html#api-codesymbol",
    "text": "Symbol; Syntax; Name; Accessibility Every other declared symbol, such as events, locals and accessors."
  },
  {
    "title": "CodeFile · API field guide",
    "url": "reference.html#api-codefile",
    "text": "Path; Name; Folder; RelativePath; HasTopLevelStatements; Nodes<T>(); files.Calls(); files.Operations<T>(); files.Declarations(); files.Comments() One document membership. RelativePath is the path below the project directory, null for a linked file outside it. File queries restrict discovery to that scope, and place filters apply before syntax is enumerated."
  },
  {
    "title": "CodeNode<T> · API field guide",
    "url": "reference.html#api-codenode-t",
    "text": "Syntax; ContainingSymbol; Operation; TypeInfo; Constant; AsExpression(); Duplicates(minimumTokens) Any syntax kind with optional compiler facts. Check Constant.HasValue."
  },
  {
    "title": "CodeIfStatement / CodeBranch · API field guide",
    "url": "reference.html#api-codeifstatement-codebranch",
    "text": "Then; Else; Branches; InconsistentlyBracedBranch; WithElse(); Branches(); WithoutBraces() / HasBraces; IsElseIf If statements and their branches; an else-if is not a missing brace."
  },
  {
    "title": "CodeCatch · API field guide",
    "url": "reference.html#api-codecatch",
    "text": "new CodeCatch(source, clause); ExceptionType; Catches<T>(); CatchesAnyException; HasFilter; Filter; Body; IsEmpty; Rethrows Written catch clauses, reported at the clause header. The constructor views any written clause, such as one of a try statement's Catches."
  },
  {
    "title": "CodeVariableDeclaration · API field guide",
    "url": "reference.html#api-codevariabledeclaration",
    "text": "TypeName; HasExplicitType; HasInitializer; CanUseVar; Variables; WithExplicitType(); WithInitializer(); WhereVarPreservesType() Local, for, using, foreach and out declarations."
  },
  {
    "title": "CodeComment / CodeBody · API field guide",
    "url": "reference.html#api-codecomment-codebody",
    "text": "Text; Kind; ContainingDeclaration; element.ExplanatoryComment(); HasExplanatoryComment() / CodeBody.Of(source, node); Calls(); Calls(member); Nodes<T>(); ControlFlowNodes(kinds); NestedBodies(); Sections(); BlankLines; IsEmpty Comments and executable bodies. Body queries: Body(), Conditions(), NullChecks()."
  },
  {
    "title": "Types and members · API field guide",
    "url": "reference.html#semantic",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "CodeType · API field guide",
    "url": "reference.html#api-codetype",
    "text": "Of<T>(); Named(name, assembly?); Framework(name); FromSymbol(symbol); Member(name, parameters?); Constructor(parameters); References; Matches Metadata identity with open-generic slots, arrays and optional assembly constraints. FromSymbol captures a bound type with its exact constructed arguments; an original definition gives an open descriptor."
  },
  {
    "title": "CodeMember · API field guide",
    "url": "reference.html#api-codemember",
    "text": "WithParameters(types); WithSignature(signature); References; Matches; DeclaringType; Name A field, property or method family, or one exact overload. Empty parameters mean parameterless."
  },
  {
    "title": "ApiSet · API field guide",
    "url": "reference.html#api-apiset",
    "text": "constructor(members); Contains(method); Union(other) A family of restricted APIs for To(...) and policy checks."
  },
  {
    "title": "MemberReference / CodeTypeReference · API field guide",
    "url": "reference.html#api-memberreference-codetypereference",
    "text": "ContainingType; MemberName; Symbol; Expression; Facts; AsArgument() / Type; RefersTo(type); Facts; OutsideNameOf(); MemberReferences.To(members) Written references to a member or type, with compiler binding."
  },
  {
    "title": "Symbols / CodeAttribute · API field guide",
    "url": "reference.html#api-symbols-codeattribute",
    "text": "HasAttribute(symbol, type, includeDerived?); IsOrDerivesFrom; DerivesFrom; Implements; ImplementsOrOverrides; IsEventHandler; type.IsTask(); UnwrapTask(); method.AddingParameter(type, matchNames?); symbol.Attributes() / ConstructorValue<T>; NamedValue<T>; Matches Semantic predicates for raw compiler symbols and typed attribute values. IsTask recognizes the framework's Task and ValueTask types, with or without a result. AddingParameter finds overloads that add one parameter of a type and keep the others; it is candidate discovery, not proof that the overload behaves the same. Generic overloads compare with the call's type arguments."
  },
  {
    "title": "Calls, expressions and flow · API field guide",
    "url": "reference.html#operations",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "CodeInvocation · API field guide",
    "url": "reference.html#api-codeinvocation",
    "text": "Target; Receiver; Arguments; Argument(name); ArgumentsFor(name); Calls(member); IsDeclaredOn; TargetNameMatches; IsConditional; Expression; Chain() Overload-aware calls in any spelling, with compiler-mapped arguments including defaults. They report at the method name."
  },
  {
    "title": "ScopedValue · API field guide",
    "url": "reference.html#api-scopedvalue",
    "text": "element.ValuesInScope<T>(); ValuesInScope(type) / Symbol; Name; Kind; Type Locals, parameters, fields and properties of a type that code at an element can read by name, nearest first."
  },
  {
    "title": "CodeCallChain · API field guide",
    "url": "reference.html#api-codecallchain",
    "text": "Calls; First; Last; Root; Local; Before(call); After(call); ThroughLocal() The fluent chain a call belongs to, optionally continued through the local it starts from."
  },
  {
    "title": "Call filters · API field guide",
    "url": "reference.html#api-call-filters",
    "text": "To(member | apiSet); ToMethodsNamed; ToMethodsMatching; ToMethodsDeclaredOn; ToMethodsDeclaredOnOrDerivedFrom; OnReceiverOfType<T>(); WhereReceiver; WhereArgument; WhereArgumentOrMissing; WhereAnyArgument; ArgumentsFor; OutsideExpressionTrees Narrow Code.Calls by target, receiver or argument."
  },
  {
    "title": "CodeArgument · API field guide",
    "url": "reference.html#api-codeargument",
    "text": "Parameter; Name; Value; IsExplicit; IsReceiver; Is(value); IsOmittedOr(value); ValueAs<T>(); TextValue; Invocation; SourceValues() One bound argument; omitted defaults have no source value and report at the call."
  },
  {
    "title": "CodeObjectCreation · API field guide",
    "url": "reference.html#api-codeobjectcreation",
    "text": "Type; Constructor; Creates<T>(); Calls(constructor); Arguments; Argument(name); IsTargetTyped; Of<T>() new T(...) and target-typed new(...)."
  },
  {
    "title": "CodeRoute · API field guide",
    "url": "reference.html#api-coderoute",
    "text": "Mapping; Path; Configuration; GroupConfiguration; HandlerExpression; Handler; IsConfiguredWith(member, includeGroups?) A minimal API route from Code.Routes or a group from Code.RouteGroups. Path includes the constant prefixes of its groups, followed through locals, and a local or call holding the application, such as builder.Build(), has none; what can't be read statically is null. Route metadata is not an authorization analysis."
  },
  {
    "title": "CodeExpression · API field guide",
    "url": "reference.html#api-codeexpression",
    "text": "Type; TypeIs; ConvertedTypeIs; TypeIsOrDerivesFrom; TypeIsAssignableTo; Constant; Is(value); IsConstant; TextValue; Symbol; RefersTo(member); IsNameOf; FlowState; MayBeNull; Facts; AsInvocation(); AwaitedCall(); AsBuiltString(); expressions.WithCollectionExpressionTarget() A bound expression with type, constant, symbol and nullable facts. AwaitedCall is the call an await awaits, through conversions and ConfigureAwait."
  },
  {
    "title": "Culture and clock calls · API field guide",
    "url": "reference.html#api-culture-and-clock-calls",
    "text": "call.ComparesWithCulture(); call.FormatsWithCulture(); hole.FormatsWithCulture(); call.WaitsWithoutTimeProvider(); creation.TimesWithoutTimeProvider() Framework calls that depend on the current culture or run on the system clock. They classify common framework APIs; they never choose which culture or clock an application should use."
  },
  {
    "title": "CodeNullCheck / ConditionPattern · API field guide",
    "url": "reference.html#api-codenullcheck-conditionpattern",
    "text": "CheckedValue; Polarity; Domain; Form; IsKnownNotNullBeforeCheck / Null; EmptyString; NullOrEmptyString; NullOrWhiteSpaceString; ForCall(...) Written null tests, and semantic condition patterns for bodies.Conditions().Checks(...)."
  },
  {
    "title": "CodeOperation<T> / MethodFlow · API field guide",
    "url": "reference.html#api-codeoperation-t-methodflow",
    "text": "Operation; ContainingSymbol; operation.WithoutConversions(includeExplicit?) / Graph; Data; NullState(expression) Typed compiler operations and lazy control-flow, data-flow and nullable analysis. Prefer method.Flow."
  },
  {
    "title": "Coverage · API field guide",
    "url": "reference.html#api-coverage",
    "text": "Executed; EnumerationStarted; Line.AtLeast(percent); OnUncovered(message); OnUnknown(message) Test-execution requirements for Require; the engine collects evidence automatically."
  },
  {
    "title": "StandardLinq · API field guide",
    "url": "reference.html#api-standardlinq",
    "text": "DrillPress.Presets in the optional DrillPress.Linq package: Inspect(call); Status; Category; SequenceInputs Maintained framework LINQ facts. See the LINQ catalogue ."
  },
  {
    "title": "Projects and relationships · API field guide",
    "url": "reference.html#graphs",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "AnalysisProject · API field guide",
    "url": "reference.html#api-analysisproject",
    "text": "Name; ProjectPath; TargetFramework; IsTestProject; Packages; ReferencesPackage(id); Properties; SourceRoots; HasType(type); Sources; Compilation; Solution One evaluated compilation context. As a candidate it reports at its first ordinary source file."
  },
  {
    "title": "AnalysisSolution / AnalysisSource · API field guide",
    "url": "reference.html#api-analysissolution-analysissource",
    "text": "Projects; ProjectGraph; Relationships; Implementations; Options / Project; Solution; Document; Tree; Model; Locate(span) The shared cache lifetime and captured sources with semantic models."
  },
  {
    "title": "ProjectGraph · API field guide",
    "url": "reference.html#api-projectgraph",
    "text": "Includes(consumer, owner); DependenciesOf; CompatibleViewsOf Exact evaluated-context dependencies."
  },
  {
    "title": "CodeRelationships · API field guide",
    "url": "reference.html#api-coderelationships",
    "text": "CallsFrom; CallersOf; Reaches; DerivedTypes; FilesOf Source relationships and static call paths; available as solution.Relationships."
  },
  {
    "title": "ImplementationView · API field guide",
    "url": "reference.html#api-implementationview",
    "text": "Interface; Projects; Implementations; IgnoringTestProjects(); ConcreteOnly(); WhereImplementation(...); WithExactlyOneImplementation() Implementations of one interface per compatible view; reportable at the interface."
  },
  {
    "title": "Facts and comparison · API field guide",
    "url": "reference.html#comparison",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "AnalysisFact<T> / ProjectFact<T> · API field guide",
    "url": "reference.html#api-analysisfact-t-projectfact-t",
    "text": "constructor(compute); In(solution); SelectMany(selector) / In(solution, project) One cached value per analysis or project context."
  },
  {
    "title": "SetComparison<T> · API field guide",
    "url": "reference.html#api-setcomparison-t",
    "text": "constructor(expected, actual, comparer?); Missing; Unexpected; AreEqual Membership comparison; duplicates do not change membership."
  },
  {
    "title": "ExpressionGroups · API field guide",
    "url": "reference.html#api-expressiongroups",
    "text": "expressions.ConstantGroups(); expressions.TemplateGroups(options); WithGroupsFrom(groups) Repeated constants and one-hole templates among the expressions you select, for extraction fixes."
  },
  {
    "title": "SourceBaseline / SourceChange · API field guide",
    "url": "reference.html#api-sourcebaseline-sourcechange",
    "text": "constructor(accepted); ChangeOf(file); ChangeOf(project, symbol); PreviousAccessibility; ChangedFiles / Unchanged; Added; Modified Compare against an explicitly supplied accepted analysis, not Git history."
  },
  {
    "title": "PathPattern · API field guide",
    "url": "reference.html#api-pathpattern",
    "text": "constructor(pattern); Matches(path) Case-sensitive full-path glob supporting *, ?, ** and **/."
  },
  {
    "title": "Fixes · API field guide",
    "url": "reference.html#corrections",
    "text": "DrillPress · Read the guide →"
  },
  {
    "title": "Fix · API field guide",
    "url": "reference.html#api-fix",
    "text": "For(expression | reference | creation | call | argument | declaration | branch | comment | variableDeclaration); Extract(group) Starts a typed fix plan from the candidate you selected."
  },
  {
    "title": "Expression fixes · API field guide",
    "url": "reference.html#api-expression-fixes",
    "text": "ReplaceWithLiteral(value); ReplaceWith(template, keep...); ReplaceWithEquality(left, right, absorbNegation?); ReplaceWithReference(symbol); ReplaceWith(syntax); MustPreserve(behavior); SafeWhen(change => ...) ExpressionChange exposes Before, After, Kept, Rewrite and Same(before, after)."
  },
  {
    "title": "Structural fixes · API field guide",
    "url": "reference.html#api-structural-fixes",
    "text": "RemoveArgument(name) / Remove(); AddArgument(name, expression); RemoveModifier(modifier); AddModifier(modifier); Rename(newName); AddBraces(); Remove(); UseVar(); ToConstant(name); ToMethod(name, parameterName) Finish with Propose() where the library owns the proof, or SafeWhen(...)."
  },
  {
    "title": "SourceChanges / FixProposal / RewriteContext · API field guide",
    "url": "reference.html#api-sourcechanges-fixproposal-rewritecontext",
    "text": "Replace(source, span, text); Propose(edits, proof) / Edits; WithheldReason; IsSafeIn(project) / Original; Rewritten; Edits; Map(source, node); Same(before, after) Custom edits with your own complete proof, validated against the whole batch."
  },
  {
    "title": "Testing and hosting · API field guide",
    "url": "reference.html#tests",
    "text": "DrillPress.Testing · DrillPress.Engine · Read the guide →"
  },
  {
    "title": "RuleTestWorkspace · API field guide",
    "url": "reference.html#api-ruletestworkspace",
    "text": "constructor(references?); AddProject(name, sources, framework?, isTest?, dependencies?, symbols?, allowErrors?, references?, packages?, nullable?, languageVersion?, implicitUsings?, outputKind?); Analyze(); CheckAsync(rules); WithCoverage(configure) In-memory projects; CheckAsync runs the production evaluator and validator, including // drillpress-allow allowances."
  },
  {
    "title": "RuleTestResult / TestFinding · API field guide",
    "url": "reference.html#api-ruletestresult-testfinding",
    "text": "Output; ExplainedOutput; Findings; FixedText(path) / Rule, Path, Line, Column, Text, HasFix Output is exactly what the CLI prints; FixedText applies validated fixes in memory."
  },
  {
    "title": "RuleApplication · API field guide",
    "url": "reference.html#api-ruleapplication",
    "text": "constructor(); RunAsync(rules, args) Hosts a rule bundle. Return its exit code from Program; the CLI loads real projects."
  },
  {
    "title": "When you need the compiler directly · API field guide",
    "url": "reference.html#compiler",
    "text": ""
  }
];
