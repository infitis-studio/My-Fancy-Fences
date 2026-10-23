# Release conventions

- Normalize new release versions to `vMAJOR.MINOR.PATCH` for both the Git tag and GitHub release title. For example, a request for `4.5` means `v4.5.0`. Do not prefix titles with the application name. Preserve existing historical tags and download URLs.
- Before publishing, compare the previous release tag with the intended release commit. Write English notes in `.github/release-notes/<tag>.md` describing only that interval.
- Use Added only for a feature introduced in that release. Describe subsequent changes as improvements or fixes; never copy a cumulative feature list into a new release.
- Update the project version to match the release. Commit the release notes before tagging. The release workflow requires the matching notes file.
- Check the published title, notes, assets, and latest-release designation after publishing. Preserve prerelease status unless explicitly changing the release channel.
- Provide the Windows x64 Setup EXE built with `installer/Build-Installer.ps1`. Setup installs the application and downloads Microsoft .NET 10 Desktop Runtime only when missing or when repair is selected. Publish only the Setup EXE: no portable single-file EXE and no installer payload as a separate download.
