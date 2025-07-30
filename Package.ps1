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
    [string]$startingYear = "2024",
    [Parameter(Mandatory=$false)]
    [string]$specificVersion = $null
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

Write-Host "Current path: $PWD"

Set-Location "$PWD"

$testProjPath = "$PWD/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj"

$testProjContent = [xml]$(Get-Content $testProjPath)

# Get all 'PackageReference' nodes
$refs = $($testProjContent).GetElementsByTagName('PackageReference').
    Where({ 
        $_.GetAttribute('Include').StartsWith('SourceCrafter.DependencyInjection') -and $_.GetAttribute('Version') -ne $version
    })

$version = if ($specificVersion) { $specificVersion } else { Get-Version }

Write-Host "CONFIG: version = $version, clean = $clean, pack = $pack, forcePack = $forcePack, test = $test, startingYear = $startingYear, $$refs.Count = $($refs.Count)
"

if($refs.Count -gt 0 -or $forcePack -eq 'true')
{    
    $refs.Foreach({ 
        Write-Output "
PACKER: Updating package: $($_.GetAttribute('Include')) to version $version"
        $_.SetAttribute('Version', "[$version]")
        Write-Output $_.OuterXml
    })

    $testProjContent.Save($testProjPath)

    Write-Output "
PACKER: Test project references where updated
"
    if(-not (Test-Path "$PWD/packaging/"))
    {
        Write-Host "PACKER: Created packaging output folder
"
        New-Item -ItemType Directory -Path "$PWD/packaging/"
    }

    if($clean -eq "true")
    {
        Write-Information "PACKER: Removing packages"
        Remove-Item -Path "$PWD/packaging/*.*" -recurse
    }

    if($pack -eq 'true')
    {
    Write-Output "
PACKER: Initializing...
"
        try
        {
            if(-not (dotnet nuget list source | Select-String -Pattern 'DILocalPackages'))
            {
                Write-Output '
    PACKER: Creating local source "DILocalPackages"...
'
                dotnet nuget add source "$PWD/packaging" -n DILocalPackages
            }
            
            Write-Output '
PACKER: Restoring...
'        
            dotnet restore

            Write-Host "PACKER: Packaging projects
"
            dotnet pack $PWD/SourceCrafter.DependencyInjection/SourceCrafter.DependencyInjection.csproj -c Release -p:PackageVersion=$version
            dotnet pack $PWD/SourceCrafter.DependencyInjection.MsConfiguration/SourceCrafter.DependencyInjection.MsConfiguration.csproj -c Release -p:PackageVersion=$version
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
PACKER: Testing projects
"
    if($pack -ne 'true')
    {
        if(dotnet nuget list source | Select-String -Pattern 'LocalPackages')
        {
            dotnet nuget add source $PWD/packaging -n DILocalPackages
        }        
        
        dotnet restore $PWD/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj
    }
    
    if($clean -eq 'true')
    {
        dotnet clean $PWD/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj -c Release
    }
    
    dotnet build $PWD/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj -c Release
    dotnet test $PWD/SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj -c Release /v:d
}