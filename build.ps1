param(
    [switch]$Testing,
    [string]$Target
)

$ErrorActionPreference = 'Stop'
if (!$Target) { $Target = Join-Path $PSScriptRoot 'SpaceFix.exe' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$data = Join-Path $PSScriptRoot 'data'
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$options = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/warnaserror+',
    "/out:$Target", "/win32manifest:$PSScriptRoot\app.manifest", "/win32icon:$PSScriptRoot\app.ico",
    "/resource:$PSScriptRoot\app-logo.png,app.logo",
    "/reference:$framework\WPF\UIAutomationClient.dll", "/reference:$framework\WPF\UIAutomationTypes.dll",
    "/reference:$framework\WPF\WindowsBase.dll",
    "/resource:$data\en.gz,words.en.gz", "/resource:$data\ru.gz,words.ru.gz", "/resource:$data\kk.gz,words.kk.gz",
    "/resource:$data\ru-typos-kartaslov.gz,words.ru-typos-kartaslov.gz",
    "/resource:$data\ru-typos-ruspellru.gz,words.ru-typos-ruspellru.gz",
    "/resource:$data\en-typos-toefl.gz,words.en-typos-toefl.gz",
    "/resource:$data\kk-typos-sauatai.gz,words.kk-typos-sauatai.gz",
    "/resource:$data\kk-russian-letters-cefr.gz,words.kk-russian-letters-cefr.gz",
    "/resource:$data\kk-russian-letters-myspell.gz,words.kk-russian-letters-myspell.gz",
    "/resource:$data\credits.txt,credits", "/resource:$data\CC-BY-SA-4.0.txt,license.cc",
    "/resource:$data\MPL-1.1.txt,license.mpl", "/resource:$data\CC-BY-NC-SA-4.0.txt,license.typos.cc",
    "/resource:$data\RUSpellRU-MIT.txt,license.typos.mit", "/resource:$data\CC-BY-4.0.txt,license.typos.kk")
if ($Testing) { $options += '/define:TESTING' }
& "$framework\csc.exe" @options @sources
if ($LASTEXITCODE -ne 0) { throw "Compiler exited with code $LASTEXITCODE" }
