---
Title: Better Tagging of EF Core Queries with .NET 6
description: How to add tags to your EF Core queries, and how to automatically give those tags better info.
date: 2021-12-10
tags:
    - Entity Framework
    - net6
repository: https://github.com/thirty25/ef-core-tagging
---

**Note:** This is an updated version of a [previous post](../../2020/09/tagging-query-with-ef-core/) that extends the functionality using .NET 6.

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

The [previous post on tagging](../../2020/09/tagging-query-with-ef-core/) introduced my `TagWithSource` that extended this functionality by automatically including the caller information.

Well, a year later EF Core 6 now includes [`TagWithCallSite`](https://docs.microsoft.com/en-us/ef/core/what-is-new/ef-core-6.0/whatsnew#tag-queries-with-file-name-and-line-number) so this functionality is built in.

```csharp
var result = await bloggingContext.Blogs
    .Where(i => i.Url.StartsWith("https://"))
    .Take(5)
    .OrderBy(i => i.BlogId)
    .TagWithCallSite()
    .ToListAsync();
```

This will now include the file (but no method name) in the query, similar to my previous post.

```sql
-- File: R:\thirty25\ef-core-tagging\tests\EfCoreTagging.Tests\UnitTest1.cs:46

SELECT "t"."BlogId", "t"."Url"
FROM (
    SELECT "b"."BlogId", "b"."Url"
    FROM "Blogs" AS "b"
    WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'https://%')
    LIMIT @__p_0
) AS "t"
ORDER BY "t"."BlogId"
```

But, we can stay one step of ahead of Microsoft. Let's include a bit more info. .NET 6 also introduced 
[CallerArgumentExpression](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/caller-argument-expression.md)
which allows us to even include the expression that called our method.

With the addition of this attribute, we can create our own `TagWith`
that in addition to the  the source location, we can also pull the expression calling the
LINQ query. Our extension method will look similar to this:

```csharp
public static IQueryable<T> TagWithSource<T>(this IQueryable<T> queryable,
    string tag = default,
    [CallerLineNumber] int lineNumber = 0,
    [CallerFilePath] string filePath = "",
    [CallerMemberName] string memberName = "",
    [CallerArgumentExpression("queryable")]
    string argument = "")
{
    // argument could be multiple lines with whitespace so let's normalize it down to one line
    var trimmedLines = string.Join(
        string.Empty,
        argument.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(i => i.Trim())
    );

    var tagContent = string.IsNullOrWhiteSpace(tag)
        ? default
        : tag + Environment.NewLine;

    tagContent += trimmedLines + Environment.NewLine + $" at {memberName}  - {filePath}:{lineNumber}";

    return queryable.TagWith(tagContent);
}
```

This allows us to include an optionally custom tag text, plus automatically include the method name, file, file number and now thanks to the addition of `CallerArgumentExpression` 
we get the full LINQ statement too. 

```sql
-- bloggingContext.Blogs.Where(i => i.Url.StartsWith("https://")).Take(5).OrderBy(i => i.BlogId)
--  at Test1  - R:\thirty25\ef-core-tagging\tests\EfCoreTagging.Tests\UnitTest1.cs:46

SELECT "t"."BlogId", "t"."Url"
FROM (
    SELECT "b"."BlogId", "b"."Url"
    FROM "Blogs" AS "b"
    WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'https://%')
    LIMIT @__p_0
) AS "t"
ORDER BY "t"."BlogId"

```

So the first line is our call with the whitespace normalized out. Second line is what we could get with our previous helper or the built in statement. Pretty cool!

There are some gotchas. Because of the `CallerArgumentExpression` addition, order will matter. Typically `TagWith` or `TagWithSiteCaller` can be placed anywhere in the LINQ chain. They are only bringing in a file name and a line number. But, because we are going to want to include everything in the 
LINQ chain, the only way the compiler will know this is if we place the `TagWithSource` call at the end. Because of this, it might make sense to add some helpers that also wrap `ToListAsync`, `FirstOrDefaultAsync` and other final LINQ operators to ensure it is called in the correct spot. 

This is easier said than done. We unfortunately can't just wrap our call to `TagWithSource` like so.

```csharp
public static async Task<List<T>> ToListWithSourceAsync<T>(this IQueryable<T> queryable, CancellationToken cancellationToken = default)
{
    return await queryable
        .TagWithSource()
        .ToListAsync(cancellationToken);
}
```        

The result of this call will use this helper as the source when we call `TagWithSource`. Note that file is actually our extension and
the only member we know about is `queryable`. Without reflection we can't go up the call stack.

```sql
-- ToListWithSourceAsync  - R:\thirty25\ef-core-tagging\src\EfCoreTagging.Data\IQueryableTaggingExtensions.cs27
-- queryable

SELECT "t"."BlogId", "t"."Url"
FROM (
    SELECT "b"."BlogId", "b"."Url"
    FROM "Blogs" AS "b"
    WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'https://%')
    LIMIT @__p_0
) AS "t"
ORDER BY "t"."BlogId"
```

To work around this, you have to include all the `Caller` attributes on your `ToListAsync` wrapper. A nice bonus here is that because
we are controlling the last call in the chain, we can also include this if we want.

 After moving the formatting code into it's helper, the method looks like

```csharp
public static async Task<List<T>> ToListWithSourceAsync<T>(this IQueryable<T> queryable, 
    string tag = default,
    [CallerLineNumber] int lineNumber = 0,
    [CallerFilePath] string filePath = "",
    [CallerMemberName] string memberName = "",
    [CallerArgumentExpression("queryable")] string argument = "",
    CancellationToken cancellationToken = default)
{
    return await queryable
        .TagWith(GetTagContent<T>(tag, lineNumber, filePath, memberName,
            $"{argument}.{nameof(ToListWithSourceAsync)}()"))
        .ToListAsync(cancellationToken);
}

```

and this is called by

```csharp
var result = await bloggingContext.Blogs
    .Where(i => i.Url.StartsWith("https://"))
    .Take(5)
    .OrderBy(i => i.BlogId)
    .ToListWithSourceTagAsync();
```

Now we not only get the correct tag with the proper expression, but we also get our addition call to `ToListWithSourceTagAsync` method included

```sql
-- bloggingContext.Blogs.Where(i => i.Url.StartsWith("https://")).Take(5).OrderBy(i => i.BlogId).ToListWithSourceAsync()
--  at Test1_WithToList  - R:\thirty25\ef-core-tagging\tests\EfCoreTagging.Tests\UnitTest1.cs:67

SELECT "t"."BlogId", "t"."Url"
FROM (
    SELECT "b"."BlogId", "b"."Url"
    FROM "Blogs" AS "b"
    WHERE "b"."Url" IS NOT NULL AND ("b"."Url" LIKE 'https://%')
    LIMIT @__p_0
) AS "t"
ORDER BY "t"."BlogId"
```

It could get rather tedious to include them all, but this might prove worth a bit of copy and paste (or a T4 template) to get you there. 

## Notes

While poking around the source of Microsoft's `TagWithCallSite` source, I noticed they used the attribute [`NotParameterized`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.query.notparameterizedattribute?view=efcore-5.0) on the tag and caller info. Only information I can find states that this "signals that custom LINQ operator parameter should not be parameterized during query compilation." This sounds like a good optimization to also include, so the full call now looks more like this when you view the repository.

```csharp
 public static IQueryable<T> TagWithSource<T>(this IQueryable<T> queryable,
    [NotParameterized] string tag = default,
    [NotParameterized] [CallerLineNumber] int lineNumber = 0,
    [NotParameterized] [CallerFilePath] string filePath = "",
    [NotParameterized] [CallerMemberName] string memberName = "",
    [NotParameterized] [CallerArgumentExpression("queryable")]
    string argument = "")
{
    return queryable.TagWith(GetTagContent<T>(tag, lineNumber, filePath, memberName, argument));
}
```

Additionally, because we are including comments that can change between compiles, this will cause a miss on the plan cache after a deployment for possibly many queries. 
You might want to run this by the DBA. Personally I think this is an acceptable risk, but if this is an extremely critical path it is worth noting. 