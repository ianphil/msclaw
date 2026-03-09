```pwsh
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.devtunnel
devtunnel user login
dotnet tool install --global MsClaw --version 0.7.2
cd \src
msclaw mind scaffold .\skippy
msclaw mind validate .\skippy
msclaw auth login

npx mcporter list
iex "& { $(irm aka.ms/InstallTool.ps1)} agency"

msclaw\docs\bootstrap-teams.md

msclaw start --mind .\skippy\
```