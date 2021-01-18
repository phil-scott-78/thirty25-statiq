---
title: DotNet Soddi
description: Introducing DotNet Soddi, a modern CLI tool for working with Stack Overflow archives
date: 2021-01-17
tags:
  - Stack Overflow
  - Soddi
---

Whenever I get into learning a new technology around data access, I always turn to one database to use for learning - the [Stack Exchange archives](https://archive.org/details/stackexchange). These frequently updated archives contain anonymized exports from all the Stack Exchange sites, from [Iota](https://iota.stackexchange.com/) all the way to the main site [Stack Overflow](https://stackoverflow.com/). With hundreds of sites to pick from, I can grab a fresh database of whatever size that matches my need. From 2mb sites all the way to over 100gb of data you can find a database with real data for playing with that fits your size. The schema is simple, well defined and has a variety of real-world data making it perfect for playing with SQL Server indexing strategies or testing out the latest Entity Framework version.

The archives themselves are just huge xml files. Getting these into your database server I've always relied on [Soddi](https://github.com/BrentOzarULTD/soddi) Originally written by Sky Sanders, and now maintained by Brent Ozar's team, it smooths out importing the 7z files into your database server. And it works well.

But I tend to run into the same set of problems every time I want to grab one of these archives.

- For the larger sites, the download times are brutal over HTTP. Torrenting works best, but I typically don't have a torrent client running on my dev machines. And I so infrequently do need a client it seems whatever one I used last has been sold to some dodgy company.
- They are packed as 7z archives. Same as the previous issue, I typically don't have 7-zip installed on my dev machines as I don't really mess with archives that often. Installing 7-zip isn't a big deal, but one I'd like to avoid if possible.
- With the larger databases you need to full expand the archive out before importing it. You need well over 150gb of storage to import that Stack Overflow database
- The existing SODDI application is not terribly intuitive. You need to get your folder structure set up in a particular way. It never feels like I flip the right levers on the first try.
- When I'm messing with the SO databases, I'm usually doing some crazy things with the database so dropping and recreating it is a frequent task.

With that in mind, I read a couple of articles around creating dotnet tools and decided I'd try and create a modern CLI version of SODDI.

## dotnet soddi

![soddi screenshot](2021-01-18-21-42-38.png)

dotnet-soddi is a dotnet tool, so we can install it using the dotnet tool install command. As of right now, it's still in (very) pre-release so a version number will be needed e.g.

```bash
dotnet tool install --global dotnet-soddi --version 0.1.1
```

Once installed there are four main command

- `list`
- `download`
- `torrent`
- `import`

`list` will list all the archives available along with their file size. You can specific a filter to only include a partial list (e.g., soddi list spa) will only include archives with spa in their name.

`download` will download the archive over http. This would be preferred for smaller sized files (less than 10mb) so you don't need to worry about the overhead of bittorrent.

`torrent` will try and use bittorrent to download the archives. For larger archives, especially things like math and the main site, this is almost needed. I'll max out at 35mbps over http, but have seen the torrent download reach up to 600mbps depending on the other clients)

`import` is where the magic happens.

Import will take a given .7z file and simultaneously extract and perform a bulk insert into SQL Server. No need for scratch space for the extracted files, it streams directly to your SQL Server. Not only keeping disk space requirements low, but it also keeps memory usage low. Even a 50gb import rarely reaches over 65mb of memory used on my machine.

By default, import will expect a database to be created and empty. With many of these databases having large sizes, this allows you to fine tune the log and data locations before the import. If you are ok with blindly creating the DB in the default locations, the `-–dropAndCreate` option will take care of that for you. It won't check for disk space or anything ahead of time so use with caution.

During this process, by default keys and indexes will be created as well as a helper table named PostTags. These can be turned off with a switch if so desired.

Once the import is done, you'll have a local copy of the Stack Exchange database of your choice!

## Technical Notes

A few technical notes on the app itself

- Uses [Spectre.Console](https://github.com/spectresystems/spectre.console) for console rendering and CLI input.
- Uses [SharpCompress](https://github.com/adamhathcock/sharpcompress) for extracting 7z files.
- Uses [MonoTorrent](https://github.com/alanmcgovern/monotorrent) for torrent downloading.

It currently extracts on one thread and performs the bulk insert on a second thread. On my machine, it is extremely CPU bound to the extraction. To the best of my knowledge, these archive files cannot be extracted in parallel on different threads. But, if someone smarter than me can figure that out it, perf would be drastically increased on multi-core machines .
