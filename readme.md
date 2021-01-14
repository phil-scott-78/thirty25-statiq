# Thirty25.com Blog

This is the source for my blog at <https://thirty25.com> powered by [Statiq](https://statiq.dev).

I've tried to keep it useful for anyone wanting to use this as the base for their own site. Editing `Constants.cs` and getting rid of my content in the `posts` folder should get you started at least.

## Project Organization

Here's the hierarchy of the files and what they are used for

- `/input` - the root of what Statiq will process
  - `/assets` - CSS and other site specific things
  - `/posts` - where the individual posts go.
    - `_layout.cshtml` - post specific layout
    - `_ViewStart.cshtml` - sets all the posts to use the layout. Case is important for vercel!
  - `_layout.cshtml` - the master layout for the site. includes all the html head and site layout
  - `_nextprevious.cshtml` - HTML partial for next and previous links, used by index.cshtml and tags.cshtml
  - `_posts.cshtml` - HTML partial for displaying a list of posts with their title and description, used by index.cshtml and tags.cshtml
  - `_taglist.cshtml` - HTML partial that takes an array of tag strings and converts them into links to the right tag archive
  - `_ViewImports.cshtml` - site wide imports. Case is important for vercel!
  - `_ViewStart.cshtml` - sets default layout for pages. Case is important for vercel!
  - `index.cshtml` - builds the home page and all the paged archives
  - `tags.cshtml` - builds the page with all the tags and also the individual tags archives
- `.editorconfig` - sets editor configuration
- `.gitignore` - standard .NET gitignore with the output of the statiq site (output and public) set to make sure they don't sneak into a push
- `Constants.cs` - contains site wide configuration like the blog's title and author name
- `LICENSE`
- `package.json` - even though we are a .NET app, we'll be using NPM for deploying plus managing tailwind.
- `postcss.config.js` - standard config for tailwind
- `Program.cs` - Bootstraps Statiq
- `readme.md`
- `tailwind.config.js` - There's a lot of customizing of the styles in here, but the important thing is the purge option which will be used during deployment to find the css classes that were used and purge the rest
- `tailwind.css` - used as the base CSS when building the css file in the assets folder. This contains some styling for prism and google fonts plus the usual tailwind
- `thirty25-statiq.csproj` - normally I would never have the csproj in the same folder as the solution, but I can't imagine why I'd have to keep them separate. This makes vercel deployment easier.
- `thirty25-statiq.sln`
- `thirty25-statiq.sln.DotSettings` - just a bunch of ReSharper and Rider editor settings to compliment `.editorconfig` that I have in my project template
- `vercel.json` - highly important, statiq will use smart links (e.g. `/tags` instead of `/tags.html`). By default vercel doesn't enable those so we need to ensure to turn them on.
