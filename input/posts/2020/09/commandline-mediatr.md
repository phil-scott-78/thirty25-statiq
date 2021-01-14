---
Title: Command Line Verbs and MediatR
description: Using MediatR to process parsed command line verbs
date: 2020-09-09
tags:
  - MediatR
  - commandlineparser
repository: https://github.com/thirty25/commandline-mediatr
---

Verbs are a way to split our console application in distinct actions within an app. If you've used the git command line
you've encountered them. `git clone`, `git commit`, etc are all each verbs. Each one of those verbs has its own set of
options and even help text. It's like having many command line apps within one master application.

With a .NET application we have a lot of different ways to tackle creating a console app that supports verbs, but my go
to choice is [CommandlineParser](https://github.com/commandlineparser/commandline). Their
[documentation on verbs](https://github.com/commandlineparser/commandline/wiki/Verbs) has a good write up on how to get
started.

I'll include a few examples, but I'll keep them short because I'm gonna go a totally different route later. To define
the verb with `CommandLineParser` you create a class and mark it with the `[Verb]` attribute. Their example looks like
this

```csharp
[Verb("add", HelpText = "Add file contents to the index.")]
class AddOptions
{
     // normal options here
}

[Verb("commit", HelpText = "Record changes to the repository.")]
class CommitOptions {
     // normal options here
}

[Verb("clone", HelpText = "Clone a repository into a new directory.")]
class CloneOptions {
     // normal options here
}
```

To parse and execute the selected verb you'd use `Parser`'s object `ParseArguments` function passing in your options and
the methods you want to execute for each option.

```csharp
static int Main(string[] args) {
    Parser.Default.ParseArguments<AddOptions, CommitOptions, CloneOptions>(args)
        .WithParsed<AddOptions>(options => ExecuteAdd(options, fileSystem))
        .WithParsed<CommitOptions>(options => ExecuteCommit(options))
        .WithParsed<CloneOptions>(options => ExecuteClone(options, fileSystem, httpClient))
        .WithNotParsed(errors => ...)
}
```

That's not terrible. Most people end up creating a new method for each verb and begin their processing from there. This
is perfectly fine for a lot of apps, but we can do better.

When I look at the above example, I see the following issues:

- Every time I add a verb, I need to remember to adjust the `Main` method to add my new option.
- I'd like to keep the code for executing my verbs close to my verb definition rather than tying it to the parser.
- Handling dependencies could be better. With my simple example above I have an instance of `fileSystem` and
  `httpClient` built but I'm not using them in all code paths.
- I'd like to be able to test my verbs without needing to run the application. Right now I could make the `Execute...`
  methods public and call those, but I'd like to be closer to the same path `Program.Main` takes.

Looking at the code there is a definite pattern arising. Take message, in this case the options, and pass it to the
appropriate handler. If you've worked on a modern ASP.NET code base of late (or read the title of this blog post) you'll
know where I'm going next

## This calls for MediatR

[MediatR](https://github.com/jbogard/MediatR) for those unfamiliar is a simple implementation of the
[mediator pattern](https://refactoring.guru/design-patterns/mediator). It's extremely common in larger ASP.NET
applications, especially those using CQRS. But nothing says we can't use it in a command line app too. Since MediatR
will want a service location we'll also add dependency injection into our console application. This will require a
little bit more work up front, but it'll provide the value as we add and test each new verb.

For my project I'm going to use [Lamar](https://jasperfx.github.io/lamar/) as my container. There is nothing special
Lamar is bringing to the table over any other IoC container so feel free to use your tool of choice. You should be able
to mold one of the [projects from the MediatR samples libraries](https://github.com/jbogard/MediatR/tree/master/samples)
to get your registration correct.

## Setting up our Console App

We'll create a new console application and add the following packages

```shell
dotnet add package Lamar
dotnet add package MediatR
dotnet add package CommandLineParser
```

Now we need to create our project's verbs. We'll stick with the `CommandLineParser` example and add a couple of verbs so
we can see them in action. Add these classes each in their own in a corresponding file

```csharp
// e.g. consoleapp add myfile.txt
[Verb("add", HelpText = "Add file contents to the index.")]
public class AddOptions
{
    [Value(0, MetaName = "Path", Required = true)]
    public string Path { get; set; }
}


// e.g. consoleapp commit -m "my message"
[Verb("commit", HelpText = "Record changes to the repository.")]
public class CommitOptions
{
    [Option('m', "Message", HelpText = "Message for commit")]
    public string Message { get; set; }
}

// e.g. consoleapp clone http://example.com/repo
[Verb("clone", HelpText = "Clone a repository into a new directory.")]
public class CloneOptions
{
    [Value(0, MetaName = "Url", Required = true)]
    public string Url { get; set; }
}
```

If you've worked with `CommandLineParser` in the past, there isn't anything new here. But we do need to tell MediatR
that we'll be using them. We'll need to make each of these classes implement `IRequest<int>`. This tells MediatR that
this is a request that will return an int.

Now our options look should be looking like this.

```csharp
public class CommitOptions : IRequest<int> {/*...*/}
public class AddOptions : IRequest<int> {/*...*/}
public class CloneOptions : IRequest<int> {/*...*/}
```

Now that MediatR knows what our requests look like, we need to add a handler for each one of them. I prefer to create
the handler side by side with the request in one single file. The syntax for your handler along with the request will
give you one file that looks like this

```csharp
// e.g. consoleapp commit -m "my message"
[Verb("commit", HelpText = "Record changes to the repository.")]
public class CommitOptions
{
    [Option('m', "Message", HelpText = "Message for commit")]
    public string Message { get; set; }
}

public class CommitHandler : IRequestHandler<CommitOptions, int>
{
    public async Task<int> Handle(CommitOptions request, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }
}
```

The `Handle` method is part of the interface of `IRequestHandle`. We'll create a handler for each one of them. You'll
notice that there will be a parameter named `request` with the options we need for our verb. Perfect! Now each of our
verbs have a standard handler. We just need a way for MediatR to actually work.

## Configure our Container

Our next step is going to be building our container. I'll be using Lamar but your code should look similar to this if
you copy and paste our of the MediatR examples correctly

```csharp
private static Container BuildContainer()
{
    return new Container(cfg =>
    {
        cfg.Scan(scanner =>
        {
            scanner.AssemblyContainingType<Program>();
            scanner.ConnectImplementationsToTypesClosing(typeof(IRequestHandler<,>));
            scanner.ConnectImplementationsToTypesClosing(typeof(INotificationHandler<>));
        });

        cfg.For(typeof(IPipelineBehavior<,>))
            .Add(typeof(RequestPreProcessorBehavior<,>));
        cfg.For(typeof(IPipelineBehavior<,>))
            .Add(typeof(RequestPostProcessorBehavior<,>));

        cfg.For<IMediator>().Use<Mediator>().Transient();
        cfg.For<ServiceFactory>().Use(ctx => ctx.GetInstance);
    });
}
```

Let's walk through this. A lot of this code isn't needed to get our examples running, but we'll want to use it because
it does configure some MediatR functionality that may prove useful in the future.

We are telling Lamar to scan the assembly with our Program looking for any classes that implement `IRequestHandler<,>`
and `INotificationHandler<>`. The later is currently unused and could be deleted, but I like to leave it enabled to make
sure future devs that see MediatR is configured and expect it to work. For more information on Notifications checkout
[their documentation](https://github.com/jbogard/MediatR/wiki#notifications).

The lines about behaviors

```csharp
cfg.For(typeof(IPipelineBehavior<,>))
    .Add(typeof(RequestPreProcessorBehavior<,>));
cfg.For(typeof(IPipelineBehavior<,>))
    .Add(typeof(RequestPostProcessorBehavior<,>));
```

configure the MediatR pipeline. Like the notification handlers, we aren't using these
[built-in behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors#built-in-behaviors) now, but I'll leave them for
future devs to have less hurdles when implementing.

The last two lines

```csharp
cfg.For<IMediator>().Use<Mediator>().Transient();
cfg.For<ServiceFactory>().Use(ctx => ctx.GetInstance);
```

tell lamar to wire up requests for `IMediator` to an instance of `Mediator` and also wires up requests for MediatR's
`ServiceFactory` to an instance of our container. With that MediatR knows about our container which in turn knows about
our handlers.

## Let's Actually Send in a Command

With all this in place, let's finally execute some commands. I'll add a few `Console.WriteLine` statements to my
handlers so we can see them in action. For example, I'll add an a handler for `AddOptions` that prints out the path
requested

```csharp
public class AddHandler : IRequestHandler<AddOptions, int>
{
    public Task<int> Handle(AddOptions request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Adding \"{request.Path}\"");
        return Task.FromResult(0);
    }
}
```

Back in `Program.cs` we need to execute these commands. Step one is finding them dynamically. If you remember from way
back at the start of the blog post, `CommandLineParser` took a list of generic options in to give us that strongly typed
syntax. Thankfully, it still does things old school and accepts an array of `Type` too. All we need to do is find all
the types that implement `IRequest<int>` and we can dynamically add them.

```csharp
var commands = typeof(Program)
    .Assembly
    .GetTypes()
    .Where(t => t.GetInterfaces().Contains(typeof(IRequest<int>)))
    .ToArray();
```

Now with our array of types we change our call to parse to simply

```csharp
var parserResult = Parser.Default.ParseArguments(args, commands);
```

With the untyped `ParseArguments` method, it will do two things depending on the `args` we send in. If it finds the
arguments aren't valid it'll be of type `NotParsed<object>`. If it has a valid parsing, it'll return `Parsed<object>`
with a property of `Value` matching our options. We then use that property value to send to `MediatR` for handling.

I'll wrap this code up in a method named `GetVerb` that takes the command line args and returns the request. If we have
an invalid argument we'll throw a custom exception named `CommandLineParsingException`.

```csharp
public static IRequest<int> GetVerb(string[] args)
{
    var commands = typeof(Program)
        .Assembly
        .GetTypes()
        .Where(t => t.GetInterfaces().Contains(typeof(IRequest<int>)))
        .ToArray();

    var parserResult = Parser.Default.ParseArguments(args, commands);
    if (parserResult is Parsed<object> parser && parser.Value is IRequest<int> request)
    {
        return request;
    }

    throw new CommandLineParsingException(parserResult);
}
```

We use C# 8 patterns matching to check for a properly parsed set of options that match one of our verbs.

This leaves us with our `Main`

```csharp
public static async Task<int> Main(string[] args)
{
    try
    {
        var command = GetVerb(args);

        var mediator = BuildContainer().GetInstance<IMediator>();
        return await mediator.Send(command);
    }
    catch (CommandLineParsingException e)
    {
        Console.WriteLine(HelpText.AutoBuild(e.ParserResult));
        return await Task.FromResult(1);
    }
}
```

After we get our verb, we'll build our container, ask for an instance of `IMediator` and send our request in. If we
built our container properly we should have handlers built that match our options. Since these handlers all return an
integer we'll use that as the return code for our application and exit.

We can now run our commands and see some output

```shell
CommandLineApp clone http://example.com
```

should output something like

```shell
Cloning "http://example.com"
```

Ok, that's cool. Let's revisit our issues and see how we are doing with those.

### Every time I add a verb I need to remember to adjust the `Main` method to add my new option

To add a new verb we just need to add a new options that implements `IRequest<int>` and a corresponding
`IRequestHandler`. Add those and it'll be wired up automatically.

### I'd like to keep the code for executing my verbs close to my verb definition

Yup, this is an obvious win as long as you aren't super strict about the one class per file. Some work around this rule
by making both the Request and Request Handler a nested class in a master one (e.g. a class named `Add` with child
classes named `Options` and `Handler`)

### Handling dependencies could be better

We really didn't touch on this yet. But because we are relying on an IoC container to build our handler this is a
breeze. It'll work just like if you were writing an ASP.NET Controller. Say for example you wanted to use
`System.IO.Abstraction` to abstract out file system access.

We'd add this to our `BuildContainer` method

```csharp
cfg.For<IFileSystem>().Use<FileSystem>();
```

With this in place we can now use standard DI to get our instance of `IFileSystem` in our handler.

```csharp
public class AddHandler : IRequestHandler<AddOptions, int>
{
    private readonly IFileSystem _fileSystem;

    public AddHandler(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Task<int> Handle(AddOptions request, CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(request.Path))
        {
            throw new PathForAddNotFoundException(request.Path);
        }

        Console.WriteLine($"Adding \"{request.Path}\"");
        return Task.FromResult(0);
    }
}
```

We can follow this pattern and move all our handler's dependencies into the container just like we would with an ASP.NET
application.

### I'd like to be able to test my verbs without needing to run the application

With our execution moved into a consistent pattern unit testing now becomes a lot easier. Even easier now that we have
also moved our dependencies out of the handler too. Now, for example, to test the AddHandler we can create an instance
of it like any other class. For example, to test the above `AddHandler` we can use
`System.IO.Abstractions.TestingHelpers` to mock a file system and verify we get a proper exception with an invalid path.

```csharp
public class AddVerbTests
{
    [Fact]
    public async Task Add_throws_exception_with_invalid_path()
    {
        var mockFileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData> {
                {"output.txt", new MockFileData("my data")}
            }
        );

        var addHandler = new AddHandler(mockFileSystem);

        await Should.ThrowAsync<PathForAddNotFoundException>(async () =>
        {
            await addHandler.Handle(new AddOptions() {Path = "notfound.txt"}, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Add_returns_with_success_for_valid_path()
    {
        var mockFileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData> {
                {"output.txt", new MockFileData("my data")}
            }
        );

        var addHandler = new AddHandler(mockFileSystem);
        await addHandler.Handle(new AddOptions() {Path = "output.txt"}, CancellationToken.None);
    }
}
```

We can also test our command line without executing a line of code.

```csharp
public class CommandLineParsingTests
{
    [Fact]
    public void Can_map_commit_option()
    {
        var verb = Program.GetVerb(new[] {"commit", "-m", "This is my commit message"});
        verb.ShouldBeOfType<CommitOptions>();
        ((CommitOptions)verb).Message.ShouldBe("This is my commit message");
    }

    [Fact]
    public void Invalid_command_line_parsing_fails()
    {
        Should.Throw<CommandLineParsingException>(() =>
        {
            Program.GetVerb(new[] {"test", "http://example.com"});
        });
    }
}
```

By being able to test the syntax of your commands what options are being created you can quickly troubleshoot issues
with command line syntax errors without needing to execute commands.

## Is This All Worth It?

Much like adding MediatR to an ASP.NET Core app, we need to weight the pros and cons of using it in a console app.

### Pros

- Consistent coding structure. By using the mediator pattern we have a consistent structure to each of our commands
  from how they are defined all the way to how they are executed.
- Testability. With each verb in its own handler testing the code outside of `Program.Main` becomes much easier.
- Leverage existing code. I've added small command line apps that support larger ASP.NET Core apps. With so much
  infrastructure configuration being done via our container configuration with ASP.NET being able to have a shared
  container can sometimes prove quite valuable.
- Easy of maintenance. To add a new command we don't need to mess with the entry point of the application.
- Extensibility. Just like using MediatR in ASP.NET, using it in a console app allows new extensibility options. For
  example we could add [FluentValidation](https://fluentvalidation.net/) as a behavior to have a consistent validation
  story. Or we could configure our DI container differently if a `--dry-run` parameter is passed.

### Cons

- More complexity in starting a new project. Not gonna lie, I had to copy and paste from some code I wrote a while ago
  on how to pull a generic object out of the command line parser. Thankfully we only need to write this one.
- For simple apps dealing with an IoC container is just silly. Creating a new instance of a class or even just
  leveraging static methods is just fine for small console apps. No need for all this ceremony.
- Unfamiliar code structure. Those who haven't encountered MediatR struggle with figuring out how it all ties
  together. There is no right click and "Go To Handler" in Visual Studio that tells you what executes next, and stack
  traces can get out of control.

All those points being equal, my breaking point for going this route is the addition of verbs to my console app. If our
console app has one path (or a UI) then MediatR just gets in the way. But once we start growing and adding verbs the
work at first pays off every time we add a new on.
