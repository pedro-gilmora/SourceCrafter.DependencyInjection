param(
    [Parameter(Mandatory=$false)]
    [string]$clean = "true",
    [Parameter(Mandatory=$false)]
    [string]$pack = "true",
    [Parameter(Mandatory=$false)]
    [string]$updateVersion = "true",
    [Parameter(Mandatory=$false)]
    [string]$forcePack = "false",
    [Parameter(Mandatory=$false)]
    [string]$test = "false",
    [Parameter(Mandatory=$false)]
    [string]$startingYear = "2024"
)

function Get-Version {
    # Calculate the first part of the version
    $part1 = [System.Convert]::ToUInt16([System.DateTime]::Now.Year - [System.Int32]::Parse($startingYear))

    # Get the second part of the version
    $part2 = [System.DateTime]::Now.ToString('yy')

    # Get the third part of the version
    $part3 = [System.DateTime]::Now.DayOfYear

    # Calculate the fourth part of the version
    $part4 = [System.Convert]::ToUInt16([System.DateTime]::Now.TimeOfDay.TotalMinutes / 15)

    # Combine all parts into a version string
    $version = "$part1.$part2.$part3.$part4"

    return $version
}

Set-Location $PSScriptRoot

$testProjPath = "$PSScriptRoot/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj"

$testProjContent = [xml]$(Get-Content $testProjPath)

# Get all 'PackageReference' nodes
$refs = $($testProjContent).GetElementsByTagName('PackageReference').
    Where({ 
        $_.GetAttribute('Include').StartsWith('SourceCrafter.DependencyInjection') -and $_.GetAttribute('Version') -ne $version
    })

$version = Get-Version

Write-Host "CONFIG: version = $version, clean = $clean, pack = $pack, forcePack = $forcePack, test = $test, startingYear = $startingYear
"

if($refs.Count -gt 0 -or $forcePack -eq 'true')
{    
    $refs.Foreach({ 
        Write-Output "
PACKER: Updating package: $($_.GetAttribute('Include')) from version $version"
        $_.SetAttribute('Version', $version)
        Write-Output $_.OuterXml
    })

    $testProjContent.Save($testProjPath)

    Write-Output "
PACKER: Test project references where updated
"
    if(-not (Test-Path "$PSScriptRoot/packaging/"))
    {
        Write-Host "PACKER: Created packaging output folder
"
        New-Item -ItemType Directory -Path "$PSScriptRoot/packaging/"
    }
    if($clean -eq "true")
    {
        Write-Information "PACKER: Removing packages"
        Remove-Item -Path "$PSScriptRoot/packaging/*.*" -recurse
    }

    if($pack -eq 'true')
    {
        try
        {
            if(dotnet nuget list source | Select-String -Pattern 'DILocalPackages')
            {
                dotnet nuget add source $PSScriptRoot/packaging -n DILocalPackages
            }
        
            dotnet restore

            Write-Host "PACKER: Packaging projects
"
            dotnet pack $PSScriptRoot/SourceCrafter.DependencyInjection.Interop/SourceCrafter.DependencyInjection.Interop.csproj -c Release -p:PackageVersion=$version
            dotnet pack $PSScriptRoot/SourceCrafter.DependencyInjection/SourceCrafter.DependencyInjection.csproj -c Release -p:PackageVersion=$version
            dotnet pack $PSScriptRoot/SourceCrafter.DependencyInjection.MsConfiguration/SourceCrafter.DependencyInjection.MsConfiguration.csproj -c Release -p:PackageVersion=$version
            dotnet pack $PSScriptRoot/SourceCrafter.DependencyInjection.MsConfiguration.Metadata/SourceCrafter.DependencyInjection.MsConfiguration.Metadata.csproj -c Release -p:PackageVersion=$version
        }
        catch
        {
            $exceptionString = $_.Exception.Message + " " + $_.InvocationInfo.PositionMessage
            Write-Error $exceptionString
        }
    }
}

if($test -eq 'true')
{
    Write-Output "
PACKER: Testin projects
"
    if($pack -ne 'true')
    {
        if(dotnet nuget list source | Select-String -Pattern 'LocalPackages')
        {
            dotnet nuget add source $PSScriptRoot/packaging -n DILocalPackages
        }        
        
        dotnet restore $PSScriptRoot/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj
    }
    
    if($clean -eq 'true')
    {
        dotnet clean $PSScriptRoot/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj -c Release
    }
    
    dotnet build $PSScriptRoot/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj -c Release
    dotnet test $PSScriptRoot/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj -c Release /v:d

}