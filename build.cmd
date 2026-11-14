@echo off
rem Runs the build pipeline (pipeline\GitSnapshotter.Pipeline). Arguments are passed through.
dotnet run --project "%~dp0pipeline\GitSnapshotter.Pipeline" -- %*
