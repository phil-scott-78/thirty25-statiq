---
Title: Better Tagging of EF Core Queries
description: How to add tags to your EF Core queries, and how to automatically give those tags better info.
date: 2020-09-02
tags:
    - Entity Framework
repository: https://github.com/thirty25/ef-core-tagging/tree/efcore-5
---

One of the quickest ways to earn the ire of a DBA is to shrug your shoulders, and tell them you don't know what code
created the query that is slowing down their server. Sure, with a little hunting and knowledge of the code base you can
generally spot the offending query, but wouldn't it be nice if the query the DBA is looking at had more information to
track it down?

This is where query tagging comes in. Query tags let you add a comment to the command EF sends to your database
provider. Because this tag is a comment included as part of the command, it will be stored with the statement in your
common DBA tools like SQL Query Store or when viewing them via Extended Events.

With [EF Core 2.2](https://devblogs.microsoft.com/dotnet/announcing-entity-framework-core-2-2/#query-tags) Microsoft
added the `TagWith` extension method. This allows us to write a query such as

```csharp
var result = await bloggingContext.Blogs
    .Where(i => i.Url.StartsWith("http://example.com"))
    .TagWith("Looking for example.com")
    .FirstOrDefaultAsync();
```

Now when you execute your code the following statement, you'll see a comment included with the command

```sql
-- Looking for example.com

SELECT "b"."BlogId", "b"."Url"
FROM "Blogs" AS "b"
WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'http://%')
LIMIT 1
```

Now when an irate DBA sees a misbehaving query they can pass it along hopefully keeping the comment. Now a little find
in files and we should be able to go right there.

But what if we wanted to make it even easier on us?

Using
[caller information](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/caller-information)
attributes we can automatically pull the method, filename, and line number. Given this we can create our own `TagWith`
that accepts these parameters and automatically generates a comment with the source location of whoever is calling the
LINQ query. Our extension method will look similar to this.

```csharp
public static IQueryable<T> TagWithSource<T>(
    this IQueryable<T> queryable,
    [CallerLineNumber] int lineNumber = 0,
    [CallerFilePath] string filePath = "",
    [CallerMemberName] string memberName = "")
{
    return queryable.TagWith($"{memberName}  - {filePath}:{lineNumber}");
}
```

Additionally, `TagWith` can be chained. EF will allow you to call it multiple times and it'll generate a new comment for
each call

```csharp
var result = await bloggingContext.Blogs
    .Where(i => i.Url.StartsWith("http://example.com"))
    .TagWith("Looking for example.com")
    .TagWithSource()
    .FirstOrDefaultAsync();
```

This generated the following command

```sql
-- Looking for example.com

-- Test1  - R:\Projects\thirty25\ef-core-tagging\tests\EfCoreTagging.Tests\UnitTest1.cs:45

SELECT "b"."BlogId", "b"."Url"
FROM "Blogs" AS "b"
WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'http://%')
LIMIT 1
```

Because this pattern is pretty common, I'll also typically create an overload for our `TagWithSource` that takes in a
string

```csharp
public static IQueryable<T> TagWithSource<T>(this IQueryable<T> queryable,
    string tag,
    [CallerLineNumber] int lineNumber = 0,
    [CallerFilePath] string filePath = "",
    [CallerMemberName] string memberName = "")
{
    return queryable.TagWith($"{tag}{Environment.NewLine}{memberName}  - {filePath}:{lineNumber}");
}
```

This will produce similar output as above, but slightly cleaner without the line-break between the two tag statements

```sql
-- Looking for example.com
-- Test1  - R:\Projects\thirty25\ef-core-tagging\tests\EfCoreTagging.Tests\UnitTest1.cs:45

SELECT "b"."BlogId", "b"."Url"
FROM "Blogs" AS "b"
WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'http://%')
LIMIT 1
```

## Limitations

The biggest limitation is that you'll only get the immediate caller of your queries. If you have this buried in a super
abstracted repository there's a good chance every comment will be the same comment pointing to something like the
`Get - Repository.cs:49`. In this case you might want to look at parsing the `StackTrace` to move up the stack and
include all the user code in the path with your tag. I know that we've all been taught to avoid accessing the stack
trace directly because "it's slow", but honestly grabbing the stacktrace and looping through all the frames with file
names and line numbers is around 50 microseconds on my dev machine. We can make a database query that will be measured
in, at best, milliseconds. Personally, in the systems I've worked with this is an acceptable perf loss for the added
benefit, but obviously don't implement it blindly if this code is part of an extremely high traffic site.
