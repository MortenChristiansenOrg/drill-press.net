using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Analysis;

public sealed class CodeFunctionTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    private static readonly CodeMember _sleep = CodeType
        .Named("System.Threading.Thread")
        .Member("Sleep");

    [Fact]
    public void Callees_follow_local_functions_constructors_accessors_events_and_operators()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Workers",
            [
                new(
                    "Worker.cs",
                    """
                    class Worker
                    {
                        event System.Action? Changed { add => Subscribe(); remove { } }
                        int this[int index] { get => Index(); set { } }
                        int Value { get => Read(); set => Store(); }

                        void Run()
                        {
                            Local();
                            var job = new Job(1);
                            Value += 1;
                            _ = this[0];
                            Changed += Run;
                            var sum = job + job;
                            long widened = job;
                            void Local() => Helper();
                        }

                        void Helper() { }
                        int Read() => 0;
                        void Store() { }
                        int Index() => 0;
                        void Subscribe() { }
                    }

                    class Base { protected Base(int seed) { } }

                    class Job : Base
                    {
                        readonly int _id = Next();
                        public Job(int seed) : base(seed) => Start();
                        static int Next() => 0;
                        void Start() { }
                        public static Job operator +(Job left, Job right) => left;
                        public static implicit operator long(Job job) => 0;
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var run = Code.Methods.Named("Run").In(solution).Single();

        var callees = run.Callees(transitive: true);

        Assert.Equal(
            [
                "LocalFunction Worker.Local",
                "Constructor Job..ctor",
                "Accessor Worker.get_Value",
                "Accessor Worker.set_Value",
                "Accessor Worker.get_Item",
                "Accessor Worker.add_Changed",
                "Operator Job.op_Addition",
                "Operator Job.op_Implicit",
                "Method Worker.Helper",
                "Method Job.Next",
                "Constructor Base..ctor",
                "Method Job.Start",
                "Method Worker.Read",
                "Method Worker.Store",
                "Method Worker.Index",
                "Method Worker.Subscribe",
            ],
            callees.Select(callee =>
                $"{callee.Kind} {callee.Symbol!.ContainingType.Name}.{callee.Name}"
            )
        );
    }

    [Fact]
    public void Constructors_run_initializers_from_every_partial_part_and_their_base_constructors()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Clients",
            [
                new(
                    "Client.cs",
                    """
                    partial class Client
                    {
                        readonly int _a = Connect();
                        public Client(int retries) : this() { }
                        public Client() { }
                        static int Connect() => 0;
                    }

                    class Base { public Base() { } public Base(int seed) { } }
                    class Plain { readonly int _c = Pause(); static int Pause() => 0; }
                    class Derived(int seed) : Base(seed) { }
                    class Bare(int seed) : Base { readonly int _seed = seed; }

                    class Factory
                    {
                        void Create() { new Client(1); new Plain(); new Derived(1); new Bare(1); }
                    }
                    """
                ),
                new(
                    "Client.Pause.cs",
                    "partial class Client { readonly int _b = Pause(); static int Pause() => 0; }"
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var create = Code.Methods.Named("Create").In(solution).Single();

        var steps = create
            .Callees(transitive: true)
            .Where(callee => callee.Kind == FunctionKind.Constructor)
            .SelectMany(constructor => constructor.Steps())
            .Select(step =>
                $"{step.Caller.Location.Line}:{step.Caller.Symbol!.ToDisplayString()} -> {step.Target.ToDisplayString()} in {step.Source.Document.Path}"
            );

        Assert.Equal(
            [
                "4:Client.Client(int) -> Client.Client() in Client.cs",
                "10:Plain.Plain() -> Plain.Pause() in Client.cs",
                "11:Derived.Derived(int) -> Base.Base(int) in Client.cs",
                "12:Bare.Bare(int) -> Base.Base() in Client.cs",
                "5:Client.Client() -> Client.Connect() in Client.cs",
                "5:Client.Client() -> Client.Pause() in Client.Pause.cs",
                "5:Client.Client() -> object.Object() in Client.cs",
                "9:Base.Base(int) -> object.Object() in Client.cs",
                "9:Base.Base() -> object.Object() in Client.cs",
            ],
            steps
        );
    }

    [Fact]
    public void Lambdas_and_method_groups_are_followed_only_through_delegates_and_say_so()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [
                new(
                    "A.cs",
                    """
                    class A
                    {
                        void M() { System.Action pause = () => Pause(); Run(Later); }
                        void Run(System.Action action) { }
                        void Later() => System.Threading.Thread.Sleep(1);
                        void Pause() => System.Threading.Thread.Sleep(1);
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var method = Code.Methods.Named("M").In(solution).Single();
        var delegates = CallPaths.Bound.ThroughDelegates();

        var bound = method.Reaches(_sleep);
        var throughDelegates = method.Reaches(_sleep, delegates);
        var path = method.PathTo(_sleep, delegates)!;

        Assert.False(bound);
        Assert.True(throughDelegates);
        Assert.Equal(
            [
                ("M", "Pause", CallEdgeKind.Bound, true),
                ("Pause", "Sleep", CallEdgeKind.Bound, false),
            ],
            path.Steps.Select(step =>
                (step.Caller.Name, step.Target.Name, step.Kind, step.ViaDelegate)
            )
        );
        Assert.False(path.IsBound);
        Assert.Equal(3, path.Location.Line);
    }

    [Fact]
    public void Dispatch_reaches_source_implementations_only_in_views_containing_the_caller()
    {
        var workspace = fixture.Workspace();
        var contracts = """
            public interface IStore { void Save(); int Count { get; } }
            public interface IHandler<T> { void Handle(T message); }
            public abstract class Job { public abstract void Run(); public virtual void Stop() { } public virtual int Size => 0; }
            public sealed class Order { }
            public sealed class Invoice { }
            """;
        var current = workspace.AddProject("Contracts", [new("Contracts.cs", contracts)]);
        var previous = workspace.AddProject(
            "Contracts",
            [new("Contracts.cs", contracts)],
            framework: "net9.0"
        );
        workspace.AddProject(
            "Storage",
            [
                new(
                    "Storage.cs",
                    """
                    class DiskStore : IStore { public void Save() => System.Threading.Thread.Sleep(1); public int Count => 0; }
                    class OrderHandler : IHandler<Order> { public void Handle(Order message) { } }
                    class InvoiceHandler : IHandler<Invoice> { public void Handle(Invoice message) { } }
                    class NightJob : Job { public override void Run() { } public override void Stop() { base.Stop(); } public override int Size => 1; }
                    """
                ),
            ],
            dependencies: [current]
        );
        workspace.AddProject(
            "Legacy",
            [
                new(
                    "Legacy.cs",
                    "class LegacyStore : IStore { public void Save() { } public int Count => 0; }"
                ),
            ],
            framework: "net9.0",
            dependencies: [previous]
        );
        workspace.AddProject(
            "App",
            [
                new(
                    "Service.cs",
                    """
                    class Service
                    {
                        void Save(IStore store) => store.Save();
                        void Handle(IHandler<Order> handler) => handler.Handle(new Order());
                        void Run(Job job) => job.Run();
                        int Count(IStore store) => store.Count;
                        int Size(Job job) => job.Size;
                    }
                    """
                ),
            ],
            dependencies: [current]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var dispatch = CallPaths.Bound.ThroughDispatch();
        var methods = Code
            .Methods.In(solution)
            .Where(method => method.Source.Project.Snapshot.Name is "App" or "Storage")
            .ToDictionary(method => method.Symbol!.ContainingType.Name + "." + method.Name);

        var callees = new[]
        {
            "Service.Save",
            "Service.Handle",
            "Service.Run",
            "NightJob.Stop",
            "Service.Count",
            "Service.Size",
        }
            .Select(name =>
                string.Join(
                    ", ",
                    methods[name]
                        .Callees(paths: dispatch)
                        .Select(callee =>
                            $"{callee.Source.Project.Snapshot.Name}/{callee.Source.Project.TargetFramework}:{callee.Symbol!.ContainingType.Name}.{callee.Name}"
                        )
                )
            )
            .ToArray();
        var bound = methods["Service.Save"].Reaches(_sleep);
        var dispatched = methods["Service.Save"].PathTo(_sleep, dispatch)!;

        Assert.Equal(
            [
                "Contracts/net10.0:IStore.Save, Storage/net10.0:DiskStore.Save",
                "Contracts/net10.0:IHandler.Handle, Storage/net10.0:OrderHandler.Handle, Contracts/net10.0:Order..ctor",
                "Contracts/net10.0:Job.Run, Storage/net10.0:NightJob.Run",
                "Contracts/net10.0:Job.Stop",
                "Contracts/net10.0:IStore.get_Count, Storage/net10.0:DiskStore.get_Count",
                "Contracts/net10.0:Job.get_Size, Storage/net10.0:NightJob.get_Size",
            ],
            callees
        );
        Assert.False(bound);
        Assert.Equal(
            [("Save", "DiskStore", CallEdgeKind.Dispatch), ("Save", "Thread", CallEdgeKind.Bound)],
            dispatched.Steps.Select(step =>
                (step.Caller.Name, step.Target.ContainingType.Name, step.Kind)
            )
        );
    }

    [Fact]
    public void Configured_edges_reach_implementations_in_projects_composed_with_the_caller()
    {
        var workspace = fixture.Workspace();
        var messaging = workspace.AddProject(
            "Messaging",
            [
                new(
                    "Bus.cs",
                    """
                    public static class Bus { public static void Publish(object message) { } }
                    public interface IConsumer { void Consume(); }
                    """
                ),
            ]
        );
        var handlers = workspace.AddProject(
            "Handlers",
            [
                new(
                    "AuditConsumer.cs",
                    "class AuditConsumer : IConsumer { public void Consume() => System.Threading.Thread.Sleep(1); }"
                ),
            ],
            dependencies: [messaging]
        );
        var app = workspace.AddProject(
            "App",
            [new("Api.cs", "class Api { void Post() => Bus.Publish(new object()); }")],
            dependencies: [messaging]
        );
        workspace.AddProject(
            "Host",
            [new("Program.cs", "class Program { }")],
            dependencies: [app, handlers]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var paths = CallPaths.Bound.WithEdge(
            CodeType.Named("Bus").Member("Publish"),
            CodeType.Named("IConsumer").Member("Consume")
        );
        var post = Code.Methods.Named("Post").In(solution).Single();

        var bound = post.Reaches(_sleep);
        var path = post.PathTo(_sleep, paths)!;

        Assert.False(bound);
        Assert.Equal(
            [
                ("Post", "AuditConsumer.Consume", CallEdgeKind.Configured),
                ("Consume", "Thread.Sleep", CallEdgeKind.Bound),
            ],
            path.Steps.Select(step =>
                (
                    step.Caller.Name,
                    $"{step.Target.ContainingType.Name}.{step.Target.Name}",
                    step.Kind
                )
            )
        );
    }

    [Fact]
    public void Boundaries_stop_searches_through_cycles_while_generated_functions_are_followed()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Tests",
            [
                new(
                    "Tests.cs",
                    """
                    class Tests
                    {
                        void A() { B(); }
                        void B() { A(); Helpers.Approved(); }
                        void C() { Helpers.Approved(); Generated.Run(); }
                    }

                    static class Helpers { public static void Approved() => System.Threading.Thread.Sleep(1); }
                    """
                ),
                new(
                    "Generated.g.cs",
                    "static class Generated { public static void Run() => System.Threading.Thread.Sleep(1); }",
                    true
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var approved = CallPaths.Bound.StopAt(function =>
            function.Symbol?.ContainingType.Name == "Helpers"
        );
        var methods = Code.Methods.In(solution).ToArray();

        var stopped = methods.Select(method => (method.Name, method.Reaches(_sleep, approved)));
        var unbounded = methods.Select(method => (method.Name, method.Reaches(_sleep)));
        var generated = methods
            .Single(method => method.Name == "C")
            .Callees(transitive: true, paths: approved)
            .Select(callee =>
                (callee.Name, callee.Source.Document.IsGenerated, callee.Method?.Name)
            );

        Assert.Equal([("A", false), ("B", false), ("C", true), ("Approved", true)], stopped);
        Assert.Equal([("A", true), ("B", true), ("C", true), ("Approved", true)], unbounded);
        Assert.Equal([("Approved", false, "Approved"), ("Run", true, null)], generated);
    }

    [Fact]
    public void Property_targets_match_the_accessors_a_path_runs()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Library",
            [new("A.cs", "class A { System.DateTime M() => System.DateTime.Now; }")]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var method = Code.Methods.Named("M").In(solution).Single();
        var dateTime = CodeType.Named("System.DateTime");

        var now = method.Reaches(dateTime.Member("Now"));
        var utcNow = method.Reaches(dateTime.Member("UtcNow"));

        Assert.True(now);
        Assert.False(utcNow);
    }
}
