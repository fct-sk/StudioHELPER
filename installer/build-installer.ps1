param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$project = Join-Path $repository "src\StudioHELPER.Windows\StudioHELPER.Windows.csproj"
$artifacts = Join-Path $repository "artifacts"
$payload = Join-Path $artifacts "payload"
$payloadZip = Join-Path $artifacts "StudioHELPER-payload.zip"
$setup = Join-Path $artifacts "StudioHELPER-Setup.exe"
$compiler = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) { throw "The .NET Framework C# compiler was not found." }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
New-Item -ItemType Directory -Path $payload -Force | Out-Null

dotnet restore $project --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $project -c $Configuration --self-contained false --no-restore -o $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $repository "README.md") -Destination (Join-Path $payload "README.md") -Force
Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $payloadZip -CompressionLevel Optimal -Force

& $compiler /nologo /target:winexe /optimize+ "/out:$setup" "/win32manifest:$(Join-Path $PSScriptRoot 'setup.manifest')" "/resource:$payloadZip,StudioHELPER.Payload" "/reference:System.Windows.Forms.dll" "/reference:System.Drawing.dll" "/reference:System.IO.Compression.dll" "/reference:System.IO.Compression.FileSystem.dll" "/reference:Microsoft.CSharp.dll" (Join-Path $PSScriptRoot "StudioHELPER.Setup.cs")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output $setup
