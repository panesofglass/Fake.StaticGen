#r "paket:
nuget FSharp.Core 4.5.2 // Locked to be in sync with FAKE runtime
nuget Fake.Core.SemVer
nuget Fake.Core.Target 
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem
nuget Fake.Tools.Git //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.Tools

let [<Literal>] solution = "Fake.StaticGen.sln"
let packagesLocation = Path.combine __SOURCE_DIRECTORY__ "packages"

module Version =
    let withPatch patch version =
        { version with Patch = patch; Original = None }

    let withPrerelease pre version =
        let pre = pre |> Option.bind (fun p -> PreRelease.TryParse p)
        { version with PreRelease = pre; Original = None }

    let getVersion () = 
        Trace.trace "Determining version based on Git history"
        let repo = "."
        let version = File.readAsString "version" |> SemVer.parse

        let height =
            let versionChanged =
                Git.FileStatus.getChangedFilesInWorkingCopy repo "HEAD" 
                |> Seq.exists (fun (_, f) -> f = "version")
            
            if versionChanged then 
                0
            else
                let previousVersionChange = Git.CommandHelper.runSimpleGitCommand repo "log --format=%H -1 -- version"
                let height = Git.Branches.revisionsBetween repo previousVersionChange "HEAD"
                if Git.Information.isCleanWorkingCopy repo then height else height + 1

        let preview = 
            let branch = Git.Information.getBranchName repo
            if branch = "master" then 
                None 
            else 
                let commit = Git.Information.getCurrentSHA1 repo |> fun s -> s.Substring(0, 7)
                let dirty = if Git.Information.isCleanWorkingCopy repo then "" else "-dirty"
                Some (branch + "-" + commit + dirty)

        version |> withPatch (uint32 height) |> withPrerelease preview

    let version = lazy getVersion ()

[<AutoOpen>]
module MSBuildParamHelpers =
    let withVersion version (param : MSBuild.CliArguments) =
        { param with Properties = ("Version", string version)::param.Properties }

    let withNoWarn warnings (param : MSBuild.CliArguments) =
        { param with 
            NoWarn = 
                param.NoWarn 
                |> Option.defaultValue []
                |> List.append warnings
                |> Some }

    let withDefaults version =
        withVersion version >> withNoWarn [ "FS2003" ]

Target.create "Clean" <| fun _ ->
    DotNet.exec id "clean" solution |> ignore
    DotNet.exec id "clean" (sprintf "%s -c Release" solution) |> ignore
    Directory.delete packagesLocation

Target.create "Build" <| fun _ ->
    let version = Version.version.Value
    DotNet.build (fun o -> { o with MSBuildParams = o.MSBuildParams |> withDefaults version }) solution

Target.create "Pack" <| fun _ ->
    let version = Version.version.Value
    solution |> DotNet.pack (fun o -> 
        { o with 
            MSBuildParams = o.MSBuildParams |> withDefaults version 
            OutputPath = Some packagesLocation }) 

"Build" ==> "Pack"

Target.runOrDefault "Pack"
