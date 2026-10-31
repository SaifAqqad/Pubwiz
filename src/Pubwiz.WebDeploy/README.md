# Pubwiz.WebDeploy

A cross-platform client for IIS Web Deploy (the `msdeploy.axd` handler of the Web Management Service, WMSVC). It deploys a folder to an IIS site from Linux, macOS or Windows, without `msdeploy.exe`.

> **Status:** work in progress. Endpoint handling and server error decoding are done; the sync protocol is being reverse-engineered (see `docs/webdeploy-protocol.md` in the repository).

Used by [Pubwiz](https://github.com/SaifAqqad/Pubwiz) (`dotnet pubwiz`), and usable on its own.
