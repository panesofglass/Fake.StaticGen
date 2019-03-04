﻿namespace Fake.StaticGen

open Fake.Core
open Fake.IO

[<AutoOpen>]
module private UrlHelpers =
    let normalizeUrl (url : string) =
        url.Replace("\\", "/").Trim().TrimEnd('/').TrimStart('/').ToLowerInvariant()

    let normalizeRelativeUrl url =
        "/" + (normalizeUrl url)

    let pageUrlToFilePath (url : string) =
        let url = normalizeUrl url
        if System.IO.Path.HasExtension url then url else url + "/index.html"

type Page<'content> =
    { Url : string
      Content : 'content }

type StaticSite<'config, 'content> =
    { BaseUrl : string
      Config : 'config
      Pages : Page<'content> seq
      Files : Page<string> seq }

    member this.AbsoluteUrl relativeUrl =
        this.BaseUrl + "/" + (normalizeUrl relativeUrl)

type ISiteConfig =
    abstract member BaseUrl : string

module StaticSite =
    /// Create a new site with a base url and some configuration
    let fromConfig baseUrl config =
        { BaseUrl = normalizeUrl baseUrl
          Config = config
          Pages = Seq.empty 
          Files = Seq.empty }

    /// Create a new site with a base url and some configuration parsed from a source file
    let fromConfigFile baseUrl filePath parse =
        Trace.tracefn "Reading %s" (Path.toRelativeFromCurrent filePath)
        filePath |> File.readAsString |> parse |> fromConfig baseUrl

    /// Create a new site with some configuration that includes the BaseUrl
    let fromIConfig (config : #ISiteConfig) =
        fromConfig config.BaseUrl config
    
    /// Create a new site with some configuration that includes the BaseUrl 
    /// parsed from a source file
    let fromIConfigFile filePath parse =
        Trace.tracefn "Reading %s" (Path.toRelativeFromCurrent filePath)
        filePath |> File.readAsString |> parse |> fromIConfig

    /// Add a new page
    let withPage content url site =
        let page = { Url = normalizeRelativeUrl url; Content = content }
        { site with Pages = Seq.append site.Pages [ page ] }

    /// Add multiple pages
    let withPages pages site =
        let pages = pages |> Seq.map (fun p -> { p with Url = normalizeRelativeUrl p.Url })
        { site with Pages = Seq.append pages site.Pages }

    /// Parse a source file and add it as a page
    let withPageFromSource sourceFile parse site =
        Trace.tracefn "Reading %s" (Path.toRelativeFromCurrent sourceFile)
        let page = File.readAsString sourceFile |> parse sourceFile
        site |> withPages [ page ]

    /// Parse multiple source files and add them as pages
    let withPagesFromSources sourceFiles parse site =
        let pages = 
            sourceFiles 
            |> Seq.map (fun path -> 
                Trace.tracefn "Reading %s" (Path.toRelativeFromCurrent path)
                path |> File.readAsString |> parse path)
        site |> withPages pages

    /// Add a file
    let withFile content url site =
        let file = { Url = normalizeRelativeUrl url; Content = content }
        { site with Files = Seq.append site.Files [ file ] }

    /// Add multiple files
    let withFiles files site =
        let files = files |> Seq.map (fun f -> { f with Url = normalizeRelativeUrl f.Url })
        { site with Files = Seq.append files site.Files }

    /// Copy a source file
    let withFileFromSource sourceFile url site =
        Trace.tracefn "Reading %s" (Path.toRelativeFromCurrent sourceFile)
        site |> withFile (File.readAsString sourceFile) url

    /// Copy multiple source files
    let withFilesFromSources sourceFiles urlMapper site =
        let files = 
            sourceFiles 
            |> Seq.map (fun path -> 
                Trace.tracefn "Reading %s" (Path.toRelativeFromCurrent path)
                let content = path |> File.readAsString
                { Url = urlMapper path
                  Content = content })
        site |> withFiles files

    /// Create an overview page based on the list of all pages
    let withOverviewPage createOverview site =
        let page = createOverview site
        site |> withPages [ page ]

    /// Create multiple overview pages based on the list of all pages
    let withOverviewPages createOverviewPages site =
        let pages = createOverviewPages site
        site |> withPages pages

    /// Create a paginated overview with a specified number of items per page
    let withPaginatedOverview itemsPerPage chooser createOverviewPages site =
        let chunks =
            site.Pages 
            |> Seq.choose chooser
            |> Seq.chunkBySize itemsPerPage
        site |> withPages (createOverviewPages chunks)

    /// Create an overview file based on the list of all pages, e.g. an RSS feed
    let withOverviewFile createOverview site =
        let file = createOverview site
        site |> withFiles [ file ]

    // Dry run generate, returning a map of file paths and contents, instead of writing them out to disk
    let generateDry outputPath render site =
        site.Pages
        |> Seq.map (fun p ->
            { Url = pageUrlToFilePath p.Url
              Content = p |> render site })
        |> Seq.append site.Files
        |> Seq.map (fun f ->
            let url = normalizeUrl f.Url
            let path = Path.combine outputPath url |> Path.normalizeFileName
            path, f.Content)

    /// Write the site files to the `outputPath`, using the render function to convert the pages into HTML
    let generate outputPath render site =
        Directory.delete outputPath
        site
        |> generateDry outputPath render
        |> Seq.iter (fun (path, content) -> 
            Trace.tracefn "Writing %s" (Path.toRelativeFromCurrent path)
            Directory.ensure (Path.getDirectory path)
            File.writeString false path content)
