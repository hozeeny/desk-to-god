[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '美术Demo/launch-demo.ps1') -Profile 'player'
