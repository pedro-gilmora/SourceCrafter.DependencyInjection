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

function Get-Env-Vars
{
    # Define your environment variable string
    $envFile = Get-Content ".\msbuildProps"

    # Convert the string into an array of lines
    $envVarsArray = $envVarsString -split "`n"

    # Iterate over each line and set the environment variables
    foreach ($line in $envVarsArray) {
        # Trim any extra whitespace from the line
        $line = $line.Trim()
    
        # Skip empty lines and lines that don't contain an equals sign
        if ($line -match '^(.+)=(.+)$') {
            # Extract the variable name and value
            $name = $matches[1].Trim()
            $value = $matches[2].Trim()
        
            # Set the environment variable in the current PowerShell session
            [System.Environment]::SetEnvironmentVariable($name, $value, [System.EnvironmentVariableTarget]::Process)
        }
    }
}

Get-Env-Vars

$testProjPath = ".\SourceCrafter.DependencyInjection.Tests\SourceCrafter.DependencyInjection.Tests.csproj"

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
    if($clean = "true")
    {
        Write-Information "PACKER: Removing packages"
        Remove-Item -Path ".\packaging\*.*" -recurse
    }

    if($pack -eq 'true')
    {
        try
        {
            Write-Host "PACKER: Packaging projects
"
            dotnet pack .\SourceCrafter.DependencyInjection.Interop\SourceCrafter.DependencyInjection.Interop.csproj -c Release -o .\packaging -p:PackageVersion=$version
            dotnet pack .\SourceCrafter.DependencyInjection\SourceCrafter.DependencyInjection.csproj -c Release -o .\packaging -p:PackageVersion=$version
            dotnet pack .\SourceCrafter.DependencyInjection.MsConfiguration\SourceCrafter.DependencyInjection.MsConfiguration.csproj -c Release -o .\packaging -p:PackageVersion=$version
            dotnet pack .\SourceCrafter.DependencyInjection.MsConfiguration.Metadata\SourceCrafter.DependencyInjection.MsConfiguration.Metadata.csproj -c Release -o .\packaging -p:PackageVersion=$version
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
    dotnet msbuild .\SourceCrafter.DependencyInjection.Tests\SourceCrafter.DependencyInjection.Tests.csproj /t:Clean /p:Configuration=Release
    dotnet msbuild .\SourceCrafter.DependencyInjection.Tests\SourceCrafter.DependencyInjection.Tests.csproj /t:Build /p:Configuration=Release
    dotnet msbuild .\SourceCrafter.DependencyInjection.Tests\SourceCrafter.DependencyInjection.Tests.csproj /t:VSTest /p:Configuration=Release /v:detailed > log

}