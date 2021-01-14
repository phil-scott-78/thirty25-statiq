---
title: CQRS Repository with EF Core
description: Creating a repository of questionable value around an EF Core DbContext.
date: 2020-08-26
tags:
  - Entity Framework
repository: https://github.com/thirty25/cqrs-ef-core-repository-pattern
---

Chances are the only reason you clicked on this article was to scroll right to the comments so you can call me an idiot.
"`DbSet` IS a repository!" you are here to tell me. Well yes. But maybe, just maybe, we can have a bit of a better
repository. We'll leave all the functionality on the existing `DbSet` and expose it directly. Our main work will be
wrapping up our `DbContext` and adding a little extra to make things a bit easier when working in a CQRS architecture.

## Setting Up A Useless Repository

Our basic querying interface will look like this

```c#
public interface IRepository<TContext> where TContext : DbContext
{
    IQueryable<T> Query<T>() where T : class;
}
```

Because `DbSet` is the repository pattern we just need to expose it.

```c#
public class Repository<TContext> : IRepository<TContext> where TContext : DbContext
{
    private readonly TContext _context;

    public Repository(TContext context) => _context = context;
    public IQueryable<T> Query<T>() where T : class => _context.Set<T>();
}
```

Looking good! Now we'll use our DI framework of choice that supports open generics to wire our interface up. Once that's
done querying will look something along the lines of

```c#
await _repository.Query<Blogs>().FirstAsync(i => i.Id = 123);
```

Two steps back, zero steps forward so far. But this is just the base. Let's solve some pain points.

## Problem One - Tagging

A feature gone unnoticed for many with EF Core 2.2 was the addition of
[Query tags](https://docs.microsoft.com/en-us/ef/core/querying/tags). This allows us to include a comment in the
generated SQL. This may not seem like a huge deal, but once our code makes it to production and our SQL DBA is sending
you questions on some costly query that's showing up in the Query Store we can use the information included in this tag
to track down the problematic code.

Given this linq query

```c#
var context = new BlogContext();
var blogs = await context.Blogs
    .TagWith("Querying all blogs")
    .ToListAsync();
```

With this is the SQL that is executed with our tag

```sql
-- Querying all blogs

SELECT "b"."BlogId", "b"."Url"
FROM "Blogs" AS "b"
```

It's important to remember that this is what's sent to the server. That means it is what will show up a trace session or
the query store. These tags are helpful when you start seeing expensive queries that could come from anywhere in the
app. Well, that is if we remembered to include it.

So, let's include it automatically.

While it's nice to have some text in there what if we included the method name and filename? We can rely on a little
compiler magic and add that to our repository.

```c#
public IQueryable<T> Query<T>(
    [System.Runtime.CompilerServices.CallerMemberName]
    string memberName = "",
    [System.Runtime.CompilerServices.CallerFilePath]
    string sourceFilePath = "",
    [System.Runtime.CompilerServices.CallerLineNumber]
    int sourceLineNumber = 0) where T : class
{
    return _context.Set<T>()
        .TagWith($"{memberName} {sourceFilePath}:{sourceLineNumber}");
}
```

Our new `Query` method now takes 3 optional parameter marked with
[caller information](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/caller-information).
If you aren't familiar with these attributes the compiler will automatically populate those fields with the appropriate
values. Keep in mind that they are populated by the compiler and not at runtime so there won't be a performance penalty.
One gotcha here is we need to make sure our interface also has the same attributes. Without both the interface and
implementation having the interfaces you'll get the default values.

Now when we rerun our our original LINQ query, we generated SQL somewhat like the following

```sql
-- Can_query() C:\projects\repo\RepositoryTests.cs:42

SELECT "b"."BlogId", "b"."Url"
FROM "Blogs" AS "b"
WHERE "b"."BlogId" = 123
LIMIT 1
```

With this change we now automatically get all our queries tagged with their source. `TagWith()` can be chained too,
allowing us to still provide extra details when the need arises.

```c#
var first = await repository.Query(i => i.Blogs)
    .TagWith("Querying blog 123")
    .FirstOrDefaultAsync(i => i.BlogId == 123);
```

produces

```sql
-- Can_query() C:\projects\repo\RepositoryTests.cs:42

-- Querying blog 123

SELECT "b"."BlogId", "b"."Url"
FROM "Blogs" AS "b"
WHERE "b"."BlogId" = 123
LIMIT 1
```

## Problem 2 - Commands and Queries

[Command Query Responsibility Segregation](https://martinfowler.com/bliki/CQRS.html) (CQRS) isn't new, but it has seen a
rise in popularity of late. If you aren't familiar with it, I'll leave getting into the weeds of that pattern for
someone else. For our purposes we'll stick to a super simple idea that an architecture with different code path for when
doing queries and commands is sometimes useful. One way I like to enforce this is with two repositories - a command and
a query repository.

For our purposes we'll split `IRepository<T>` into `ICommandRepository<T>` and `IQueryRepository<T>`. Query repository
stays the same with just a `Query` method. That's all it needs. We won't be updating the data when issuing our queries
so no need for anything else. Our command repository we'll add a simple `SaveChangesAsync()` and `Set<T>` for working
directly with the `DbSet` and persisting the data.

```c#
public Task SaveChangesAsync() => _context.SaveChangesAsync();
public DbSet<T> Set<T>() where T : class => _context.Set<T>();
```

Save changes doesn't allow tagging, unfortunately, so our implementation is dead simple. Now our code to create and
persist an item would look something like this

```csharp
var blog = new Blog()
{
    Url = "http://example.com/rss.xml",
    BlogId = 123
};

await commandRepository.Set<Blog>.AddAsync(blog);
await commandRepository.SaveChangesAsync();
```

Not a ton of value being added, even though it is nice having two implementations that are explicit on their use. But
there is one EF detail that we can include in our query repository. Our `QueryRepository` will never return data that
will be used for updates in a `DbContext`. We don't even have a method to persist it if we wanted. With EF when you are
querying data it makes sense to also call `AsNoTracking()`. By default,
[EF will include tracking details](https://docs.microsoft.com/en-us/ef/core/querying/tracking) when we query. This data
can become quite cumbersome over time and introduces a performance penalty when all we want is read-only data. When we
are using our `QueryRepository` that is precisely what we are doing so let's include it. Now our implementation while
simple is providing a much better foundation for queries.

```c#
public IQueryable<T> Query<T>(
    [System.Runtime.CompilerServices.CallerMemberName]
    string memberName = "",
    [System.Runtime.CompilerServices.CallerFilePath]
    string sourceFilePath = "",
    [System.Runtime.CompilerServices.CallerLineNumber]
    int sourceLineNumber = 0) where T : class
{
    return _context.Set<T>()
        .AsNoTracking()
        .TagWith($"{memberName}() {sourceFilePath}:{sourceLineNumber}");
}
```

## Problem 3 - DbContextFactory Management

With EF Core 5 preview 7 Microsoft
[shipped a context factory](https://devblogs.microsoft.com/dotnet/announcing-entity-framework-core-ef-core-5-0-preview-7/#dbcontextfactory)
out of the box with EF Core. Many people have rolled their own implementation, but if you haven't ran across this
pattern the added benefit of using a factory comes when you are relying on dependency injection. Take for example a
controller taking in a `DbContext` as a dependency.

In our scenario maybe we are doing some basic validation on the command that is requested. If it doesn't meet our rules
we send back a `BadRequest` without touching the database. But if our constructor is asking for `DbContext` that means
ASP.NET is gonna build up an instance even though we don't need one. Even for a simple context this can be at least 20ms
of time and allocations we don't need. By using a factory ASP.NET will give us an instance of the factory, which by
default is a singleton. So in our `BadRequest` example there would be now zero allocations and no time spent building
the context.

The downside is we must now manage the lifetime of the context. This can be done pretty simply with an `using`
statement, but that is still one more thing to worry about while writing code and doing code reviews. If we've gone
through the trouble of creating our repository we might as well do this once here.

Now our constructor of our repository will look something like this

```c#
private readonly Lazy<TContext> _context;

public CommandRepository(IDbContextFactory<TContext> contextFactory)
{
    _context = new Lazy<TContext>(contextFactory.CreateDbContext);
}
```

Because `_context` is now lazy we'll need to adjust our instance methods to use the `.Value` property

```c#
public Task SaveChangesAsync() => _context.Value.SaveChangesAsync();
public DbSet<T> Set<T>() where T : class => _context.Value.Set<T>();
public DbSet<T> Set<T>(Func<TContext, DbSet<T>> action) where T : class
    => action.Invoke(_context.Value);
```

and we'll also need to dispose because that's the whole reason we are going through this trouble

```c#
public void Dispose()
{
    if (_context.IsValueCreated) _context.Value.Dispose();
}
```

Assuming we adjust our container properly we shouldn't have to adjust any of our code. A more complete example might
toss the `Lazy` altogether and reduce our allocations even further.

## Pros and Cons

Obviously this usage isn't for everyone. But I find by restricting the surface of `DbContext` on commands plus enhancing
`IQueryable` with our tags and automatic `AsNoTracking` it produces more consistent code.

### Pros

- Automatic tagging of all queries with member name, source file and line number for debugging in SQL tools.
- Automatic disabling of tracking information in code paths that are query only.
- No temptation to include commands in query only code path. You must be explicit with the command repository.
- Command repositories have enforced standard code paths. E.g. without adding `Add` method to the repository interface
  developers will need to be consistent and use the `DbSet` implementation.
- We aren't hiding `DbSet` or `IQueryable` allowing the devs to work with EF in an optimal fashion and not have to
  write a ton of boiler plate `Get`, `GetAll`, `GetById`, etc implementations of your typical `IRepository`
  implementation around EF.

### Cons

- As you need more functionality exposed from the `DbContext` you may find yourself expanding out the interfaces.
  Ideally you can leave these two interfaces slim and add new services when needing to work with underlying
  `DbContext` details.
- If you are used to using `IRepository` because it makes mocking your data access easier than mocking out EF's
  objects this won't help you as you'll still need to mock `DbSet`.
- You look like a crazy person for wrapping a perfectly good repository pattern.

## Example Code Notes

The code in the repository is configured using Lamar as a container with a SQLite backend to demonstrate what would be
closer to real world usage. It also expands upon the repositories to include a `Set` and `Query` method that accept a
lambda to allow code such as `Query(i => i.Blogs)` for better discoverability of the context's `DbSet` members.

To demonstrate the `IDbContextFactory` we are targeting EF Core 5 preview bits which require .NET Standard 2.1. The CQRS
portion works fine in EF Core 3 as long as you rework the constructor to take a `DbContext` directly.
